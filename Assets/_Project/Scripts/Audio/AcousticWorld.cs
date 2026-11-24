using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MaidCafe.Generation;
using MaidCafe.Player;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MaidCafe.Audio
{
    /// <summary>Something that owns emitters: pushes positions before the raytracing update and reads results after it.</summary>
    internal interface IAcousticClient
    {
        void SyncToWorld();
        void ReadFromWorld();
    }

    /// <summary>Managed handle to a native emitter. Becomes invalid when released or when the world shuts down.</summary>
    public sealed class AcousticEmitter
    {
        internal IntPtr Ptr;
        internal Vector3 LastPosition = new Vector3(float.NaN, float.NaN, float.NaN);
        internal readonly List<AcousticEmitter> Targets = new List<AcousticEmitter>();
        internal readonly List<AcousticEmitter> TargetedBy = new List<AcousticEmitter>();

        public string Name { get; }
        public bool Valid => Ptr != IntPtr.Zero;

        internal AcousticEmitter(IntPtr ptr, string name)
        {
            Ptr = ptr;
            Name = name;
        }
    }

    /// <summary>Ray budget of an emitter. Emitters that are only heard (sound sources) cast no rays.</summary>
    public struct EmitterSettings
    {
        public int OcclusionRays, OcclusionBounces;
        public int PermeationRays, PermeationBounces;
        public int ReverbRays, ReverbBounces;
        public float ReverbEnergyCap;

        /// <summary>A sound source or a body that others listen to: casts no rays of its own.</summary>
        public static EmitterSettings Target => default;

        /// <summary>The player's ear: muffling towards every sound source plus the room reverb.</summary>
        public static EmitterSettings PlayerEar => new EmitterSettings
        {
            OcclusionRays = 192, OcclusionBounces = 6,
            PermeationRays = 48, PermeationBounces = 3,
            ReverbRays = 64, ReverbBounces = 32, ReverbEnergyCap = 0.2f
        };

        /// <summary>A maid's ear: how much of the player's (or a noise's) sound reaches her.</summary>
        public static EmitterSettings MaidEar => new EmitterSettings
        {
            OcclusionRays = 96, OcclusionBounces = 6,
            PermeationRays = 24, PermeationBounces = 2
        };
    }

    /// <summary>
    /// Owns the Vercidium Audio raytracing world (native SDK, CPU background threads). Every floor is copied into the
    /// world as one voxel grid (<see cref="AcousticVoxelizer"/>) plus thin moving boxes for elevator doors and locker
    /// panels. Emitters are created here and handed out as <see cref="AcousticEmitter"/> handles; clients push their
    /// positions before and read their filters after the once-per-frame update (this component runs last).
    /// Without the native library (<see cref="LibraryAvailable"/> false) nothing is created and clients fall back to
    /// simple line-of-sight occlusion.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class AcousticWorld : MonoBehaviour
    {
        public static AcousticWorld Instance { get; private set; }

        /// <summary>True when the native world is running.</summary>
        public static bool Available => Instance != null && Instance._world != IntPtr.Zero;

        public static string SdkVersion { get; private set; } = "n/a";

        /// <summary>
        /// The SDK world runs in voxel units (1 unit = one 0.25 m voxel) with MetersPerUnit = 0.25, so air absorption,
        /// reverb timing and material transmission stay in metres. Reason: in SDK 1.11.1 a VoxelPrimitive whose scale is
        /// not 1 is never hit by rays (measured), so the grid must keep scale 1. Every position/size sent to the SDK is
        /// multiplied by this factor.
        /// </summary>
        public const float UnitsPerMeter = 1f / AcousticVoxelizer.DefaultVoxelSize;

        private static VAudioNative.VAVector Sdk(Vector3 meters) => VAudioNative.VAVector.From(meters * UnitsPerMeter);

        private static bool? _libraryOk;
        private static readonly List<IAcousticClient> Clients = new List<IAcousticClient>();
        private static IAcousticClient[] _clientBuffer = new IAcousticClient[32];
        private static VAudioNative.WorldLogCallback _worldLog;
        private static VAudioNative.EmitterLogCallback _emitterLog;

        private IntPtr _world;
        private IntPtr _voxel;
        private VoxelGrid _grid;
        private int _serial;
        private bool _debugWindow;
        private FloorDirector _floors;

        private readonly List<AcousticEmitter> _emitters = new List<AcousticEmitter>();
        private readonly List<IntPtr> _pendingEmitters = new List<IntPtr>();
        private readonly List<PendingPrimitive> _pendingPrimitives = new List<PendingPrimitive>();
        private readonly List<DynamicBox> _dynamic = new List<DynamicBox>();

        private struct PendingPrimitive
        {
            public IntPtr Ptr;
            public bool Voxel;
            public int Serial;
        }

        private class DynamicBox
        {
            public BoxCollider Collider;
            public IntPtr Prism;
            public bool Added;
            public Vector3 Center, Size;
        }

        /// <summary>The player's ear (set by <see cref="RaytracedListener"/>). Sound sources attach to it as targets.</summary>
        public AcousticEmitter Listener { get; internal set; }

        /// <summary>Follows the player's chest; maids listen to it to hear the player's noises.</summary>
        public AcousticEmitter PlayerBody { get; private set; }

        public VoxelGrid Grid => _grid;

        /// <summary>Number of raytracing results handled so far (increments when a background cycle completes).</summary>
        public int Serial => _serial;

        public int PrismCount => _dynamic.Count;

        // ------------------------------------------------------------------ library

        /// <summary>True if vaudionative.dll could be loaded (checked once per session).</summary>
        public static bool LibraryAvailable
        {
            get
            {
                if (_libraryOk.HasValue) return _libraryOk.Value;
#if UNITY_WEBGL && !UNITY_EDITOR
                // Browser build: the page loads the SDK's WebAssembly build asynchronously (VAudioNativeWeb.cs). While it
                // is still loading the answer is "not yet" (not cached); see TryLateInit.
                int status = VAudioNative.WebStatus;
                if (status == 0) return false;
                VAudioNative.vaGetVersion(out int webMajor, out int webMinor, out int webPatch);
                SdkVersion = $"{webMajor}.{webMinor}.{webPatch} (WebAssembly)";
                _libraryOk = status == 1;
                if (status != 1)
                {
                    Debug.LogWarning("[AcousticWorld] Vercidium Audio WebAssembly build unavailable (" + VAudioNative.WebStatusMessage +
                                     "). Using line-of-sight audio instead. On itch.io, tick \"SharedArrayBuffer support\".");
                }
                return _libraryOk.Value;
#else
                try
                {
                    VAudioNative.vaGetVersion(out int major, out int minor, out int patch);
                    SdkVersion = $"{major}.{minor}.{patch}";
                    _libraryOk = true;
                }
                catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException)
                {
                    _libraryOk = false;
                    Debug.LogWarning("[AcousticWorld] Vercidium Audio native library not found (see README, \"Ray-traced audio SDK\"). " +
                                     "Using line-of-sight audio instead. " + ex.GetType().Name);
                }
                return _libraryOk.Value;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Clients.Clear();
        }

        internal static void Register(IAcousticClient client)
        {
            if (!Clients.Contains(client)) Clients.Add(client);
        }

        internal static void Unregister(IAcousticClient client) => Clients.Remove(client);

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (Instance != this) return;
            Init();
            _floors = FloorDirector.Instance != null ? FloorDirector.Instance : FindAnyObjectByType<FloorDirector>();
            if (_floors != null)
            {
                _floors.FloorBuilt += OnFloorBuilt;
                if (_floors.Current != null) OnFloorBuilt(_floors.Current);
            }
        }

        private void OnDisable()
        {
            if (_floors != null) _floors.FloorBuilt -= OnFloorBuilt;
            _floors = null;
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit() => Shutdown();

        private void Init()
        {
            if (_world != IntPtr.Zero || !LibraryAvailable) return;
            _world = VAudioNative.vaWorldCreate();
            if (_world == IntPtr.Zero)
            {
                Debug.LogWarning("[AcousticWorld] vaWorldCreate failed; using line-of-sight audio.");
                return;
            }

            if (_worldLog == null) _worldLog = OnWorldLog;
            if (_emitterLog == null) _emitterLog = OnEmitterLog;
            VAudioNative.vaWorldSetLogCallback(_world, _worldLog);
            // One notice per allocation (i.e. per new emitter / one-shot sound) would flood the console and the player log.
            VAudioNative.vaWorldSetLogMemoryAllocationWarnings(_world, false);
            VAudioNative.vaWorldSetName(_world, "MaidCafe");
            VAudioNative.vaWorldSetMetersPerUnit(_world, 1f / UnitsPerMeter);
            VAudioNative.vaWorldSetCoordinateSystem(_world, VAudioNative.CoordinateSystemUnity);
            VAudioNative.vaWorldSetMaximumConcurrencyLevel(_world, ThreadCount);
            VAudioNative.vaWorldSetEmittersOutsideTheWorldAreMuffled(_world, true);
            AcousticMaterials.Register(_world);

            PlayerBody = CreateEmitter("PlayerBody", EmitterSettings.Target, Vector3.up);
            Debug.Log($"[AcousticWorld] Vercidium Audio {SdkVersion} running ({ThreadCount} threads).");
        }

        /// <summary>
        /// WebGL: the SDK may still be loading when the game scene starts; create the world (and copy the current floor
        /// into it) as soon as it is ready. Clients create their emitters lazily, so they join on their own.
        /// </summary>
        private void TryLateInit()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (Instance != this || _libraryOk.HasValue || !LibraryAvailable) return;
            Init();
            if (_world != IntPtr.Zero && _floors != null && _floors.Current != null) OnFloorBuilt(_floors.Current);
#endif
        }

        /// <summary>Raytracing threads: half the cores minus one (1-4); in a browser, the workers the page reserved.</summary>
        private static int ThreadCount
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Mathf.Max(1, VAudioNative.WebThreadCount);
#else
                return Mathf.Clamp(SystemInfo.processorCount / 2 - 1, 1, 4);
#endif
            }
        }

        private void Shutdown()
        {
            if (_world == IntPtr.Zero) return;

            ClearFloorGeometry();
            VAudioNative.vaWorldDestroy(_world); // waits for the background threads
            _world = IntPtr.Zero;

            int leaked = 0;
            foreach (var e in _emitters)
            {
                if (VAudioNative.vaEmitterDestroy(e.Ptr) != VAudioNative.Success) leaked++;
                e.Ptr = IntPtr.Zero;
                e.Targets.Clear();
                e.TargetedBy.Clear();
            }
            _emitters.Clear();
            foreach (var p in _pendingEmitters)
            {
                if (VAudioNative.vaEmitterDestroy(p) != VAudioNative.Success) leaked++;
            }
            _pendingEmitters.Clear();
            foreach (var p in _pendingPrimitives)
            {
                int r = p.Voxel ? VAudioNative.vaVoxelPrimitiveDestroy(p.Ptr) : VAudioNative.vaPrismPrimitiveDestroy(p.Ptr);
                if (r != VAudioNative.Success) leaked++;
            }
            _pendingPrimitives.Clear();
            if (leaked > 0) Debug.LogWarning($"[AcousticWorld] {leaked} native objects could not be freed at shutdown.");

            Listener = null;
            PlayerBody = null;
            _grid = null;
        }

        // ------------------------------------------------------------------ update

        private void LateUpdate()
        {
            // Every Update has run (this component runs last): hand this frame's filters and reverb to the page (WebGL).
            WebAudioBridge.Flush();
            if (_world == IntPtr.Zero)
            {
                TryLateInit();
                return;
            }

            UpdatePlayerBody();
            int n = SnapshotClients();
            for (int i = 0; i < n; i++) _clientBuffer[i].SyncToWorld();
            SyncDynamicBoxes();

            if (VAudioNative.vaWorldUpdate(_world) == VAudioNative.Success) _serial++;

            n = SnapshotClients();
            for (int i = 0; i < n; i++)
            {
                // A callback may have destroyed a later client this frame.
                if (_clientBuffer[i] is UnityEngine.Object o && o == null) continue;
                _clientBuffer[i].ReadFromWorld();
            }
            ProcessPending();
            if (_debugWindow) SyncDebugCamera();
        }

        private static int SnapshotClients()
        {
            if (_clientBuffer.Length < Clients.Count) _clientBuffer = new IAcousticClient[Clients.Count * 2];
            Clients.CopyTo(_clientBuffer);
            return Clients.Count;
        }

        private void UpdatePlayerBody()
        {
            var player = PlayerContext.Instance;
            if (player == null || PlayerBody == null) return;
            float chest = player.Motor != null ? Mathf.Clamp(player.Motor.EyeHeight - 0.35f, 0.15f, 1.3f) : 1f;
            SetPosition(PlayerBody, player.transform.position + Vector3.up * chest);
        }

        private void ProcessPending()
        {
            for (int i = _pendingEmitters.Count - 1; i >= 0; i--)
            {
                if (VAudioNative.vaEmitterDestroy(_pendingEmitters[i]) != VAudioNative.ErrorInUse) _pendingEmitters.RemoveAt(i);
            }

            if (_pendingPrimitives.Count == 0 || VAudioNative.vaWorldGetThreadsRunning(_world)) return;
            for (int i = _pendingPrimitives.Count - 1; i >= 0; i--)
            {
                var p = _pendingPrimitives[i];
                if (_serial <= p.Serial) continue; // the threads may still hold it until one more cycle was handled
                int r = p.Voxel ? VAudioNative.vaVoxelPrimitiveDestroy(p.Ptr) : VAudioNative.vaPrismPrimitiveDestroy(p.Ptr);
                if (r != VAudioNative.ErrorInUse) _pendingPrimitives.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ emitters

        /// <summary>Creates an emitter in the world. Returns null when the world is not running.</summary>
        public AcousticEmitter CreateEmitter(string name, EmitterSettings s, Vector3 position)
        {
            if (_world == IntPtr.Zero) return null;
            IntPtr p = VAudioNative.vaEmitterCreate();
            if (p == IntPtr.Zero) return null;

            VAudioNative.vaEmitterSetName(p, name);
            VAudioNative.vaEmitterSetLogCallback(p, _emitterLog);
            VAudioNative.vaEmitterSetLogErrorCallback(p, _emitterLog);
            VAudioNative.vaEmitterSetClampPosition(p, true);
            VAudioNative.vaEmitterSetKeepReverbTailAlive(p, false);
            VAudioNative.vaEmitterSetAffectsGroupedEAX(p, false);
            VAudioNative.vaEmitterSetOcclusionRayCount(p, s.OcclusionRays);
            VAudioNative.vaEmitterSetOcclusionBounceCount(p, s.OcclusionBounces);
            VAudioNative.vaEmitterSetPermeationRayCount(p, s.PermeationRays);
            VAudioNative.vaEmitterSetPermeationBounceCount(p, s.PermeationBounces);
            VAudioNative.vaEmitterSetReverbRayCount(p, s.ReverbRays);
            VAudioNative.vaEmitterSetReverbBounceCount(p, s.ReverbBounces);
            if (s.ReverbRays > 0)
            {
                VAudioNative.vaEmitterSetReverbEnergyCap(p, s.ReverbEnergyCap);
                VAudioNative.vaEmitterSetMaxEchogramTime(p, 3000);
                VAudioNative.vaEmitterSetEchogramGranularity(p, 50);
            }
            VAudioNative.vaEmitterSetAmbientOcclusionRayCount(p, 0);
            VAudioNative.vaEmitterSetAmbientOcclusionBounceCount(p, 0);
            VAudioNative.vaEmitterSetAmbientPermeationRayCount(p, 0);
            VAudioNative.vaEmitterSetAmbientPermeationBounceCount(p, 0);
            VAudioNative.vaEmitterSetPosition(p, Sdk(position));

            int r = VAudioNative.vaWorldAddEmitter(_world, p);
            if (r != VAudioNative.Success)
            {
                Debug.LogWarning($"[AcousticWorld] vaWorldAddEmitter({name}) failed: {r}");
                VAudioNative.vaEmitterDestroy(p);
                return null;
            }
            var e = new AcousticEmitter(p, name) { LastPosition = position };
            _emitters.Add(e);
            return e;
        }

        /// <summary>Removes the emitter from the world (freed once the raytracing threads let go of it).</summary>
        public void Release(AcousticEmitter e)
        {
            if (e == null || !e.Valid || _world == IntPtr.Zero) return;
            foreach (var listener in e.TargetedBy)
            {
                if (listener.Valid) VAudioNative.vaEmitterRemoveTarget(listener.Ptr, e.Ptr);
                listener.Targets.Remove(e);
            }
            foreach (var target in e.Targets)
            {
                if (target.Valid) VAudioNative.vaEmitterRemoveTarget(e.Ptr, target.Ptr);
                target.TargetedBy.Remove(e);
            }
            e.Targets.Clear();
            e.TargetedBy.Clear();
            if (Listener == e) Listener = null;

            VAudioNative.vaWorldRemoveEmitter(_world, e.Ptr);
            _pendingEmitters.Add(e.Ptr);
            e.Ptr = IntPtr.Zero;
            _emitters.Remove(e);
        }

        public void SetPosition(AcousticEmitter e, Vector3 position)
        {
            if (e == null || !e.Valid) return;
            if ((position - e.LastPosition).sqrMagnitude < 0.0001f) return;
            e.LastPosition = position;
            VAudioNative.vaEmitterSetPosition(e.Ptr, Sdk(position));
        }

        /// <summary><paramref name="listener"/> starts casting occlusion/permeation rays towards <paramref name="target"/>.</summary>
        public bool AddTarget(AcousticEmitter listener, AcousticEmitter target)
        {
            if (listener == null || target == null || !listener.Valid || !target.Valid) return false;
            if (listener.Targets.Contains(target)) return true;
            int r = VAudioNative.vaEmitterAddTarget(listener.Ptr, target.Ptr);
            if (r != VAudioNative.Success && r != 3 /* VA_ALREADY_EXISTS */) return false;
            listener.Targets.Add(target);
            target.TargetedBy.Add(listener);
            return true;
        }

        public void RemoveTarget(AcousticEmitter listener, AcousticEmitter target)
        {
            if (listener == null || target == null || !listener.Targets.Contains(target)) return;
            if (listener.Valid && target.Valid) VAudioNative.vaEmitterRemoveTarget(listener.Ptr, target.Ptr);
            listener.Targets.Remove(target);
            target.TargetedBy.Remove(listener);
        }

        public static bool IsTargetOf(AcousticEmitter listener, AcousticEmitter target) =>
            listener != null && target != null && listener.Targets.Contains(target);

        /// <summary>Low/high-frequency gain (0–1) of <paramref name="target"/> as heard by <paramref name="listener"/>, once raytraced.</summary>
        public bool TryGetFilter(AcousticEmitter listener, AcousticEmitter target, out float gainLF, out float gainHF)
        {
            gainLF = gainHF = 0f;
            if (_world == IntPtr.Zero || listener == null || target == null || !listener.Valid || !target.Valid) return false;
            if (VAudioNative.vaEmitterHasRaytracedTarget(listener.Ptr, target.Ptr) != VAudioNative.True) return false;
            if (VAudioNative.vaEmitterGetTargetFilter(listener.Ptr, target.Ptr, out IntPtr filter) != VAudioNative.Success || filter == IntPtr.Zero) return false;
            var f = VAudioNative.ReadFilter(filter);
            if (float.IsNaN(f.gainLF) || float.IsNaN(f.gainHF)) return false;
            gainLF = Mathf.Clamp01(f.gainLF);
            gainHF = Mathf.Clamp01(f.gainHF);
            return true;
        }

        /// <summary>The emitter's EAX reverb parameters (false until its reverb rays were cast once).</summary>
        public bool TryGetEAX(AcousticEmitter e, out VAudioNative.VAEAXReverb eax)
        {
            eax = default;
            if (_world == IntPtr.Zero || e == null || !e.Valid) return false;
            IntPtr p = VAudioNative.vaEmitterGetEAX(e.Ptr);
            if (p == IntPtr.Zero) return false;
            eax = Marshal.PtrToStructure<VAudioNative.VAEAXReverb>(p);
            return true;
        }

        // ------------------------------------------------------------------ floor geometry

        private void OnFloorBuilt(FloorInstance floor)
        {
            if (_world == IntPtr.Zero || floor == null) return;
            var watch = Stopwatch.StartNew();
            ClearFloorGeometry();

            var boxes = AcousticVoxelizer.CollectStatic(floor);
            if (boxes.Count == 0) return;
            _grid = AcousticVoxelizer.Rasterize(boxes);

            VAudioNative.vaWorldSetPosition(_world, Sdk(_grid.Origin));
            VAudioNative.vaWorldSetSize(_world, Sdk(_grid.Size));

            // Voxel scale stays 1: the world runs in voxel units (see UnitsPerMeter).
            _voxel = VAudioNative.vaVoxelPrimitiveCreate(_grid.Width, _grid.Height, _grid.Depth);
            VAudioNative.vaVoxelPrimitiveSetMaterial(_voxel, VAudioNative.MaterialConcrete);
            Vector3 o = _grid.Origin * UnitsPerMeter;
            var m = VAudioNative.vaMatrixCreateTranslation(o.x, o.y, o.z);
            VAudioNative.vaVoxelPrimitiveSetTransform(_voxel, ref m);
            IntPtr data = VAudioNative.vaVoxelPrimitiveGetVoxelData(_voxel);
            Marshal.Copy(_grid.Data, 0, data, _grid.Data.Length);
            VAudioNative.vaVoxelPrimitiveSetDataDirty(_voxel);
            int r = VAudioNative.vaWorldAddPrimitive_(_world, _voxel);
            if (r != VAudioNative.Success) Debug.LogWarning($"[AcousticWorld] Adding the voxel grid failed: {r}");

            foreach (var (col, material) in AcousticVoxelizer.CollectDynamic(floor))
            {
                var box = new DynamicBox { Collider = col, Prism = VAudioNative.vaPrismPrimitiveCreate() };
                VAudioNative.vaPrismPrimitiveSetMaterial(box.Prism, material);
                SyncBox(box, force: true);
                _dynamic.Add(box);
            }

            Debug.Log($"[AcousticWorld] Floor voxelized: {_grid.Width}x{_grid.Height}x{_grid.Depth} @ {_grid.VoxelSize} m " +
                      $"({_grid.CountSolid()} solid), {_dynamic.Count} moving boxes, {watch.Elapsed.TotalMilliseconds:F1} ms.");
        }

        private void ClearFloorGeometry()
        {
            if (_world == IntPtr.Zero) return;
            if (_voxel != IntPtr.Zero)
            {
                VAudioNative.vaWorldRemovePrimitive_(_world, _voxel);
                _pendingPrimitives.Add(new PendingPrimitive { Ptr = _voxel, Voxel = true, Serial = _serial });
                _voxel = IntPtr.Zero;
            }
            foreach (var box in _dynamic) DropBox(box);
            _dynamic.Clear();
            _grid = null;
        }

        private void DropBox(DynamicBox box)
        {
            if (box.Added) VAudioNative.vaWorldRemovePrimitive_(_world, box.Prism);
            _pendingPrimitives.Add(new PendingPrimitive { Ptr = box.Prism, Voxel = false, Serial = _serial });
            box.Prism = IntPtr.Zero;
        }

        private void SyncDynamicBoxes()
        {
            for (int i = _dynamic.Count - 1; i >= 0; i--)
            {
                var box = _dynamic[i];
                if (box.Collider == null)
                {
                    DropBox(box);
                    _dynamic.RemoveAt(i);
                    continue;
                }
                SyncBox(box, force: false);
            }
        }

        private void SyncBox(DynamicBox box, bool force)
        {
            bool active = box.Collider.enabled && box.Collider.gameObject.activeInHierarchy;
            if (!active)
            {
                if (box.Added)
                {
                    VAudioNative.vaWorldRemovePrimitive_(_world, box.Prism);
                    box.Added = false;
                }
                return;
            }

            Bounds b = AcousticVoxelizer.WorldAabb(box.Collider);
            if (force || (b.center - box.Center).sqrMagnitude > 1e-6f || (b.size - box.Size).sqrMagnitude > 1e-6f)
            {
                box.Center = b.center;
                box.Size = b.size;
                VAudioNative.vaPrismPrimitiveSetSize(box.Prism, Sdk(b.size));
                Vector3 c = b.center * UnitsPerMeter;
                var m = VAudioNative.vaMatrixCreateTranslation(c.x, c.y, c.z);
                VAudioNative.vaPrismPrimitiveSetTransform(box.Prism, ref m);
            }
            if (!box.Added)
            {
                box.Added = VAudioNative.vaWorldAddPrimitive_(_world, box.Prism) == VAudioNative.Success;
            }
        }

        // ------------------------------------------------------------------ debugging

        /// <summary>Opens/closes the SDK's debug window (dev library = Editor only). The camera follows the player's view.</summary>
        public void SetDebugWindow(bool open)
        {
            if (_world == IntPtr.Zero) return;
            int r = VAudioNative.vaWorldSetRenderingEnabled(_world, open);
            _debugWindow = open && r == VAudioNative.Success;
            if (_debugWindow) VAudioNative.vaWorldSetManualCamera(_world, true);
            Debug.Log($"[AcousticWorld] Debug window {(open ? "open" : "closed")} (result {r}).");
        }

        public bool DebugWindowOpen => _debugWindow;

        private void SyncDebugCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var t = cam.transform;
            VAudioNative.vaWorldSetCameraPosition(_world, Sdk(t.position));
            Vector3 e = t.eulerAngles;
            float pitch = -Mathf.DeltaAngle(0f, e.x) * Mathf.Deg2Rad;
            VAudioNative.vaWorldSetCameraPitch(_world, pitch);
            VAudioNative.vaWorldSetCameraYaw(_world, e.y * Mathf.Deg2Rad);
        }

        /// <summary>One-line timing summary for the dev overlay / logs.</summary>
        public string Stats()
        {
            if (_world == IntPtr.Zero) return "acoustics: off";
            return $"acoustics: {VAudioNative.vaWorldGetEmitterCount(_world)} emitters, " +
                   $"raytrace {VAudioNative.vaWorldGetRaytracingTime(_world):F2} ms, prep {VAudioNative.vaWorldGetPreparationTime(_world):F2} ms, " +
                   $"main {VAudioNative.vaWorldGetMainThreadTime(_world):F3} ms, cycles {_serial}";
        }

        [AOT.MonoPInvokeCallback(typeof(VAudioNative.WorldLogCallback))]
        private static void OnWorldLog(IntPtr world, IntPtr message)
        {
            string text = Marshal.PtrToStringAnsi(message);
            if (string.IsNullOrEmpty(text)) return;
            if (text.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) Debug.LogWarning("[vaudio] " + text);
            else Debug.Log("[vaudio] " + text);
        }

        [AOT.MonoPInvokeCallback(typeof(VAudioNative.EmitterLogCallback))]
        private static void OnEmitterLog(IntPtr emitter, IntPtr message)
        {
            string text = Marshal.PtrToStringAnsi(message);
            if (!string.IsNullOrEmpty(text)) Debug.LogWarning("[vaudio emitter] " + text);
        }
    }
}
