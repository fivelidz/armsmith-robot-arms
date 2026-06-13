using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Visualization
{
    /// <summary>
    /// Live IK preview: draws the path from the current tip to the current IK target. The "planned" line
    /// is the straight-line approach the controller is driving toward; the visualizer's executed-path
    /// (pushed separately by the bootstrap) shows where the tip ACTUALLY went. Cheap, always-correct,
    /// and a useful baseline next to which diffusion/MPD multimodal paths can be compared.
    /// </summary>
    public class IKPathProvider : MonoBehaviour, ITrajectoryProvider
    {
        public ArmController controller;
        public ProceduralArm arm;
        public bool vizEnabled = true;
        public int samples = 16;

        public string ProviderName => "IK preview";
        public bool VizEnabled => vizEnabled && controller != null && controller.ikTarget != null;

        public TrajectorySet GetTrajectories()
        {
            if (controller == null || controller.ikTarget == null || arm == null || arm.endEffector == null)
                return null;
            Vector3 from = arm.endEffector.position;
            Vector3 to = controller.ikTarget.position;
            var s = new TrajectorySample { label = "ik", cost = Vector3.Distance(from, to), weight = 1f, chosen = true };
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                s.points.Add(Vector3.Lerp(from, to, t));
            }
            var set = new TrajectorySet { source = "ik", start = from, goal = to, hasStartGoal = true };
            set.Add(s);
            return set;
        }
    }

    /// <summary>
    /// Synthetic MULTIMODAL path demo — stands in for a diffusion motion-planner's output until the real
    /// Python inference is wired (DF5). Given a start, goal, and an obstacle, it generates several distinct
    /// candidate routes (over/around-left/around-right/direct) with collision-aware costs, exactly the kind
    /// of multimodal set a diffusion planner samples. This lets the visualization + UI be built and demoed
    /// NOW against realistic data; later, swap GetTrajectories() to read paths from the diffusion server.
    /// </summary>
    public class DiffusionPathDemo : MonoBehaviour, ITrajectoryProvider
    {
        public bool vizEnabled = true;
        public Transform startRef;     // optional; else uses 'start'
        public Transform goalRef;      // optional; else uses 'goal'
        public Vector3 start = new Vector3(0.16f, 0.05f, 0.30f);
        public Vector3 goal = new Vector3(-0.16f, 0.05f, 0.30f);
        public Vector3 obstacle = new Vector3(0f, 0.05f, 0.30f);
        public float obstacleRadius = 0.06f;
        public int pointsPerPath = 24;
        public int variants = 4;

        public string ProviderName => "Diffusion paths (demo)";
        public bool VizEnabled => vizEnabled;

        public TrajectorySet GetTrajectories()
        {
            Vector3 s = startRef != null ? startRef.position : start;
            Vector3 g = goalRef != null ? goalRef.position : goal;
            var set = new TrajectorySet { source = "diffusion", start = s, goal = g, hasStartGoal = true };

            // Several "modes": a high arc over, a left detour, a right detour, and a near-direct path.
            // Each is a smooth Bezier-ish curve through a control point offset from the straight line.
            Vector3 mid = (s + g) * 0.5f;
            Vector3 dir = (g - s); float len = dir.magnitude; dir = len > 1e-4f ? dir / len : Vector3.forward;
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;   // horizontal perpendicular

            var offsets = new (Vector3 ctrl, string label)[]
            {
                (mid + Vector3.up * 0.12f,                 "over"),
                (mid + side * 0.14f,                       "around-right"),
                (mid - side * 0.14f,                       "around-left"),
                (mid + Vector3.up * 0.03f,                 "near-direct"),
            };

            int n = Mathf.Min(variants, offsets.Length);
            for (int v = 0; v < n; v++)
            {
                var samp = new TrajectorySample { label = "diffusion#" + v + " " + offsets[v].label };
                for (int i = 0; i < pointsPerPath; i++)
                {
                    float t = i / (float)(pointsPerPath - 1);
                    samp.points.Add(QuadBezier(s, offsets[v].ctrl, g, t));
                }
                samp.cost = PathCost(samp.points);
                set.Add(samp);
            }
            set.MarkBestChosen();           // lowest-cost (most collision-free + short) becomes the chosen path
            // map cost -> weight (probability-ish) for alpha
            float cMin = float.MaxValue, cMax = float.MinValue;
            foreach (var smp in set.samples) { cMin = Mathf.Min(cMin, smp.cost); cMax = Mathf.Max(cMax, smp.cost); }
            float span = Mathf.Max(1e-4f, cMax - cMin);
            foreach (var smp in set.samples) smp.weight = 1f - 0.7f * ((smp.cost - cMin) / span);
            return set;
        }

        float PathCost(List<Vector3> pts)
        {
            // length + collision penalty (how deep the path dips into the obstacle sphere)
            float L = 0f, pen = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) L += Vector3.Distance(pts[i - 1], pts[i]);
                float d = Vector3.Distance(pts[i], obstacle);
                if (d < obstacleRadius) pen += (obstacleRadius - d) * 8f;   // heavy penalty for intrusion
            }
            return L + pen;
        }

        static Vector3 QuadBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }
    }

    /// <summary>
    /// DENOISING animation demo — visualizes a path being refined from pure noise toward a smooth solution,
    /// the core mental model of diffusion. Each "step" it interpolates from a noisy version of the chosen
    /// path toward the clean path, so you watch the trajectory crystallize. Drives a striking explainer of
    /// "diffusion directs the arm" and later can be driven by the real per-step diffusion samples.
    /// </summary>
    public class DenoisePathDemo : MonoBehaviour, ITrajectoryProvider
    {
        public bool vizEnabled = true;
        public Vector3 start = new Vector3(0.16f, 0.06f, 0.30f);
        public Vector3 goal = new Vector3(-0.14f, 0.10f, 0.26f);
        public int points = 24;
        public int steps = 30;            // denoising steps in the loop
        public float secondsPerStep = 0.12f;
        public float startNoise = 0.10f;  // metres of jitter at step 0

        readonly List<Vector3> clean = new List<Vector3>();
        readonly List<Vector3> work = new List<Vector3>();
        int step; float timer; int seed = 12345;

        public string ProviderName => "Denoising (demo)";
        public bool VizEnabled => vizEnabled;

        void OnEnable() { BuildClean(); ResetNoise(); }

        void BuildClean()
        {
            clean.Clear();
            Vector3 ctrl = (start + goal) * 0.5f + Vector3.up * 0.10f;
            for (int i = 0; i < points; i++)
            {
                float t = i / (float)(points - 1);
                float u = 1 - t;
                clean.Add(u * u * start + 2 * u * t * ctrl + t * t * goal);
            }
        }

        void ResetNoise()
        {
            work.Clear();
            var rng = new System.Random(seed);
            for (int i = 0; i < clean.Count; i++)
            {
                // endpoints stay anchored (inpainting-style hard constraint); middle is noisy
                float anchor = Mathf.Abs((i / (float)(clean.Count - 1)) - 0.5f) * 2f; // 0 mid, 1 ends
                float amp = startNoise * (1f - anchor);
                Vector3 jit = new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)) * amp;
                work.Add(clean[i] + jit);
            }
            step = 0; timer = 0f;
        }

        void Update()
        {
            if (!vizEnabled || clean.Count == 0) return;
            timer += Time.deltaTime;
            if (timer < secondsPerStep) return;
            timer = 0f;
            step++;
            if (step > steps) { seed++; BuildClean(); ResetNoise(); return; }  // loop with new noise
            float a = step / (float)steps;                                     // 0..1 denoise progress
            for (int i = 0; i < work.Count; i++)
                work[i] = Vector3.Lerp(work[i], clean[i], 0.18f + 0.5f * a);   // converge faster over time
        }

        public TrajectorySet GetTrajectories()
        {
            if (work.Count < 2) return null;
            var set = new TrajectorySet { source = "denoise", start = start, goal = goal, hasStartGoal = true };
            float prog = steps > 0 ? step / (float)steps : 1f;
            var samp = new TrajectorySample(work)
            {
                label = "denoise step " + step + "/" + steps,
                chosen = false,
                // colour shifts from magenta (noisy) -> cyan-green (clean) as it denoises
                colorOverride = Color.Lerp(new Color(1f, 0.3f, 0.8f, 0.8f), new Color(0.2f, 1f, 0.6f, 0.95f), prog)
            };
            set.Add(samp);
            return set;
        }
    }
}
