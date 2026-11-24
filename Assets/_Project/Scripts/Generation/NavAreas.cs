using UnityEngine.AI;

namespace MaidCafe.Generation
{
    /// <summary>
    /// NavMesh area indices (ProjectSettings/NavMeshAreas) and the masks built from them.
    /// Only the floor slabs are <see cref="Walkable"/>; every other collider is baked as <see cref="NotWalkable"/>, so
    /// nothing can be walked on top of. Small furniture is not an obstacle in the bake: its footprint (plus a body-width
    /// margin) is the <see cref="Furniture"/> area, which only chasing maids cross (see MaidAI: per-agent area cost and
    /// the climb lift).
    /// </summary>
    public static class NavAreas
    {
        public const int Walkable = 0;
        public const int NotWalkable = 1;

        /// <summary>Footprints of climbable furniture ("Furniture" in ProjectSettings/NavMeshAreas).</summary>
        public const int Furniture = 3;

        public const int FurnitureMask = 1 << Furniture;

        /// <summary>
        /// Every area the player can actually walk on (the furniture footprints are solid for them). Use it for
        /// validation, spawn points, checkpoints and any destination that is not a chase target.
        /// </summary>
        public const int Ground = NavMesh.AllAreas & ~FurnitureMask;
    }
}
