using System;

namespace MaidCafe.Audio
{
    /// <summary>
    /// Acoustic materials of the café. Custom ids start at 1000 (the SDK reserves 0–999). Walls are voxelized at
    /// 0.25 m (thicker than the real 0.2 m), so their transmission distances are tuned for that thickness: a closed
    /// wall takes roughly 15 dB off the lows and 34 dB off the highs; sound mostly travels through door openings.
    /// Transmission = metres of material that remove all energy (-30 dB), linear in dB in between.
    /// </summary>
    public static class AcousticMaterials
    {
        public const int InteriorWall = 1000;
        public const int ExteriorWall = 1001;
        public const int LockerMetal = 1002;

        public const int Floor = VAudioNative.MaterialWoodIndoor;
        public const int Ceiling = VAudioNative.MaterialGyprock;
        public const int Pillar = VAudioNative.MaterialConcrete;
        public const int ElevatorShell = VAudioNative.MaterialMetal;

        private struct Props
        {
            public int Id;
            public float AbsorptionLF, AbsorptionHF, Scattering, TransmissionLF, TransmissionHF;
        }

        private static readonly Props[] Custom =
        {
            // Painted plaster partitions: fairly reflective, muffles the highs strongly.
            new Props { Id = InteriorWall, AbsorptionLF = 0.08f, AbsorptionHF = 0.14f, Scattering = 0.15f, TransmissionLF = 0.5f, TransmissionHF = 0.22f },
            // Outer brick shell: nothing worth hearing outside, keep rays in.
            new Props { Id = ExteriorWall, AbsorptionLF = 0.1f, AbsorptionHF = 0.2f, Scattering = 0.3f, TransmissionLF = 0.4f, TransmissionHF = 0.2f },
            // 4 cm steel locker sheet: ~8 dB lows, ~20 dB highs.
            new Props { Id = LockerMetal, AbsorptionLF = 0.05f, AbsorptionHF = 0.03f, Scattering = 0.02f, TransmissionLF = 0.15f, TransmissionHF = 0.06f },
        };

        /// <summary>Creates and configures the custom materials in a freshly created world.</summary>
        public static void Register(IntPtr world)
        {
            foreach (var p in Custom)
            {
                VAudioNative.vaWorldCreateMaterial(world, p.Id);
                VAudioNative.vaWorldSetMaterialAbsorptionLF(world, p.Id, p.AbsorptionLF);
                VAudioNative.vaWorldSetMaterialAbsorptionHF(world, p.Id, p.AbsorptionHF);
                VAudioNative.vaWorldSetMaterialScattering(world, p.Id, p.Scattering);
                VAudioNative.vaWorldSetMaterialTransmissionLF(world, p.Id, p.TransmissionLF);
                VAudioNative.vaWorldSetMaterialTransmissionHF(world, p.Id, p.TransmissionHF);
            }
        }
    }
}
