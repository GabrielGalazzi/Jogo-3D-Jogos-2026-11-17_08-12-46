using System;
using System.Runtime.InteropServices;

namespace MaidCafe.Audio
{
    /// <summary>
    /// P/Invoke bindings for the Vercidium Audio 3D native SDK (vaudionative.dll, C API, single precision).
    /// Only the functions the game uses are declared. The SDK's C# build targets .NET 8, which Unity's Mono runtime
    /// cannot load, so the game talks to the C library directly. Every call must happen on the Unity main thread
    /// (the thread the world was created on).
    /// Reference: Assets/ThirdParty/VercidiumAudio/Include/vaudio.h.txt and https://vercidium.com/docs
    /// The WebGL player has no native library: the same functions are implemented in VAudioNativeWeb.cs on top of the
    /// SDK's WebAssembly build (Plugins/WebGL/VAudioWeb.jslib).
    /// </summary>
    public static partial class VAudioNative
    {
        private const string Lib = "vaudionative";

        // Result codes (subset of vaudio.h).
        public const int Success = 0;
        public const int ErrorInUse = 5;
        public const int StillRunning = 21;
        public const int True = 24;
        public const int False = 25;

        // Default material ids (VAMaterialType).
        public const int MaterialBrick = 1;
        public const int MaterialCloth = 2;
        public const int MaterialConcrete = 3;
        public const int MaterialGyprock = 9;
        public const int MaterialMetal = 13;
        public const int MaterialTile = 18;
        public const int MaterialWoodIndoor = 21;

        public const int CoordinateSystemUnity = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct VAVector
        {
            public float x, y, z;

            public VAVector(float x, float y, float z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public static VAVector From(UnityEngine.Vector3 v) => new VAVector(v.x, v.y, v.z);
        }

        /// <summary>4x4 matrix, same memory layout as vaudio.h (translation in m41..m43).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct VAMatrix
        {
            public float m11, m12, m13, m14;
            public float m21, m22, m23, m24;
            public float m31, m32, m33, m34;
            public float m41, m42, m43, m44;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VALowPassFilter
        {
            public float gainLF;
            public float gainHF;
        }

        /// <summary>Computed EAX reverb parameters (VAEAXReverb). Pointer fields are not read by the game.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct VAEAXReverb
        {
            public float outsidePercent;
            public float returnedPercent;
            public float materialAbsorptionLF;
            public float materialAbsorptionHF;
            public float materialRoughness;
            public IntPtr relativeEmitters;
            public IntPtr relativeDirections;
            public IntPtr relativeGains;
            public int relativeCount;
            public int relativeCapacity;
            public float reflectionsDelay;
            public float density;
            public float diffusion;
            public float gainLF;
            public float gainHF;
            public float gain;
            public float decayTime;
            public float decayLFRatio;
            public float decayHFRatio;
            public float reflectionsGain;
            public float lateReverbGain;
            public float lateReverbDelay;
            public float echoTime;
            public float echoDepth;
            public float modulationTime;
            public float modulationDepth;
            public float airAbsorptionGainHF;
            public float hfReference;
            public float lfReference;
            public float roomRolloffFactor;
            public int decayHFLimit;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void WorldLogCallback(IntPtr world, IntPtr message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void EmitterLogCallback(IntPtr emitter, IntPtr message);

#if !UNITY_WEBGL || UNITY_EDITOR
        // ------------------------------------------------------------------ version

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vaGetVersion(out int major, out int minor, out int patch);

        // ------------------------------------------------------------------ world

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vaWorldCreate();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldDestroy(IntPtr world);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldUpdate(IntPtr world);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldWait(IntPtr world);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool vaWorldGetThreadsRunning(IntPtr world);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetPosition(IntPtr world, VAVector position);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetSize(IntPtr world, VAVector size);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetName(IntPtr world, [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetMaximumConcurrencyLevel(IntPtr world, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetMetersPerUnit(IntPtr world, float value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetCoordinateSystem(IntPtr world, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetEmittersOutsideTheWorldAreMuffled(IntPtr world, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetRenderingEnabled(IntPtr world, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetManualCamera(IntPtr world, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetCameraPosition(IntPtr world, VAVector position);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetCameraPitch(IntPtr world, float pitch);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetCameraYaw(IntPtr world, float yaw);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetLogCallback(IntPtr world, WorldLogCallback callback);

        /// <summary>Whether the world log callback is invoked each time the SDK allocates memory (on by default).</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetLogMemoryAllocationWarnings(IntPtr world, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldAddPrimitive_(IntPtr world, IntPtr primitive);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldRemovePrimitive_(IntPtr world, IntPtr primitive);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldAddEmitter(IntPtr world, IntPtr emitter);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldRemoveEmitter(IntPtr world, IntPtr emitter);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldGetEmitterCount(IntPtr world);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldGetRaysCastThisFrame(IntPtr world);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern double vaWorldGetRaytracingTime(IntPtr world);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern double vaWorldGetPreparationTime(IntPtr world);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern double vaWorldGetMainThreadTime(IntPtr world);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldCreateMaterial(IntPtr world, int materialId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetMaterialAbsorptionLF(IntPtr world, int materialId, float value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetMaterialAbsorptionHF(IntPtr world, int materialId, float value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetMaterialScattering(IntPtr world, int materialId, float value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetMaterialTransmissionLF(IntPtr world, int materialId, float meters);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaWorldSetMaterialTransmissionHF(IntPtr world, int materialId, float meters);

        // ------------------------------------------------------------------ emitter

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vaEmitterCreate();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterDestroy(IntPtr emitter);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterAddTarget(IntPtr emitter, IntPtr target);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterRemoveTarget(IntPtr emitter, IntPtr target);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterHasRaytracedTarget(IntPtr emitter, IntPtr target);

        /// <summary>outFilter receives a pointer to the live filter (VALowPassFilter*), or zero if not raytraced yet.</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterGetTargetFilter(IntPtr emitter, IntPtr target, out IntPtr outFilter);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetPosition(IntPtr emitter, VAVector position);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetName(IntPtr emitter, [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetReverbRayCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetReverbBounceCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetReverbEnergyCap(IntPtr emitter, float value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetOcclusionRayCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetOcclusionBounceCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetPermeationRayCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetPermeationBounceCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetAmbientOcclusionRayCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetAmbientOcclusionBounceCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetAmbientPermeationRayCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetAmbientPermeationBounceCount(IntPtr emitter, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetMaxEchogramTime(IntPtr emitter, int milliseconds);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetEchogramGranularity(IntPtr emitter, int milliseconds);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetClampPosition(IntPtr emitter, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetKeepReverbTailAlive(IntPtr emitter, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetAffectsGroupedEAX(IntPtr emitter, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetLogCallback(IntPtr emitter, EmitterLogCallback callback);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaEmitterSetLogErrorCallback(IntPtr emitter, EmitterLogCallback callback);

        /// <summary>Returns a pointer to the emitter's VAEAXReverb, or zero until its reverb rays were cast once.</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vaEmitterGetEAX(IntPtr emitter);

        // ------------------------------------------------------------------ voxel primitive

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vaVoxelPrimitiveCreate(int width, int height, int depth);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaVoxelPrimitiveDestroy(IntPtr voxel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaVoxelPrimitiveSetMaterial(IntPtr voxel, int material);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaVoxelPrimitiveSetScale(IntPtr voxel, float scale);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaVoxelPrimitiveSetTransform(IntPtr voxel, ref VAMatrix transform);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaVoxelPrimitiveSetVoxel(IntPtr voxel, int x, int y, int z, int material);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaVoxelPrimitiveGetVoxel(IntPtr voxel, int x, int y, int z);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaVoxelPrimitiveGetSize(IntPtr voxel, out int width, out int height, out int depth);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vaVoxelPrimitiveGetTransform(IntPtr voxel);

        /// <summary>Pointer to width*height*depth int material ids, indexed (x * height + y) * depth + z.</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vaVoxelPrimitiveGetVoxelData(IntPtr voxel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaVoxelPrimitiveSetDataDirty(IntPtr voxel);

        // ------------------------------------------------------------------ prism primitive

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vaPrismPrimitiveCreate();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaPrismPrimitiveDestroy(IntPtr prism);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaPrismPrimitiveSetMaterial(IntPtr prism, int material);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaPrismPrimitiveSetSize(IntPtr prism, VAVector size);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vaPrismPrimitiveSetTransform(IntPtr prism, ref VAMatrix transform);

        // ------------------------------------------------------------------ matrix

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern VAMatrix vaMatrixCreateTranslation(float x, float y, float z);

#endif

        // ------------------------------------------------------------------ helpers

        /// <summary>Reads a live VALowPassFilter* without allocating.</summary>
        public static VALowPassFilter ReadFilter(IntPtr filter)
        {
            return new VALowPassFilter
            {
                gainLF = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(filter, 0)),
                gainHF = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(filter, 4))
            };
        }
    }
}
