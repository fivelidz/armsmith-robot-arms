using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Cad
{
    /// <summary>
    /// A CAD primitive — the extension point for in-game parametric part creation (see CAD_SPEC.md).
    /// Each primitive yields a Unity Mesh from its parameters. New shapes (ServoBracket, FingerProfile,
    /// ...) implement this without touching the evaluator. Box/Cylinder/Sphere provided.
    /// </summary>
    public interface ICadPrimitive
    {
        string Type { get; }
        Mesh BuildMesh();                 // local-space mesh
        Vector3 LocalPosition { get; }
        Vector3 LocalEuler { get; }
    }

    [Serializable]
    public class CadBox : ICadPrimitive
    {
        public Vector3 size = Vector3.one * 0.05f;
        public Vector3 position; public Vector3 euler;
        public string Type => "Box";
        public Vector3 LocalPosition => position;
        public Vector3 LocalEuler => euler;
        public Mesh BuildMesh() => CadMeshGen.Box(size);
    }

    [Serializable]
    public class CadCylinder : ICadPrimitive
    {
        public float radius = 0.02f, height = 0.06f; public int segments = 24;
        public Vector3 position; public Vector3 euler;
        public string Type => "Cylinder";
        public Vector3 LocalPosition => position;
        public Vector3 LocalEuler => euler;
        public Mesh BuildMesh() => CadMeshGen.Cylinder(radius, height, segments);
    }

    /// <summary>
    /// A parametric CAD part: an ordered list of primitives combined into one mesh, plus a named
    /// parameter table (the evolvable genome) and a name. Serializable to JSON (save + evolve + export).
    /// Evaluated to a Unity Mesh -> StlExporter for printing. CSG (subtract) is approximated for v1 by
    /// authoring holes as separate primitives; a true boolean lib can slot in later.
    /// </summary>
    [Serializable]
    public class CadPart
    {
        public string name = "part";
        public List<CadBox> boxes = new List<CadBox>();
        public List<CadCylinder> cylinders = new List<CadCylinder>();
        public Dictionary<string, float> parameters = new Dictionary<string, float>(); // not JsonUtility-serialized; for runtime tuning

        public IEnumerable<ICadPrimitive> Primitives()
        {
            foreach (var b in boxes) yield return b;
            foreach (var c in cylinders) yield return c;
        }

        /// <summary>Combine all primitives into one mesh (union by concatenation — good for additive parts).</summary>
        public Mesh Evaluate()
        {
            var combine = new List<CombineInstance>();
            foreach (var p in Primitives())
            {
                var m = p.BuildMesh();
                if (m == null) continue;
                var mtx = Matrix4x4.TRS(p.LocalPosition, Quaternion.Euler(p.LocalEuler), Vector3.one);
                combine.Add(new CombineInstance { mesh = m, transform = mtx });
            }
            var result = new Mesh { name = name };
            result.CombineMeshes(combine.ToArray(), true, true);
            result.RecalculateNormals();
            result.RecalculateBounds();
            return result;
        }

        // ── serialization (JsonUtility can't do the Dictionary, so we wrap) ──
        [Serializable] class Dto { public string name; public List<CadBox> boxes; public List<CadCylinder> cylinders; }
        public string ToJson() => JsonUtility.ToJson(new Dto { name = name, boxes = boxes, cylinders = cylinders }, true);
        public static CadPart FromJson(string s)
        {
            var d = JsonUtility.FromJson<Dto>(s);
            return new CadPart { name = d.name, boxes = d.boxes ?? new List<CadBox>(), cylinders = d.cylinders ?? new List<CadCylinder>() };
        }

        /// <summary>A sample parametric servo-mount bracket (base plate + two walls + a bore), so the
        /// system has a real, useful default part the player can tweak/evolve.</summary>
        public static CadPart ServoBracket(float width = 0.04f, float height = 0.03f, float wall = 0.004f, float bore = 0.01f)
        {
            var p = new CadPart { name = "ServoBracket" };
            p.boxes.Add(new CadBox { size = new Vector3(width, wall, width), position = new Vector3(0, wall * 0.5f, 0) });        // base plate
            p.boxes.Add(new CadBox { size = new Vector3(wall, height, width), position = new Vector3(width * 0.5f - wall * 0.5f, height * 0.5f, 0) }); // wall +X
            p.boxes.Add(new CadBox { size = new Vector3(wall, height, width), position = new Vector3(-width * 0.5f + wall * 0.5f, height * 0.5f, 0) }); // wall -X
            p.cylinders.Add(new CadCylinder { radius = bore, height = width * 1.1f, position = new Vector3(0, height * 0.6f, 0), euler = new Vector3(90, 0, 0) }); // bore boss
            return p;
        }
    }
}
