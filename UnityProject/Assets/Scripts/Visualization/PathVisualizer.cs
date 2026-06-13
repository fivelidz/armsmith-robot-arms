using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Visualization
{
    /// <summary>
    /// Draws robot-arm trajectories directly in the 3D world using GL immediate-mode (same lightweight
    /// approach as WorkspaceMap) — no GameObject-per-line, so it scales to MANY candidate paths cheaply.
    ///
    /// Purpose (user direction S7d): "drawing diffusion paths / showing more data visually in the world
    /// is good." This is the visual layer the diffusion planner / IK preview / GA trainer all feed via
    /// ITrajectoryProvider + TrajectorySet. It can show:
    ///   • a single planned path + the executed path (different colours)
    ///   • a MULTIMODAL set of candidate paths (the left-vs-right routes a diffusion planner samples),
    ///     coloured/alpha'd by cost or weight, with the chosen one highlighted
    ///   • waypoint spheres + start/goal markers
    ///   • a DENOISING animation (path refining noisy -> smooth) to explain the diffusion concept
    ///
    /// Drive it either by registering ITrajectoryProvider(s) via Register(), or by pushing a TrajectorySet
    /// directly via Show(). Pure rendering — never touches physics, so it's safe regardless of arm state.
    /// </summary>
    public class PathVisualizer : MonoBehaviour
    {
        public bool show = true;
        [Tooltip("Draw small spheres at each waypoint.")]
        public bool drawWaypoints = true;
        [Tooltip("Draw start (green) and goal (red) markers.")]
        public bool drawStartGoal = true;
        [Range(1, 8)] public int lineThickness = 2;     // emulated via parallel offset lines
        public float waypointSize = 0.006f;
        public float markerSize = 0.012f;

        // Colour ramp: low cost / chosen = bright cyan-green; high cost = dim magenta.
        public Color chosenColor = new Color(0.20f, 1.0f, 0.55f, 0.95f);
        public Color altColorLow = new Color(0.30f, 0.80f, 1.0f, 0.55f);
        public Color altColorHigh = new Color(1.0f, 0.30f, 0.75f, 0.35f);
        public Color executedColor = new Color(1.0f, 0.85f, 0.15f, 0.95f);

        static Material lineMat;
        readonly List<ITrajectoryProvider> providers = new List<ITrajectoryProvider>();

        // Directly-pushed sets (for callers that don't want to implement a provider).
        TrajectorySet pushedPlanned;     // candidate / planned trajectories
        TrajectorySample pushedExecuted; // the actually-executed path (drawn distinctly)

        // ---- public API ----------------------------------------------------------------
        public void Register(ITrajectoryProvider p) { if (p != null && !providers.Contains(p)) providers.Add(p); }
        public void Unregister(ITrajectoryProvider p) { providers.Remove(p); }

        /// <summary>Push a set of planned/candidate trajectories to draw (replaces the previous pushed set).</summary>
        public void Show(TrajectorySet set) { pushedPlanned = set; }

        /// <summary>Push/extend the executed path (e.g. append the live tip position each frame).</summary>
        public void SetExecuted(TrajectorySample executed) { pushedExecuted = executed; }
        public void ClearExecuted() { pushedExecuted = null; }
        public void ClearPushed() { pushedPlanned = null; pushedExecuted = null; }

        // ---- rendering -----------------------------------------------------------------
        void EnsureMat()
        {
            if (lineMat != null) return;
            lineMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMat.SetInt("_ZWrite", 0);
            lineMat.SetInt("_Cull", 0);
            lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        }

        void OnRenderObject()
        {
            if (!show) return;
            EnsureMat();
            lineMat.SetPass(0);
            GL.PushMatrix();

            // gather everything to draw this frame
            foreach (var p in providers)
            {
                if (p == null || !p.VizEnabled) continue;
                var set = p.GetTrajectories();
                if (set != null) DrawSet(set);
            }
            if (pushedPlanned != null) DrawSet(pushedPlanned);
            if (pushedExecuted != null && pushedExecuted.Count > 1)
                DrawPolyline(pushedExecuted.points, executedColor, Mathf.Max(lineThickness, 2));

            GL.PopMatrix();
        }

        void DrawSet(TrajectorySet set)
        {
            if (set == null) return;
            // find cost range for the colour ramp
            float cMin = float.MaxValue, cMax = float.MinValue;
            for (int i = 0; i < set.Count; i++)
            {
                cMin = Mathf.Min(cMin, set.samples[i].cost);
                cMax = Mathf.Max(cMax, set.samples[i].cost);
            }
            float span = Mathf.Max(1e-4f, cMax - cMin);

            for (int i = 0; i < set.Count; i++)
            {
                var s = set.samples[i];
                if (s == null || s.Count < 2) continue;
                Color col;
                if (s.HasColorOverride) col = s.colorOverride;
                else if (s.chosen) col = chosenColor;
                else
                {
                    float t = (s.cost - cMin) / span;          // 0 = best, 1 = worst
                    col = Color.Lerp(altColorLow, altColorHigh, t);
                    col.a *= Mathf.Clamp01(s.weight <= 0f ? 1f : s.weight);
                }
                int th = s.chosen ? Mathf.Max(lineThickness, 3) : lineThickness;
                DrawPolyline(s.points, col, th);
                if (drawWaypoints) DrawWaypoints(s.points, col);
            }

            if (drawStartGoal && set.hasStartGoal)
            {
                DrawMarker(set.start, new Color(0.2f, 1f, 0.3f, 0.95f), markerSize);  // start = green
                DrawMarker(set.goal, new Color(1f, 0.25f, 0.25f, 0.95f), markerSize); // goal = red
            }
        }

        // Thickness is emulated by drawing a few parallel screen-facing offset lines.
        void DrawPolyline(List<Vector3> pts, Color col, int thickness)
        {
            if (pts == null || pts.Count < 2) return;
            var cam = Camera.current;
            for (int t = 0; t < Mathf.Max(1, thickness); t++)
            {
                Vector3 off = Vector3.zero;
                if (cam != null && t > 0)
                {
                    float o = (t - thickness * 0.5f) * 0.0008f;
                    off = cam.transform.up * o + cam.transform.right * o;
                }
                GL.Begin(GL.LINES);
                GL.Color(col);
                for (int i = 1; i < pts.Count; i++)
                {
                    GL.Vertex(pts[i - 1] + off);
                    GL.Vertex(pts[i] + off);
                }
                GL.End();
            }
        }

        void DrawWaypoints(List<Vector3> pts, Color col)
        {
            GL.Begin(GL.QUADS);
            GL.Color(col);
            var cam = Camera.current;
            Vector3 r = cam != null ? cam.transform.right : Vector3.right;
            Vector3 u = cam != null ? cam.transform.up : Vector3.up;
            float h = waypointSize;
            foreach (var p in pts)
            {
                GL.Vertex(p - r * h - u * h);
                GL.Vertex(p - r * h + u * h);
                GL.Vertex(p + r * h + u * h);
                GL.Vertex(p + r * h - u * h);
            }
            GL.End();
        }

        void DrawMarker(Vector3 p, Color col, float size)
        {
            var cam = Camera.current;
            Vector3 r = cam != null ? cam.transform.right : Vector3.right;
            Vector3 u = cam != null ? cam.transform.up : Vector3.up;
            GL.Begin(GL.QUADS);
            GL.Color(col);
            GL.Vertex(p - r * size - u * size);
            GL.Vertex(p - r * size + u * size);
            GL.Vertex(p + r * size + u * size);
            GL.Vertex(p + r * size - u * size);
            GL.End();
        }
    }
}
