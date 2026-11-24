using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MaidCafe.Audio;
using NUnit.Framework;
using UnityEngine;

namespace MaidCafe.Tests
{
    /// <summary>Voxelizer rules, hearing/filter mappings, synthesized clips and (if the SDK is installed) a native trace.</summary>
    public class AcousticTests
    {
        private const float S = AcousticVoxelizer.DefaultVoxelSize;

        private static AcousticBox Box(Vector3 min, Vector3 max, int material = AcousticMaterials.InteriorWall, int priority = 0)
        {
            var b = new Bounds();
            b.SetMinMax(min, max);
            return new AcousticBox(b, material, priority);
        }

        /// <summary>Two 4 m rooms side by side (x 0–4 and 4–8), 3 m high, with an optional 1.6 m door in the shared wall.</summary>
        private static List<AcousticBox> TwoRooms(bool door)
        {
            var boxes = new List<AcousticBox>
            {
                Box(new Vector3(0f, -0.2f, 0f), new Vector3(8f, 0f, 4f), AcousticMaterials.Floor),
                Box(new Vector3(0f, 3f, 0f), new Vector3(8f, 3.2f, 4f), AcousticMaterials.Ceiling),
                Box(new Vector3(-0.1f, 0f, 0f), new Vector3(0.1f, 3f, 4f), AcousticMaterials.ExteriorWall, 3),
                Box(new Vector3(7.9f, 0f, 0f), new Vector3(8.1f, 3f, 4f), AcousticMaterials.ExteriorWall, 3),
                Box(new Vector3(0f, 0f, -0.1f), new Vector3(8f, 3f, 0.1f), AcousticMaterials.ExteriorWall, 3),
                Box(new Vector3(0f, 0f, 3.9f), new Vector3(8f, 3f, 4.1f), AcousticMaterials.ExteriorWall, 3),
            };
            if (door)
            {
                boxes.Add(Box(new Vector3(3.9f, 0f, 0f), new Vector3(4.1f, 3f, 1.2f)));
                boxes.Add(Box(new Vector3(3.9f, 0f, 2.8f), new Vector3(4.1f, 3f, 4f)));
                boxes.Add(Box(new Vector3(3.9f, 2.3f, 1.2f), new Vector3(4.1f, 3f, 2.8f)));
            }
            else
            {
                boxes.Add(Box(new Vector3(3.9f, 0f, 0f), new Vector3(4.1f, 3f, 4f)));
            }
            return boxes;
        }

        // ------------------------------------------------------------------ voxelizer

        [Test]
        public void ThinWallIsExactlyOneVoxelThick()
        {
            var grid = AcousticVoxelizer.Rasterize(TwoRooms(door: false));
            Assert.AreEqual(AcousticMaterials.InteriorWall, grid.Sample(new Vector3(4f, 1.5f, 2f)));
            Assert.AreEqual(0, grid.Sample(new Vector3(4f - S, 1.5f, 2f)), "wall bled into the left room");
            Assert.AreEqual(0, grid.Sample(new Vector3(4f + S, 1.5f, 2f)), "wall bled into the right room");
        }

        [Test]
        public void DoorOpeningStaysOpenAndLintelIsSolid()
        {
            var grid = AcousticVoxelizer.Rasterize(TwoRooms(door: true));
            Assert.AreEqual(0, grid.Sample(new Vector3(4f, 1f, 2f)), "doorway is blocked");
            Assert.AreEqual(0, grid.Sample(new Vector3(4f, 2.1f, 1.4f)), "doorway corner is blocked");
            Assert.AreNotEqual(0, grid.Sample(new Vector3(4f, 1f, 0.6f)), "wall beside the door is missing");
            Assert.AreNotEqual(0, grid.Sample(new Vector3(4f, 2.7f, 2f)), "lintel is missing");
        }

        [Test]
        public void FloorTopIsAVoxelBoundaryAtZero()
        {
            var grid = AcousticVoxelizer.Rasterize(TwoRooms(door: true));
            float k = (0f - grid.Origin.y) / S;
            Assert.AreEqual(Mathf.Round(k), k, 1e-4f, "y = 0 must be a voxel boundary");
            Assert.AreEqual(AcousticMaterials.Floor, grid.Sample(new Vector3(2f, -0.1f, 2f)));
            Assert.AreEqual(0, grid.Sample(new Vector3(2f, 0.05f, 2f)));
            Assert.AreEqual(AcousticMaterials.Ceiling, grid.Sample(new Vector3(2f, 3.1f, 2f)));
            Assert.AreEqual(0, grid.Sample(new Vector3(2f, 2.95f, 2f)));
        }

        [Test]
        public void HigherPriorityWinsRegardlessOfOrder()
        {
            var low = Box(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f), AcousticMaterials.InteriorWall, 1);
            var high = Box(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f), AcousticMaterials.ExteriorWall, 3);
            var a = AcousticVoxelizer.Rasterize(new List<AcousticBox> { low, high });
            var b = AcousticVoxelizer.Rasterize(new List<AcousticBox> { high, low });
            Assert.AreEqual(AcousticMaterials.ExteriorWall, a.Sample(new Vector3(0.5f, 0.5f, 0.5f)));
            Assert.AreEqual(AcousticMaterials.ExteriorWall, b.Sample(new Vector3(0.5f, 0.5f, 0.5f)));
        }

        [Test]
        public void IndexMatchesSdkVoxelLayout()
        {
            var grid = new VoxelGrid { Width = 3, Height = 4, Depth = 5 };
            Assert.AreEqual((2 * 4 + 3) * 5 + 4, grid.Index(2, 3, 4)); // (x * height + y) * depth + z, as in vaudio.h
        }

        // ------------------------------------------------------------------ mappings

        [Test]
        public void HearingRadiusFactorKeepsOldBehaviourAtTheExtremes()
        {
            Assert.AreEqual(1f, MaidHearing.RadiusFactor(1f), 1e-4f);
            Assert.AreEqual(MaidHearing.SealedRadiusFactor, MaidHearing.RadiusFactor(0f), 1e-4f);
            // The line-of-sight fallback reproduces the old "muffled through walls" factor of 0.6.
            Assert.AreEqual(0.6f, MaidHearing.RadiusFactor(MaidHearing.BlockedFallbackAudibility), 0.01f);
            float last = 0f;
            for (float a = 0f; a <= 1f; a += 0.05f)
            {
                float f = MaidHearing.RadiusFactor(a);
                Assert.GreaterOrEqual(f, last);
                last = f;
            }
        }

        [Test]
        public void FilterMappingIsOpenWhenClearAndMuffledWhenBlocked()
        {
            Assert.AreEqual(22000f, RaytracedSource.CutoffFromGains(1f, 1f), 1f);
            Assert.AreEqual(300f, RaytracedSource.CutoffFromGains(1f, 0f), 1f);
            Assert.Less(RaytracedSource.CutoffFromGains(1f, 0.1f), 1500f);
            Assert.AreEqual(1f, RaytracedSource.VolumeFromGain(1f), 1e-4f);
            Assert.AreEqual(0f, RaytracedSource.VolumeFromGain(0f), 1e-4f);
            Assert.Greater(RaytracedSource.VolumeFromGain(0.25f), 0.25f, "faint paths are lifted a little");
        }

        [Test]
        public void EverySoundSynthesizesCleanAudio()
        {
            foreach (Sfx id in Enum.GetValues(typeof(Sfx)))
            {
                for (int v = 0; v < ProceduralSfx.VariantCount(id); v++)
                {
                    float[] data = ProceduralSfx.Synthesize(id, v);
                    Assert.Greater(data.Length, ProceduralSfx.Rate / 20, $"{id} is too short");
                    float peak = 0f;
                    foreach (float x in data)
                    {
                        Assert.IsFalse(float.IsNaN(x) || float.IsInfinity(x), $"{id} has NaN/Inf samples");
                        peak = Mathf.Max(peak, Mathf.Abs(x));
                    }
                    Assert.Greater(peak, 1e-3f, $"{id} is silent");
                }
            }
        }

        // ------------------------------------------------------------------ native SDK (skipped without vaudionative.dll)

        [Test]
        public void NativeTraceHearsThroughTheDoorMoreThanThroughTheWall()
        {
            if (!AcousticWorld.LibraryAvailable) Assert.Ignore("Vercidium Audio SDK not installed.");
            float open = TraceTwoRooms(door: true, out float openHF);
            float closed = TraceTwoRooms(door: false, out float closedHF);
            Debug.Log($"[AcousticTests] door LF {open:F3} HF {openHF:F3} | wall LF {closed:F3} HF {closedHF:F3}");
            Assert.Greater(openHF, closedHF + 0.2f, "the doorway should let much more of the highs through");
            Assert.Less(closedHF, 0.2f, "a closed wall should cut the highs");
            Assert.Greater(open, 0.5f, "sound through an open doorway should stay loud");
        }

        /// <summary>
        /// Same unit convention as AcousticWorld (voxel units, MetersPerUnit = voxel size, scale 1).
        /// Listener and target sit in different rooms, offset from the door so there is no straight line through it.
        /// </summary>
        private static float TraceTwoRooms(bool door, out float gainHF)
        {
            var grid = AcousticVoxelizer.Rasterize(TwoRooms(door));
            float u = AcousticWorld.UnitsPerMeter;
            IntPtr world = VAudioNative.vaWorldCreate();
            IntPtr voxel = IntPtr.Zero, ear = IntPtr.Zero, src = IntPtr.Zero;
            try
            {
                VAudioNative.vaWorldSetMetersPerUnit(world, 1f / u);
                AcousticMaterials.Register(world);
                VAudioNative.vaWorldSetPosition(world, VAudioNative.VAVector.From(grid.Origin * u));
                VAudioNative.vaWorldSetSize(world, VAudioNative.VAVector.From(grid.Size * u));
                voxel = VAudioNative.vaVoxelPrimitiveCreate(grid.Width, grid.Height, grid.Depth);
                Vector3 o = grid.Origin * u;
                var m = VAudioNative.vaMatrixCreateTranslation(o.x, o.y, o.z);
                VAudioNative.vaVoxelPrimitiveSetTransform(voxel, ref m);
                Marshal.Copy(grid.Data, 0, VAudioNative.vaVoxelPrimitiveGetVoxelData(voxel), grid.Data.Length);
                VAudioNative.vaVoxelPrimitiveSetDataDirty(voxel);
                Assert.AreEqual(VAudioNative.Success, VAudioNative.vaWorldAddPrimitive_(world, voxel));

                ear = VAudioNative.vaEmitterCreate();
                VAudioNative.vaEmitterSetOcclusionRayCount(ear, 96);
                VAudioNative.vaEmitterSetOcclusionBounceCount(ear, 6);
                VAudioNative.vaEmitterSetPermeationRayCount(ear, 24);
                VAudioNative.vaEmitterSetPermeationBounceCount(ear, 2);
                VAudioNative.vaEmitterSetPosition(ear, VAudioNative.VAVector.From(new Vector3(1.5f, 1.5f, 3f) * u));
                src = VAudioNative.vaEmitterCreate();
                VAudioNative.vaEmitterSetPosition(src, VAudioNative.VAVector.From(new Vector3(6.5f, 1.5f, 3f) * u));
                VAudioNative.vaWorldAddEmitter(world, ear);
                VAudioNative.vaWorldAddEmitter(world, src);
                VAudioNative.vaEmitterAddTarget(ear, src);
                for (int i = 0; i < 5; i++)
                {
                    VAudioNative.vaWorldUpdate(world);
                    VAudioNative.vaWorldWait(world);
                }
                Assert.AreEqual(VAudioNative.Success, VAudioNative.vaEmitterGetTargetFilter(ear, src, out IntPtr filter));
                var f = VAudioNative.ReadFilter(filter);
                gainHF = f.gainHF;
                return f.gainLF;
            }
            finally
            {
                VAudioNative.vaWorldDestroy(world);
                if (ear != IntPtr.Zero) VAudioNative.vaEmitterDestroy(ear);
                if (src != IntPtr.Zero) VAudioNative.vaEmitterDestroy(src);
                if (voxel != IntPtr.Zero) VAudioNative.vaVoxelPrimitiveDestroy(voxel);
            }
        }
    }
}
