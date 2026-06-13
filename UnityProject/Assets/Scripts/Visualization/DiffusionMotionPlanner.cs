using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Visualization
{
    /// <summary>
    /// An MPD-style DIFFUSION MOTION PLANNER (research/diffusion_pathfinding/REPORT.md §4: "planning as
    /// denoising"). It is NOT a learned network — it's a classical, fully in-sim implementation of the
    /// SAME mechanism a trained trajectory-diffusion planner uses, so it produces the same KIND of output
    /// (smooth, collision-free, MULTIMODAL end-effector paths) and can be swapped for a learned model later
    /// without changing anything downstream (it implements ITrajectoryProvider, feeds PathVisualizer).
    ///
    /// How it works (per query: start, goal, ObstacleField):
    ///   1. Seed K candidate trajectories from NOISE — straight start→goal line + per-trajectory random
    ///      control-point perturbations (different seeds explore different "modes": left vs right vs over).
    ///   2. DENOISE for N steps. Each step applies, with a decaying step size (the diffusion schedule):
    ///        • cost-guided push   : x -= α · ∇Cost(x)        (classifier guidance — away from obstacles)
    ///        • smoothing (prior)  : x ← x + β·(neighbour mean − x)   (the learned smooth-trajectory prior)
    ///        • endpoint anchoring : x[0]=start, x[H-1]=goal          (inpainting hard constraint)
    ///        • floor clamp        : keep above the worktop
    ///   3. Score each by length + max collision cost; mark the best (lowest cost) as chosen.
    ///
    /// This is deterministic per seed (reproducible) and cheap (a few hundred point updates), so it runs
    /// live every frame or on demand. Real diffusion would replace steps 1–2 with a denoiser; the cost
    /// guidance / anchoring / scoring stay identical.
    /// </summary>
    public class DiffusionMotionPlanner : MonoBehaviour, ITrajectoryProvider
    {
        [Header("Query")]
        public Transform startRef;     // e.g. the gripper tip
        public Transform goalRef;      // e.g. the target/cube
        public Vector3 start = new Vector3(0.16f, 0.06f, 0.30f);
        public Vector3 goal  = new Vector3(-0.16f, 0.06f, 0.30f);
        public bool autoResolveScene = true;

        [Header("Planner")]
        public int candidates = 5;     // number of modes to explore
        public int pointsPerPath = 24;
        public int denoiseSteps = 60;
        public float initialNoise = 0.10f;   // metres of control-point jitter at seed
        public float guidanceStep = 0.045f;  // α — obstacle-avoidance push per step (must beat smoothing near obstacles)
        public float smoothing = 0.22f;      // β — prior smoothing per step
        public float floorY = 0.02f;
        public bool vizEnabled = true;
        public bool replanEachFrame = false; // recompute every frame (else cache until ReplanNow)

        readonly ObstacleField field = new ObstacleField();
        TrajectorySet cached;
        bool dirty = true;

        public string ProviderName => "Diffusion planner (MPD)";
        public bool VizEnabled => vizEnabled;

        public void ReplanNow() { dirty = true; }
        public ObstacleField Field => field;

        void TryResolve()
        {
            if (!autoResolveScene) return;
            if (startRef == null) { var g = GameObject.Find("S_Cube"); if (g) startRef = g.transform; }
            if (goalRef == null)  { var p = GameObject.Find("S_Pad") ?? GameObject.Find("S_TrayB") ?? GameObject.Find("S_TrayA"); if (p) goalRef = p.transform; }
        }

        public TrajectorySet GetTrajectories()
        {
            if (replanEachFrame || dirty || cached == null)
            {
                cached = Plan();
                dirty = false;
            }
            return cached;
        }

        /// <summary>Run the full plan now and return the multimodal set (also usable headlessly).</summary>
        public TrajectorySet Plan() => Plan(true);

        /// <summary>
        /// Plan with optional scene-obstacle repopulation. Pass populateField=false to plan against the
        /// CURRENT ObstacleField (used by headless tests that set obstacles manually).
        /// </summary>
        public TrajectorySet Plan(bool populateField)
        {
            TryResolve();
            Vector3 s = startRef != null ? startRef.position + Vector3.up * 0.02f : start;
            Vector3 g = goalRef  != null ? goalRef.position  + Vector3.up * 0.08f : goal;

            if (populateField)
            {
                // Build obstacles from the scene, ignoring the arm + the start/goal anchors' owners.
                var armRoot = GameObject.Find("Arm");
                field.PopulateFromScene(armRoot != null ? armRoot.transform : null,
                                        goalRef, includeDynamic: false);
            }

            var set = new TrajectorySet { source = "mpd", start = s, goal = g, hasStartGoal = true };

            for (int k = 0; k < Mathf.Max(1, candidates); k++)
            {
                var path = SeedTrajectory(s, g, k);
                Denoise(path, s, g, k);
                var samp = new TrajectorySample(path)
                {
                    label = "mpd#" + k,
                    cost = PathCost(path),
                };
                set.Add(samp);
            }
            set.MarkBestChosen();

            // weight (probability-ish) from cost for alpha in the visualizer
            float cMin = float.MaxValue, cMax = float.MinValue;
            foreach (var smp in set.samples) { cMin = Mathf.Min(cMin, smp.cost); cMax = Mathf.Max(cMax, smp.cost); }
            float span = Mathf.Max(1e-4f, cMax - cMin);
            foreach (var smp in set.samples) smp.weight = 1f - 0.7f * ((smp.cost - cMin) / span);
            return set;
        }

        List<Vector3> SeedTrajectory(Vector3 s, Vector3 g, int seed)
        {
            var rng = new System.Random(1000 + seed * 7919);
            var pts = new List<Vector3>(pointsPerPath);
            // Bias each candidate toward a different "mode" via a control-point offset direction.
            Vector3 dir = (g - s); float len = dir.magnitude; dir = len > 1e-4f ? dir / len : Vector3.forward;
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            float lobe = (seed % 2 == 0 ? 1f : -1f) * (0.04f + 0.05f * (seed / 2));
            Vector3 ctrl = (s + g) * 0.5f + side * lobe + Vector3.up * (0.04f + 0.03f * (seed % 3));
            for (int i = 0; i < pointsPerPath; i++)
            {
                float t = i / (float)(pointsPerPath - 1);
                float u = 1 - t;
                Vector3 p = u * u * s + 2 * u * t * ctrl + t * t * g;   // quadratic bezier base
                // add interior noise (endpoints stay anchored)
                float anchor = Mathf.Abs(t - 0.5f) * 2f;               // 0 mid, 1 ends
                float amp = initialNoise * (1f - anchor);
                p += new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)) * amp;
                pts.Add(p);
            }
            return pts;
        }

        void Denoise(List<Vector3> x, Vector3 s, Vector3 g, int seed)
        {
            int H = x.Count;
            var tmp = new Vector3[H];
            for (int step = 0; step < denoiseSteps; step++)
            {
                float schedule = 1f - step / (float)denoiseSteps;   // decays 1→0 (diffusion noise schedule)
                float a = guidanceStep * (0.4f + 0.6f * schedule);
                float b = smoothing;

                // 1) smoothing prior FIRST (Laplacian toward neighbour mean) — into tmp to avoid in-place bias
                for (int i = 1; i < H - 1; i++)
                {
                    Vector3 mean = (x[i - 1] + x[i + 1]) * 0.5f;
                    tmp[i] = Vector3.Lerp(x[i], mean, b);
                }
                for (int i = 1; i < H - 1; i++) x[i] = tmp[i];

                // 2) cost-guided push away from obstacles AFTER smoothing, so the dodge isn't flattened
                //    back into the obstacle. Iterate a few sub-steps for points that are deep inside.
                for (int i = 1; i < H - 1; i++)
                {
                    for (int sub = 0; sub < 3; sub++)
                    {
                        Vector3 grad = field.Gradient(x[i]);
                        if (grad.sqrMagnitude < 1e-8f) break;
                        x[i] += grad * a;     // gradient points AWAY from obstacle (outward) -> add to move out
                    }
                }

                // 3) endpoint anchoring (inpainting) + floor clamp
                x[0] = s; x[H - 1] = g;
                for (int i = 0; i < H; i++) { var p = x[i]; if (p.y < floorY) p.y = floorY; x[i] = p; }
            }
        }

        float PathCost(List<Vector3> pts)
        {
            float L = 0f;
            for (int i = 1; i < pts.Count; i++) L += Vector3.Distance(pts[i - 1], pts[i]);
            float coll = field.MaxCostAlong(pts);
            return L + coll * 2f;   // length + heavy collision weight
        }
    }
}
