using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>
    /// Gives a scaled cube primitive UVs in world metres ("box mapping"), so a textured surface keeps the same texel
    /// density and lines up across neighbouring pieces whatever the box size. Faces project along their dominant world
    /// axis: walls use (horizontal position, height), floors and ceilings use (x, z). Because V is the world height,
    /// textures whose tile spans the 3 m wall height (the wallpapers) keep their lower panel and border at the same
    /// height everywhere. Tangents are rebuilt for normal mapping.
    ///
    /// Only boxes whose material has a base texture get their own mesh (flat-colour boxes keep the shared cube).
    /// Call it once the box has its final position, rotation and scale; moving it afterwards just carries the
    /// texture along. The mesh is destroyed with the object (<see cref="GeneratedMesh"/>).
    /// </summary>
    public static class SurfaceMapping
    {
        private static Vector3[] _vertices;
        private static Vector3[] _normals;
        private static int[] _triangles;

        public static bool IsTextured(Material mat)
        {
            return mat != null && mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;
        }

        public static void Apply(GameObject go, Material mat)
        {
            if (!IsTextured(mat)) return;
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            if (_vertices == null)
            {
                var cube = filter.sharedMesh;
                _vertices = cube.vertices;
                _normals = cube.normals;
                _triangles = cube.triangles;
            }

            var t = go.transform;
            var uv = new Vector2[_vertices.Length];
            for (int i = 0; i < _vertices.Length; i++)
            {
                Vector3 p = t.TransformPoint(_vertices[i]);
                Vector3 n = t.TransformDirection(_normals[i]);
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                // Each face is seen un-mirrored from outside the box (normal maps rely on tangents either way).
                if (ay >= ax && ay >= az) uv[i] = new Vector2(p.x, n.y > 0f ? p.z : -p.z);
                else if (ax >= az) uv[i] = new Vector2(n.x > 0f ? p.z : -p.z, p.y);
                else uv[i] = new Vector2(n.z > 0f ? -p.x : p.x, p.y);
            }

            var mesh = new Mesh { name = go.name + "_Surface" };
            mesh.vertices = _vertices;
            mesh.normals = _normals;
            mesh.uv = uv;
            mesh.triangles = _triangles;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            go.AddComponent<GeneratedMesh>().Mesh = mesh;
        }
    }
}
