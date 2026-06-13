using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Visualization
{
    /// <summary>
    /// A single end-effector-space trajectory: an ordered list of world-space points the tip is
    /// planned (or was observed) to pass through, plus metadata used for visualization (cost / weight,
    /// colour, whether it's the chosen/executed path). This is the COMMON currency between every path
    /// source — Jacobian IK previews, GA-evolved motions, and (the goal) diffusion-planner samples all
    /// produce TrajectorySamples that the PathVisualizer can draw without knowing where they came from.
    ///
    /// Why EE-space points (not joint angles): visualization is about showing the human WHERE the arm
    /// will go. A joint-space trajectory can be converted to EE points via forward kinematics by the
    /// provider before handing it over (e.g. ArmController.IKAnglesFor / a FK pass).
    /// </summary>
    [System.Serializable]
    public class TrajectorySample
    {
        public List<Vector3> points = new List<Vector3>();   // world-space EE path
        public List<float> gripper;                          // optional 0..1 gripper state per point
        public float cost = 0f;        // lower = better (collision cost / path length / -reward)
        public float weight = 1f;      // probability / confidence in [0,1] — drives alpha/thickness
        public bool chosen = false;    // is this the selected/executed path among a multimodal set?
        public string label;           // optional tag ("ik", "ga", "diffusion#3", "denoise step 7"...)
        public Color colorOverride = new Color(0, 0, 0, 0);  // alpha>0 => use this instead of auto colour

        public TrajectorySample() { }
        public TrajectorySample(IEnumerable<Vector3> pts) { if (pts != null) points.AddRange(pts); }

        public bool HasColorOverride => colorOverride.a > 0.001f;
        public int Count => points != null ? points.Count : 0;

        /// <summary>Total path length in metres (useful as a cheap cost / for labelling).</summary>
        public float Length()
        {
            float L = 0f;
            for (int i = 1; i < Count; i++) L += Vector3.Distance(points[i - 1], points[i]);
            return L;
        }

        /// <summary>Linearly resample to exactly n points (handy for denoise animations / uniform draw).</summary>
        public TrajectorySample Resampled(int n)
        {
            var outS = new TrajectorySample { cost = cost, weight = weight, chosen = chosen, label = label, colorOverride = colorOverride };
            if (Count == 0 || n <= 0) return outS;
            if (Count == 1) { for (int i = 0; i < n; i++) outS.points.Add(points[0]); return outS; }
            float total = Length();
            if (total < 1e-6f) { for (int i = 0; i < n; i++) outS.points.Add(points[0]); return outS; }
            for (int k = 0; k < n; k++)
            {
                float t = (n == 1) ? 0f : (k / (float)(n - 1)) * total;
                outS.points.Add(PointAtArcLength(t));
            }
            return outS;
        }

        Vector3 PointAtArcLength(float s)
        {
            float acc = 0f;
            for (int i = 1; i < Count; i++)
            {
                float seg = Vector3.Distance(points[i - 1], points[i]);
                if (acc + seg >= s)
                {
                    float f = seg > 1e-6f ? (s - acc) / seg : 0f;
                    return Vector3.Lerp(points[i - 1], points[i], f);
                }
                acc += seg;
            }
            return points[Count - 1];
        }
    }

    /// <summary>
    /// A bundle of trajectories drawn together — e.g. the multimodal set of candidate paths a diffusion
    /// planner sampled for one start->goal query, or one IK preview. Carries shared start/goal markers.
    /// </summary>
    [System.Serializable]
    public class TrajectorySet
    {
        public List<TrajectorySample> samples = new List<TrajectorySample>();
        public Vector3 start;
        public Vector3 goal;
        public bool hasStartGoal = false;
        public string source = "";     // "ik" | "ga" | "diffusion" | "mpd" ...

        public void Add(TrajectorySample s) { if (s != null) samples.Add(s); }
        public int Count => samples != null ? samples.Count : 0;
        public void Clear() { samples.Clear(); hasStartGoal = false; }

        /// <summary>Mark the lowest-cost sample as chosen (others become alternatives).</summary>
        public void MarkBestChosen()
        {
            int best = -1; float bc = float.MaxValue;
            for (int i = 0; i < Count; i++) { if (samples[i].cost < bc) { bc = samples[i].cost; best = i; } }
            for (int i = 0; i < Count; i++) samples[i].chosen = (i == best);
        }
    }

    /// <summary>
    /// Anything that can supply trajectories for visualization implements this. The PathVisualizer polls
    /// registered providers each frame (or on demand), so new path sources (a diffusion inference client,
    /// an MPD planner, the IK preview) plug in without touching the visualizer.
    /// </summary>
    public interface ITrajectoryProvider
    {
        /// <summary>Return the current set to draw, or null if nothing to show right now.</summary>
        TrajectorySet GetTrajectories();

        /// <summary>Human-readable name (for the viz legend/toggles).</summary>
        string ProviderName { get; }

        /// <summary>Whether this provider's output should currently be drawn.</summary>
        bool VizEnabled { get; }
    }
}
