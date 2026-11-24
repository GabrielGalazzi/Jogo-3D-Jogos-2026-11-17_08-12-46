using System.Collections.Generic;
using MaidCafe.Generation;
using NUnit.Framework;
using UnityEngine;

namespace MaidCafe.Tests
{
    /// <summary>Metre UVs on floor boxes (<see cref="SurfaceMapping"/>) and the surface materials in Resources.</summary>
    public class SurfaceMappingTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            // Edit mode: GeneratedMesh.OnDestroy does not run, so meshes are destroyed here too.
            foreach (var o in _created)
            {
                if (o == null) continue;
                if (o is GameObject go)
                {
                    var owner = go.GetComponent<GeneratedMesh>();
                    if (owner != null && owner.Mesh != null) Object.DestroyImmediate(owner.Mesh);
                }
                Object.DestroyImmediate(o);
            }
            _created.Clear();
        }

        private Material Textured()
        {
            var mat = new Material(Resources.Load<Material>("MaterialTemplates/LitBase"));
            var tex = new Texture2D(4, 4);
            mat.SetTexture("_BaseMap", tex);
            _created.Add(mat);
            _created.Add(tex);
            return mat;
        }

        private GameObject Box(Vector3 center, Vector3 size, Material mat)
        {
            var go = FloorBuilder.Box(null, "TestBox", center, size, mat);
            _created.Add(go);
            return go;
        }

        /// <summary>UV bounds of the vertices whose normal points along <paramref name="worldNormal"/>.</summary>
        private static Rect FaceUvs(GameObject go, Vector3 worldNormal)
        {
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var normals = mesh.normals;
            var uv = mesh.uv;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < uv.Length; i++)
            {
                if (Vector3.Dot(go.transform.TransformDirection(normals[i]), worldNormal) < 0.99f) continue;
                minX = Mathf.Min(minX, uv[i].x); maxX = Mathf.Max(maxX, uv[i].x);
                minY = Mathf.Min(minY, uv[i].y); maxY = Mathf.Max(maxY, uv[i].y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        [Test]
        public void WallFaceUvsAreWorldMetresWithHeightAsV()
        {
            var wall = Box(new Vector3(5f, 1.5f, 2f), new Vector3(3.7f, 3f, 0.1f), Textured());
            Rect front = FaceUvs(wall, Vector3.back);
            Assert.AreEqual(3.7f, front.width, 1e-4f, "U spans the wall length in metres");
            Assert.AreEqual(0f, front.yMin, 1e-4f, "V starts at the floor (y = 0)");
            Assert.AreEqual(3f, front.yMax, 1e-4f, "V ends at the wall top (3 m)");
            Assert.IsNotNull(wall.GetComponent<MeshFilter>().sharedMesh.tangents);
            Assert.AreEqual(24, wall.GetComponent<MeshFilter>().sharedMesh.tangents.Length, "tangents for normal maps");
        }

        [Test]
        public void FloorTopUsesWorldXZ()
        {
            var floor = Box(new Vector3(10f, -0.1f, 6f), new Vector3(8f, 0.2f, 8f), Textured());
            Rect top = FaceUvs(floor, Vector3.up);
            Assert.AreEqual(6f, top.xMin, 1e-4f);
            Assert.AreEqual(14f, top.xMax, 1e-4f);
            Assert.AreEqual(2f, top.yMin, 1e-4f);
            Assert.AreEqual(10f, top.yMax, 1e-4f);
        }

        [Test]
        public void NeighbouringBoxesLineUp()
        {
            var mat = Textured();
            var a = Box(new Vector3(1f, 1.5f, 0f), new Vector3(2f, 3f, 0.1f), mat);
            var b = Box(new Vector3(3f, 1.5f, 0f), new Vector3(2f, 3f, 0.1f), mat);
            Assert.AreEqual(FaceUvs(a, Vector3.back).xMax, FaceUvs(b, Vector3.back).xMin, 1e-4f,
                "the texture continues across the joint");
        }

        [Test]
        public void RotatedBoxesStillMapInMetres()
        {
            var cabinet = FloorBuilder.Box(null, "Cabinet", new Vector3(0f, 0.45f, 0f), new Vector3(0.8f, 0.9f, 0.45f), Textured(),
                rotation: Quaternion.Euler(0f, 90f, 0f));
            _created.Add(cabinet);
            // After a 90° yaw the 0.8 m side faces ±X.
            Assert.AreEqual(0.8f, FaceUvs(cabinet, Vector3.right).width, 1e-4f);
            Assert.AreEqual(0.9f, FaceUvs(cabinet, Vector3.right).height, 1e-4f);
        }

        [Test]
        public void FlatMaterialsKeepTheSharedCube()
        {
            var flat = new Material(Resources.Load<Material>("MaterialTemplates/LitBase"));
            _created.Add(flat);
            var box = Box(Vector3.zero, Vector3.one, flat);
            Assert.IsNull(box.GetComponent<GeneratedMesh>());
            Assert.AreEqual("Cube", box.GetComponent<MeshFilter>().sharedMesh.name);
        }

        [Test]
        public void SurfaceMaterialsAreReadyForTheBuild()
        {
            var mats = Resources.LoadAll<Material>("Surfaces");
            Assert.GreaterOrEqual(mats.Length, 22, "one material per surface set");
            foreach (var m in mats)
            {
                var albedo = m.GetTexture("_BaseMap");
                Assert.IsNotNull(albedo, m.name + " albedo");
                Assert.LessOrEqual(albedo.width, 2048, m.name + " albedo is at most 2K");
                Assert.IsTrue(m.IsKeywordEnabled("_NORMALMAP"), m.name + " normal map keyword");
                Assert.IsTrue(m.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), m.name + " mask keyword");
                Assert.IsTrue(m.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"), m.name + " no sky reflections");
                var mask = m.GetTexture("_MetallicGlossMap");
                Assert.IsNotNull(mask, m.name + " mask");
                Assert.LessOrEqual(mask.width, 1024, m.name + " mask is at most 1K");
            }
        }
    }
}
