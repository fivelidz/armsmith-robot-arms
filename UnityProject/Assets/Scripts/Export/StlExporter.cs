using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Binary STL exporter. Combines all MeshFilters under a root into one solid and writes a binary STL.
    /// Format: 80-byte header + uint32 triangle count + per-triangle (3f normal + 9f verts + uint16 attr).
    /// Converts Unity (left-handed, Y-up) -> STL (right-handed, Z-up) and flips winding.
    /// See research/cad_3dprint/REPORT.md.
    /// </summary>
    public static class StlExporter
    {
        public static void ExportHierarchy(Transform root, string path, float scaleToMM = 1000f)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>();
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c)>();

            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                if (mf.name.StartsWith("vis_") == false && mf.GetComponent<MeshRenderer>() == null) continue;
                Mesh m = mf.sharedMesh;
                Vector3[] verts = m.vertices;
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    int[] idx = m.GetTriangles(sm);
                    for (int i = 0; i < idx.Length; i += 3)
                    {
                        Vector3 a = mf.transform.TransformPoint(verts[idx[i]]);
                        Vector3 b = mf.transform.TransformPoint(verts[idx[i + 1]]);
                        Vector3 c = mf.transform.TransformPoint(verts[idx[i + 2]]);
                        tris.Add((a, b, c));
                    }
                }
            }

            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            bw.Write(new byte[80]);                 // header
            bw.Write((uint)tris.Count);

            foreach (var t in tris)
            {
                // Unity(LH,Y-up) -> STL(RH,Z-up): (x,y,z)->(x,z,y). Flip winding for correct outward normal.
                Vector3 a = Conv(t.a, scaleToMM);
                Vector3 b = Conv(t.c, scaleToMM);   // swap b/c to flip winding
                Vector3 c = Conv(t.b, scaleToMM);
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                WriteV(bw, n); WriteV(bw, a); WriteV(bw, b); WriteV(bw, c);
                bw.Write((ushort)0);
            }
            Debug.Log($"[StlExporter] wrote {tris.Count} triangles -> {path}");
        }

        static Vector3 Conv(Vector3 v, float s) => new Vector3(v.x * s, v.z * s, v.y * s);
        static void WriteV(BinaryWriter bw, Vector3 v) { bw.Write(v.x); bw.Write(v.y); bw.Write(v.z); }
    }
}
