#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MaidCafe.Audio
{
    /// <summary>
    /// WebGL implementation of <see cref="VAudioNative"/>: the same functions, backed by the Vercidium Audio WebAssembly
    /// build through Plugins/WebGL/VAudioWeb.jslib. "Pointers" are integer handles issued by the bridge (never 0).
    /// Functions that return pointers to SDK-owned memory in the native library (EAX, target filter, voxel data) return
    /// buffers owned by this class instead; callers read them immediately, exactly as they do with the native library.
    /// Not available in the browser build: log callbacks, world names, keep-reverb-tail-alive (no-ops returning
    /// Success) and the debug window (returns an error).
    /// </summary>
    public static partial class VAudioNative
    {
        private const int ErrorGeneric = 1;

        [DllImport("__Internal")] private static extern int VAW_Status();
        [DllImport("__Internal")] private static extern string VAW_StatusMessage();
        [DllImport("__Internal")] private static extern int VAW_ThreadCount();
        [DllImport("__Internal")] private static extern int VAW_WorldCreate();
        [DllImport("__Internal")] private static extern int VAW_WorldDestroy(int world);
        [DllImport("__Internal")] private static extern int VAW_WorldUpdate(int world);
        [DllImport("__Internal")] private static extern int VAW_WorldThreadsRunning(int world);
        [DllImport("__Internal")] private static extern int VAW_WorldSetVector(int world, int which, float x, float y, float z);
        [DllImport("__Internal")] private static extern int VAW_WorldSetFloat(int world, int which, float value);
        [DllImport("__Internal")] private static extern int VAW_WorldSetInt(int world, int which, int value);
        [DllImport("__Internal")] private static extern double VAW_WorldGetDouble(int world, int which);
        [DllImport("__Internal")] private static extern int VAW_WorldEmitterCount(int world);
        [DllImport("__Internal")] private static extern int VAW_WorldAddPrimitive(int world, int primitive);
        [DllImport("__Internal")] private static extern int VAW_WorldRemovePrimitive(int world, int primitive);
        [DllImport("__Internal")] private static extern int VAW_WorldAddEmitter(int world, int emitter);
        [DllImport("__Internal")] private static extern int VAW_WorldRemoveEmitter(int world, int emitter);
        [DllImport("__Internal")] private static extern int VAW_WorldCreateMaterial(int world, int materialId);
        [DllImport("__Internal")] private static extern int VAW_WorldSetMaterialFloat(int world, int materialId, int which, float value);
        [DllImport("__Internal")] private static extern int VAW_EmitterCreate();
        [DllImport("__Internal")] private static extern int VAW_EmitterDestroy(int emitter);
        [DllImport("__Internal")] private static extern int VAW_EmitterTarget(int emitter, int target, int op);
        [DllImport("__Internal")] private static extern int VAW_EmitterGetTargetFilter(int emitter, int target, IntPtr outFilter);
        [DllImport("__Internal")] private static extern int VAW_EmitterSetPosition(int emitter, float x, float y, float z);
        [DllImport("__Internal")] private static extern int VAW_EmitterSetInt(int emitter, int which, int value);
        [DllImport("__Internal")] private static extern int VAW_EmitterSetFloat(int emitter, int which, float value);
        [DllImport("__Internal")] private static extern int VAW_EmitterSetName(int emitter, string name);
        [DllImport("__Internal")] private static extern int VAW_EmitterGetEAX(int emitter, float[] values);
        [DllImport("__Internal")] private static extern int VAW_VoxelCreate(int width, int height, int depth);
        [DllImport("__Internal")] private static extern int VAW_VoxelSetData(int voxel, int[] data, int count);
        [DllImport("__Internal")] private static extern int VAW_PrismCreate();
        [DllImport("__Internal")] private static extern int VAW_PrismSetSize(int prism, float x, float y, float z);
        [DllImport("__Internal")] private static extern int VAW_PrimitiveSetMaterial(int primitive, int material);
        [DllImport("__Internal")] private static extern int VAW_PrimitiveSetTransform(int primitive, float[] matrix);
        [DllImport("__Internal")] private static extern int VAW_PrimitiveDestroy(int primitive);

        /// <summary>Bridge state: 1 ready, 0 still loading (the page loads the SDK asynchronously), -1 failed.</summary>
        public static int WebStatus => VAW_Status();

        /// <summary>Why the SDK failed to load in the page (empty while loading or when ready).</summary>
        public static string WebStatusMessage => VAW_StatusMessage();

        /// <summary>Raytracing worker threads the page reserved (SystemInfo.processorCount is not reliable in a browser).</summary>
        public static int WebThreadCount => VAW_ThreadCount();

        private static int H(IntPtr p) => p.ToInt32();
        private static IntPtr P(int h) => new IntPtr(h);

        // ------------------------------------------------------------------ version

        public static void vaGetVersion(out int major, out int minor, out int patch)
        {
            major = 1;
            minor = 11;
            patch = 1;
        }

        // ------------------------------------------------------------------ world

        public static IntPtr vaWorldCreate() => P(VAW_WorldCreate());
        public static int vaWorldDestroy(IntPtr world) => VAW_WorldDestroy(H(world));
        public static int vaWorldUpdate(IntPtr world) => VAW_WorldUpdate(H(world));
        public static int vaWorldWait(IntPtr world) => Success;
        public static bool vaWorldGetThreadsRunning(IntPtr world) => VAW_WorldThreadsRunning(H(world)) != 0;
        public static int vaWorldSetPosition(IntPtr world, VAVector p) => VAW_WorldSetVector(H(world), 0, p.x, p.y, p.z);
        public static int vaWorldSetSize(IntPtr world, VAVector s) => VAW_WorldSetVector(H(world), 1, s.x, s.y, s.z);
        public static int vaWorldSetName(IntPtr world, string name) => Success;
        public static int vaWorldSetMaximumConcurrencyLevel(IntPtr world, int value) => VAW_WorldSetInt(H(world), 0, Math.Min(value, VAW_ThreadCount()));
        public static int vaWorldSetMetersPerUnit(IntPtr world, float value) => VAW_WorldSetFloat(H(world), 0, value);
        public static int vaWorldSetCoordinateSystem(IntPtr world, int value) => VAW_WorldSetInt(H(world), 1, value);
        public static int vaWorldSetEmittersOutsideTheWorldAreMuffled(IntPtr world, bool value) => VAW_WorldSetInt(H(world), 2, value ? 1 : 0);
        public static int vaWorldSetRenderingEnabled(IntPtr world, bool value) => value ? VAW_WorldSetInt(H(world), 3, 1) : Success;
        public static int vaWorldSetManualCamera(IntPtr world, bool value) => VAW_WorldSetInt(H(world), 4, value ? 1 : 0);
        public static int vaWorldSetCameraPosition(IntPtr world, VAVector p) => VAW_WorldSetVector(H(world), 2, p.x, p.y, p.z);
        public static int vaWorldSetCameraPitch(IntPtr world, float pitch) => VAW_WorldSetFloat(H(world), 1, pitch);
        public static int vaWorldSetCameraYaw(IntPtr world, float yaw) => VAW_WorldSetFloat(H(world), 2, yaw);
        public static int vaWorldSetLogCallback(IntPtr world, WorldLogCallback callback) => Success;
        public static int vaWorldSetLogMemoryAllocationWarnings(IntPtr world, bool value) => VAW_WorldSetInt(H(world), 5, value ? 1 : 0);
        public static int vaWorldAddPrimitive_(IntPtr world, IntPtr primitive) => VAW_WorldAddPrimitive(H(world), H(primitive));
        public static int vaWorldRemovePrimitive_(IntPtr world, IntPtr primitive) => VAW_WorldRemovePrimitive(H(world), H(primitive));
        public static int vaWorldAddEmitter(IntPtr world, IntPtr emitter) => VAW_WorldAddEmitter(H(world), H(emitter));
        public static int vaWorldRemoveEmitter(IntPtr world, IntPtr emitter) => VAW_WorldRemoveEmitter(H(world), H(emitter));
        public static int vaWorldGetEmitterCount(IntPtr world) => VAW_WorldEmitterCount(H(world));
        public static int vaWorldGetRaysCastThisFrame(IntPtr world) => 0;
        public static double vaWorldGetRaytracingTime(IntPtr world) => VAW_WorldGetDouble(H(world), 0);
        public static double vaWorldGetPreparationTime(IntPtr world) => VAW_WorldGetDouble(H(world), 1);
        public static double vaWorldGetMainThreadTime(IntPtr world) => VAW_WorldGetDouble(H(world), 2);
        public static int vaWorldCreateMaterial(IntPtr world, int materialId) => VAW_WorldCreateMaterial(H(world), materialId);
        public static int vaWorldSetMaterialAbsorptionLF(IntPtr world, int materialId, float value) => VAW_WorldSetMaterialFloat(H(world), materialId, 0, value);
        public static int vaWorldSetMaterialAbsorptionHF(IntPtr world, int materialId, float value) => VAW_WorldSetMaterialFloat(H(world), materialId, 1, value);
        public static int vaWorldSetMaterialScattering(IntPtr world, int materialId, float value) => VAW_WorldSetMaterialFloat(H(world), materialId, 2, value);
        public static int vaWorldSetMaterialTransmissionLF(IntPtr world, int materialId, float meters) => VAW_WorldSetMaterialFloat(H(world), materialId, 3, meters);
        public static int vaWorldSetMaterialTransmissionHF(IntPtr world, int materialId, float meters) => VAW_WorldSetMaterialFloat(H(world), materialId, 4, meters);

        // ------------------------------------------------------------------ emitter

        private static IntPtr _filterBuffer;
        private static IntPtr _eaxBuffer;
        private static readonly float[] EaxValues = new float[26];

        public static IntPtr vaEmitterCreate() => P(VAW_EmitterCreate());
        public static int vaEmitterDestroy(IntPtr emitter) => VAW_EmitterDestroy(H(emitter));
        public static int vaEmitterAddTarget(IntPtr emitter, IntPtr target) => VAW_EmitterTarget(H(emitter), H(target), 0);
        public static int vaEmitterRemoveTarget(IntPtr emitter, IntPtr target) => VAW_EmitterTarget(H(emitter), H(target), 1);
        public static int vaEmitterHasRaytracedTarget(IntPtr emitter, IntPtr target) => VAW_EmitterTarget(H(emitter), H(target), 2);

        /// <summary>outFilter points to a buffer shared by all calls: read it before the next call.</summary>
        public static int vaEmitterGetTargetFilter(IntPtr emitter, IntPtr target, out IntPtr outFilter)
        {
            if (_filterBuffer == IntPtr.Zero) _filterBuffer = Marshal.AllocHGlobal(8);
            int r = VAW_EmitterGetTargetFilter(H(emitter), H(target), _filterBuffer);
            outFilter = r == Success ? _filterBuffer : IntPtr.Zero;
            return r;
        }

        public static int vaEmitterSetPosition(IntPtr emitter, VAVector p) => VAW_EmitterSetPosition(H(emitter), p.x, p.y, p.z);
        public static int vaEmitterSetName(IntPtr emitter, string name) => VAW_EmitterSetName(H(emitter), name ?? string.Empty);
        public static int vaEmitterSetReverbRayCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 0, value);
        public static int vaEmitterSetReverbBounceCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 1, value);
        public static int vaEmitterSetReverbEnergyCap(IntPtr emitter, float value) => VAW_EmitterSetFloat(H(emitter), 0, value);
        public static int vaEmitterSetOcclusionRayCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 2, value);
        public static int vaEmitterSetOcclusionBounceCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 3, value);
        public static int vaEmitterSetPermeationRayCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 4, value);
        public static int vaEmitterSetPermeationBounceCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 5, value);
        public static int vaEmitterSetAmbientOcclusionRayCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 6, value);
        public static int vaEmitterSetAmbientOcclusionBounceCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 7, value);
        public static int vaEmitterSetAmbientPermeationRayCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 8, value);
        public static int vaEmitterSetAmbientPermeationBounceCount(IntPtr emitter, int value) => VAW_EmitterSetInt(H(emitter), 9, value);
        public static int vaEmitterSetMaxEchogramTime(IntPtr emitter, int milliseconds) => VAW_EmitterSetInt(H(emitter), 10, milliseconds);
        public static int vaEmitterSetEchogramGranularity(IntPtr emitter, int milliseconds) => VAW_EmitterSetInt(H(emitter), 11, milliseconds);
        public static int vaEmitterSetClampPosition(IntPtr emitter, bool value) => VAW_EmitterSetInt(H(emitter), 12, value ? 1 : 0);
        public static int vaEmitterSetKeepReverbTailAlive(IntPtr emitter, bool value) => Success;
        public static int vaEmitterSetAffectsGroupedEAX(IntPtr emitter, bool value) => VAW_EmitterSetInt(H(emitter), 13, value ? 1 : 0);
        public static int vaEmitterSetLogCallback(IntPtr emitter, EmitterLogCallback callback) => Success;
        public static int vaEmitterSetLogErrorCallback(IntPtr emitter, EmitterLogCallback callback) => Success;

        /// <summary>Pointer to a VAEAXReverb shared by all calls (read it before the next call), or zero until traced.</summary>
        public static IntPtr vaEmitterGetEAX(IntPtr emitter)
        {
            if (VAW_EmitterGetEAX(H(emitter), EaxValues) == 0) return IntPtr.Zero;
            var v = EaxValues;
            var e = new VAEAXReverb
            {
                outsidePercent = v[0], returnedPercent = v[1], materialAbsorptionLF = v[2], materialAbsorptionHF = v[3],
                materialRoughness = v[4], reflectionsDelay = v[5], density = v[6], diffusion = v[7], gainLF = v[8],
                gainHF = v[9], gain = v[10], decayTime = v[11], decayLFRatio = v[12], decayHFRatio = v[13],
                reflectionsGain = v[14], lateReverbGain = v[15], lateReverbDelay = v[16], echoTime = v[17],
                echoDepth = v[18], modulationTime = v[19], modulationDepth = v[20], airAbsorptionGainHF = v[21],
                hfReference = v[22], lfReference = v[23], roomRolloffFactor = v[24], decayHFLimit = v[25] != 0f ? 1 : 0
            };
            if (_eaxBuffer == IntPtr.Zero) _eaxBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<VAEAXReverb>());
            Marshal.StructureToPtr(e, _eaxBuffer, false);
            return _eaxBuffer;
        }

        // ------------------------------------------------------------------ voxel primitive

        /// <summary>Caller-visible voxel memory in the native layout ((x * height + y) * depth + z).</summary>
        private sealed class VoxelData
        {
            public int Width, Height, Depth;
            public IntPtr Buffer;
            public int[] Native, Web;
        }

        private static readonly Dictionary<int, VoxelData> Voxels = new Dictionary<int, VoxelData>();
        private static readonly float[] MatrixValues = new float[16];

        public static IntPtr vaVoxelPrimitiveCreate(int width, int height, int depth)
        {
            int h = VAW_VoxelCreate(width, height, depth);
            if (h != 0) Voxels[h] = new VoxelData { Width = width, Height = height, Depth = depth };
            return P(h);
        }

        public static int vaVoxelPrimitiveDestroy(IntPtr voxel)
        {
            int r = VAW_PrimitiveDestroy(H(voxel));
            if (r == Success && Voxels.TryGetValue(H(voxel), out var d))
            {
                if (d.Buffer != IntPtr.Zero) Marshal.FreeHGlobal(d.Buffer);
                Voxels.Remove(H(voxel));
            }
            return r;
        }

        public static int vaVoxelPrimitiveSetMaterial(IntPtr voxel, int material) => VAW_PrimitiveSetMaterial(H(voxel), material);
        public static int vaVoxelPrimitiveSetScale(IntPtr voxel, float scale) => ErrorGeneric;
        public static int vaVoxelPrimitiveSetTransform(IntPtr voxel, ref VAMatrix transform) => VAW_PrimitiveSetTransform(H(voxel), Flatten(ref transform));

        public static int vaVoxelPrimitiveGetSize(IntPtr voxel, out int width, out int height, out int depth)
        {
            if (Voxels.TryGetValue(H(voxel), out var d))
            {
                width = d.Width;
                height = d.Height;
                depth = d.Depth;
                return Success;
            }
            width = height = depth = 0;
            return ErrorGeneric;
        }

        /// <summary>Same layout as the native library; the data reaches the SDK on <see cref="vaVoxelPrimitiveSetDataDirty"/>.</summary>
        public static IntPtr vaVoxelPrimitiveGetVoxelData(IntPtr voxel)
        {
            if (!Voxels.TryGetValue(H(voxel), out var d)) return IntPtr.Zero;
            if (d.Buffer == IntPtr.Zero)
            {
                int count = d.Width * d.Height * d.Depth;
                d.Buffer = Marshal.AllocHGlobal(count * 4);
                d.Native = new int[count];
                Marshal.Copy(d.Native, 0, d.Buffer, count); // all air
            }
            return d.Buffer;
        }

        /// <summary>Copies the caller's voxel memory to the SDK, reordered to x + y * width + z * width * height.</summary>
        public static int vaVoxelPrimitiveSetDataDirty(IntPtr voxel)
        {
            if (!Voxels.TryGetValue(H(voxel), out var d) || d.Buffer == IntPtr.Zero) return ErrorGeneric;
            int w = d.Width, h = d.Height, dep = d.Depth, count = w * h * dep;
            Marshal.Copy(d.Buffer, d.Native, 0, count);
            if (d.Web == null) d.Web = new int[count];
            int[] src = d.Native, dst = d.Web;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int s = (x * h + y) * dep;
                    int t = x + y * w;
                    for (int z = 0; z < dep; z++) dst[t + z * w * h] = src[s + z];
                }
            }
            return VAW_VoxelSetData(H(voxel), dst, count);
        }

        // ------------------------------------------------------------------ prism primitive

        public static IntPtr vaPrismPrimitiveCreate() => P(VAW_PrismCreate());
        public static int vaPrismPrimitiveDestroy(IntPtr prism) => VAW_PrimitiveDestroy(H(prism));
        public static int vaPrismPrimitiveSetMaterial(IntPtr prism, int material) => VAW_PrimitiveSetMaterial(H(prism), material);
        public static int vaPrismPrimitiveSetSize(IntPtr prism, VAVector size) => VAW_PrismSetSize(H(prism), size.x, size.y, size.z);
        public static int vaPrismPrimitiveSetTransform(IntPtr prism, ref VAMatrix transform) => VAW_PrimitiveSetTransform(H(prism), Flatten(ref transform));

        // ------------------------------------------------------------------ matrix

        public static VAMatrix vaMatrixCreateTranslation(float x, float y, float z) => new VAMatrix
        {
            m11 = 1f, m22 = 1f, m33 = 1f, m44 = 1f,
            m41 = x, m42 = y, m43 = z
        };

        private static float[] Flatten(ref VAMatrix m)
        {
            var v = MatrixValues;
            v[0] = m.m11; v[1] = m.m12; v[2] = m.m13; v[3] = m.m14;
            v[4] = m.m21; v[5] = m.m22; v[6] = m.m23; v[7] = m.m24;
            v[8] = m.m31; v[9] = m.m32; v[10] = m.m33; v[11] = m.m34;
            v[12] = m.m41; v[13] = m.m42; v[14] = m.m43; v[15] = m.m44;
            return v;
        }
    }
}
#endif
