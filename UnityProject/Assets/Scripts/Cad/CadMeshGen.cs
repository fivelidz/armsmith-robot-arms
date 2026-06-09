using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Cad
{
    /// <summary>Procedural mesh generators for CAD primitives (no external deps). Produces watertight
    /// boxes + cylinders that combine into a CadPart mesh and export to STL.</summary>
    public static class CadMeshGen
    {
        public static Mesh Box(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] v = {
                new(-h.x,-h.y,-h.z), new(h.x,-h.y,-h.z), new(h.x,h.y,-h.z), new(-h.x,h.y,-h.z), // back
                new(-h.x,-h.y, h.z), new(h.x,-h.y, h.z), new(h.x,h.y, h.z), new(-h.x,h.y, h.z),  // front
            };
            int[] tri = {
                0,2,1, 0,3,2,   // back (-z)
                4,5,6, 4,6,7,   // front (+z)
                0,1,5, 0,5,4,   // bottom (-y)
                3,7,6, 3,6,2,   // top (+y)
                1,2,6, 1,6,5,   // right (+x)
                0,4,7, 0,7,3,   // left (-x)
            };
            var m = new Mesh { name = "cad_box" };
            m.vertices = v; m.triangles = tri;
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        public static Mesh Cylinder(float radius, float height, int seg)
        {
            seg = Mathf.Max(3, seg);
            float hh = height * 0.5f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            // side ring vertices
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * radius, z = Mathf.Sin(a) * radius;
                verts.Add(new Vector3(x, -hh, z));  // bottom ring (2i)
                verts.Add(new Vector3(x, hh, z));   // top ring (2i+1)
            }
            for (int i = 0; i < seg; i++)
            {
                int b0 = i * 2, t0 = i * 2 + 1;
                int b1 = ((i + 1) % seg) * 2, t1 = ((i + 1) % seg) * 2 + 1;
                tris.AddRange(new[] { b0, t0, b1, b1, t0, t1 });
            }
            // caps
            int cb = verts.Count; verts.Add(new Vector3(0, -hh, 0));
            int ct = verts.Count; verts.Add(new Vector3(0, hh, 0));
            for (int i = 0; i < seg; i++)
            {
                int b0 = i * 2, b1 = ((i + 1) % seg) * 2;
                int t0 = i * 2 + 1, t1 = ((i + 1) % seg) * 2 + 1;
                tris.AddRange(new[] { cb, b1, b0 });   // bottom cap
                tris.AddRange(new[] { ct, t0, t1 });   // top cap
            }
            var m = new Mesh { name = "cad_cylinder" };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
