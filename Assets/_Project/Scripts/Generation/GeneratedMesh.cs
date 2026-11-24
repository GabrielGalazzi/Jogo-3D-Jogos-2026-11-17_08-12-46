using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>Owns a mesh created at runtime for this object and destroys it with the object (floors are rebuilt often).</summary>
    public class GeneratedMesh : MonoBehaviour
    {
        public Mesh Mesh;

        private void OnDestroy()
        {
            if (Mesh == null) return;
            if (Application.isPlaying) Destroy(Mesh);
            else DestroyImmediate(Mesh);
        }
    }
}
