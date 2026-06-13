using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Visualization
{
    /// <summary>
    /// A simple obstacle / cost field used by the diffusion motion planner for COST-GUIDED sampling
    /// (the classifier-guidance mechanism from Diffuser/MPD — see research/diffusion_pathfinding/REPORT.md
    /// §4). It models obstacles as spheres with a soft margin and exposes:
    ///   • Cost(p)        — a smooth collision penalty (0 outside the margin, rising inside)
    ///   • Gradient(p)    — ∇Cost, the direction that pushes a point AWAY from obstacles
    ///   • Blocked(a,b)   — whether a straight segment intersects any obstacle (for feasibility checks)
    /// Spheres are cheap, analytic, and give clean gradients — ideal for the iterative denoising guidance.
    /// Populate manually (AddSphere) or from scene colliders (PopulateFromScene), excluding the arm + a
    /// chosen ignore list (e.g. the object being grasped).
    /// </summary>
    public class ObstacleField
    {
        public struct Sphere { public Vector3 center; public float radius; }
        readonly List<Sphere> spheres = new List<Sphere>();
        public float margin = 0.04f;   // soft buffer beyond the hard radius where cost ramps up

        public int Count => spheres.Count;
        public IReadOnlyList<Sphere> Spheres => spheres;
        public void Clear() => spheres.Clear();
        public void AddSphere(Vector3 c, float r) => spheres.Add(new Sphere { center = c, radius = r });

        /// <summary>
        /// Build obstacle spheres from scene colliders. Skips the arm (anything under a ProceduralArm),
        /// skips non-kinematic Rigidbodies optionally (the manipulable target you're reaching FOR), and
        /// approximates each collider by its bounds-sphere. Static furniture (worktop/walls) are included
        /// so planned paths avoid them.
        /// </summary>
        public void PopulateFromScene(Transform armRoot = null, Transform ignore = null, bool includeDynamic = false, float maxRadius = 0.25f)
        {
            spheres.Clear();
#if UNITY_2023_1_OR_NEWER
            var cols = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
#else
            var cols = Object.FindObjectsOfType<Collider>();
#endif
            foreach (var c in cols)
            {
                if (c == null || !c.enabled) continue;
                if (armRoot != null && c.transform.IsChildOf(armRoot)) continue;
                if (ignore != null && (c.transform == ignore || c.transform.IsChildOf(ignore))) continue;
                var rb = c.attachedRigidbody;
                if (!includeDynamic && rb != null && !rb.isKinematic) continue;  // skip the grasp target
                var b = c.bounds;
                float r = Mathf.Min(maxRadius, b.extents.magnitude * 0.6f);
                if (r < 0.005f) continue;
                spheres.Add(new Sphere { center = b.center, radius = r });
            }
        }

        /// <summary>Smooth collision cost at a point: 0 outside (radius+margin), quadratic ramp inside.</summary>
        public float Cost(Vector3 p)
        {
            float total = 0f;
            for (int i = 0; i < spheres.Count; i++)
            {
                float d = Vector3.Distance(p, spheres[i].center) - spheres[i].radius;
                if (d < margin)
                {
                    float pen = (margin - d) / Mathf.Max(1e-4f, margin);  // 0..(>1 if penetrating)
                    total += pen * pen;
                }
            }
            return total;
        }

        /// <summary>∇Cost — points away from obstacles (descending this reduces collision cost).</summary>
        public Vector3 Gradient(Vector3 p)
        {
            Vector3 g = Vector3.zero;
            for (int i = 0; i < spheres.Count; i++)
            {
                Vector3 d = p - spheres[i].center;
                float dist = d.magnitude;
                float surf = dist - spheres[i].radius;
                if (surf < margin && dist > 1e-5f)
                {
                    float pen = (margin - surf) / Mathf.Max(1e-4f, margin);
                    g += (d / dist) * (2f * pen);   // push outward, stronger the deeper inside
                }
            }
            return g;
        }

        /// <summary>True if the straight segment a→b passes within any obstacle's hard radius.</summary>
        public bool Blocked(Vector3 a, Vector3 b, int samples = 8)
        {
            for (int s = 0; s <= samples; s++)
            {
                Vector3 p = Vector3.Lerp(a, b, s / (float)samples);
                for (int i = 0; i < spheres.Count; i++)
                    if (Vector3.Distance(p, spheres[i].center) < spheres[i].radius) return true;
            }
            return false;
        }

        /// <summary>Max collision cost along a polyline (handy for labelling a path feasible/infeasible).</summary>
        public float MaxCostAlong(IReadOnlyList<Vector3> pts)
        {
            float m = 0f;
            for (int i = 0; i < pts.Count; i++) m = Mathf.Max(m, Cost(pts[i]));
            return m;
        }
    }
}
