using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// FABRIK (Forward And Backward Reaching Inverse Kinematics) solver.
    /// Recommended in research/learning_evolution/REPORT.md: converges in 3-5 iterations,
    /// handles variable joint counts (good for evolved morphologies), no matrix inversion.
    ///
    /// This solves for a chain of points in WORLD space, then ArmController maps the
    /// resulting bone directions back to per-joint target angles for the ArticulationBody.
    /// </summary>
    public static class FabrikIK
    {
        /// <summary>
        /// Solve a position-only FABRIK chain.
        /// </summary>
        /// <param name="points">Joint positions (world). points[0] = root (fixed). Modified in place.</param>
        /// <param name="lengths">Bone lengths; lengths[i] = distance points[i]->points[i+1]. Count = points.Count-1.</param>
        /// <param name="target">Desired position of the end effector (last point).</param>
        /// <param name="iterations">Max iterations.</param>
        /// <param name="tolerance">Stop when end effector within this of target (m).</param>
        public static void Solve(List<Vector3> points, IReadOnlyList<float> lengths,
                                 Vector3 target, int iterations = 10, float tolerance = 0.001f)
        {
            int n = points.Count;
            if (n < 2) return;

            Vector3 root = points[0];

            float totalLen = 0f;
            for (int i = 0; i < lengths.Count; i++) totalLen += lengths[i];

            // Target unreachable: stretch straight toward it.
            float rootToTarget = Vector3.Distance(root, target);
            if (rootToTarget > totalLen)
            {
                Vector3 dir = (target - root).normalized;
                points[0] = root;
                for (int i = 1; i < n; i++)
                    points[i] = points[i - 1] + dir * lengths[i - 1];
                return;
            }

            for (int iter = 0; iter < iterations; iter++)
            {
                if (Vector3.Distance(points[n - 1], target) < tolerance) break;

                // --- Backward reaching: set end to target, work toward root.
                points[n - 1] = target;
                for (int i = n - 2; i >= 0; i--)
                {
                    Vector3 dir = (points[i] - points[i + 1]).normalized;
                    points[i] = points[i + 1] + dir * lengths[i];
                }

                // --- Forward reaching: pin root, work toward end.
                points[0] = root;
                for (int i = 1; i < n; i++)
                {
                    Vector3 dir = (points[i] - points[i - 1]).normalized;
                    points[i] = points[i - 1] + dir * lengths[i - 1];
                }
            }
        }
    }
}
