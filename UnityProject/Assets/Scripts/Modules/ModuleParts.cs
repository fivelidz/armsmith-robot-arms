using System;
using UnityEngine;

namespace ArmSmith.Modules
{
    /// <summary>
    /// Builds the procedural 3D MESHES for attachable parts (no asset import). Each part is a small assembly
    /// of primitives sized in metres to sit nicely on the SO-101 links — a camera is a body + lens, a lidar
    /// is a puck + dome, a bracket is a riser, etc. Colliders are removed so parts don't disturb physics.
    /// </summary>
    public static class ModuleParts
    {
        public static GameObject Build(PartDef def, Func<Color, Material> matFactory)
        {
            var root = new GameObject("Part_" + def.id);
            Material body = matFactory != null ? matFactory(def.color) : Fallback(def.color);
            Material dark = matFactory != null ? matFactory(new Color(0.08f, 0.09f, 0.11f)) : Fallback(new Color(0.08f, 0.09f, 0.11f));
            Material lens = matFactory != null ? matFactory(new Color(0.2f, 0.45f, 0.8f)) : Fallback(new Color(0.2f, 0.45f, 0.8f));

            switch (def.kind)
            {
                case PartKind.Camera:
                    Box(root, body, new Vector3(0, 0, 0), new Vector3(0.045f, 0.035f, 0.03f));          // camera body
                    Cyl(root, dark, new Vector3(0, 0, 0.022f), new Vector3(90, 0, 0), 0.013f, 0.018f);  // lens barrel
                    Cyl(root, lens, new Vector3(0, 0, 0.032f), new Vector3(90, 0, 0), 0.010f, 0.004f);  // lens glass
                    break;
                case PartKind.RangeFinder:
                    Box(root, body, Vector3.zero, new Vector3(0.025f, 0.025f, 0.018f));
                    Cyl(root, lens, new Vector3(0, 0, 0.012f), new Vector3(90, 0, 0), 0.006f, 0.006f);  // emitter eye
                    break;
                case PartKind.Lidar:
                    Cyl(root, body, Vector3.zero, Vector3.zero, 0.025f, 0.018f);                         // puck base
                    Cyl(root, dark, new Vector3(0, 0.016f, 0), Vector3.zero, 0.020f, 0.012f);            // spinning dome
                    break;
                case PartKind.Imu:
                    Box(root, body, Vector3.zero, new Vector3(0.02f, 0.008f, 0.02f));                    // tiny PCB
                    Box(root, dark, new Vector3(0, 0.006f, 0), new Vector3(0.008f, 0.004f, 0.008f));     // chip
                    break;
                case PartKind.Tactile:
                    Box(root, body, Vector3.zero, new Vector3(0.02f, 0.006f, 0.015f));                   // skin pad
                    break;
                case PartKind.Light:
                    Cyl(root, dark, Vector3.zero, new Vector3(90, 0, 0), 0.016f, 0.014f);                // housing
                    Cyl(root, body, new Vector3(0, 0, 0.016f), new Vector3(90, 0, 0), 0.014f, 0.003f);   // LED face (emissive-ish colour)
                    break;
                case PartKind.Bracket:
                    Box(root, body, new Vector3(0, 0.02f, 0), new Vector3(0.02f, 0.04f, 0.02f));         // riser post
                    Box(root, body, new Vector3(0, 0.041f, 0), new Vector3(0.035f, 0.006f, 0.035f));    // top plate
                    break;
                case PartKind.Counterweight:
                    Cyl(root, body, Vector3.zero, Vector3.zero, 0.03f, 0.025f);                          // chunky disc
                    break;
                default:
                    Box(root, body, Vector3.zero, new Vector3(0.03f, 0.03f, 0.03f));
                    break;
            }
            return root;
        }

        static void Box(GameObject parent, Material m, Vector3 localPos, Vector3 size)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Strip(g); g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = localPos; g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        static void Cyl(GameObject parent, Material m, Vector3 localPos, Vector3 euler, float radius, float height)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Strip(g); g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = localPos; g.transform.localRotation = Quaternion.Euler(euler);
            g.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        static void Strip(GameObject g)
        {
            var c = g.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);   // parts must not perturb the arm physics
        }

        static Material Fallback(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(sh) { color = c };
        }
    }
}
