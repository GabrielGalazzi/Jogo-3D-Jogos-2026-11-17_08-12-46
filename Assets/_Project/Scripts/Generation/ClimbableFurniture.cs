using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>
    /// Marks a small furniture piece (chair, stool, box, low table, bench, bin) that chasing maids may climb over.
    /// It sits on the piece's holder, next to its BoxCollider. The piece is left out of the NavMesh bake and its
    /// footprint becomes the <see cref="NavAreas.Furniture"/> area (see <see cref="FurnitureDecorator"/>); maids read
    /// the collider's top with a downward ray to lift their body over it (MaidAI).
    /// </summary>
    public sealed class ClimbableFurniture : MonoBehaviour
    {
    }
}
