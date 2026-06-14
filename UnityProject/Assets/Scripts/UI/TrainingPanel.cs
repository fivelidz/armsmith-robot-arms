using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Training interface (design/specs/TRAINING_REGIMEN.md §7). OnGUI immediate-mode panel: choose the
    /// BACKEND (model), start/stop/step training, watch LIVE CURVES (best/mean fitness + success-rate vs
    /// generation), and see progress (generation, eval status, curriculum level). Toggle with F3.
    /// Companion to ConditionsPanel (F4) which edits the TrainingConfig.
    /// </summary>
    public class TrainingPanel : MonoBehaviour
    {
        public EvolutionTrainer trainer;
        public bool show = true;

        Texture2D px;
        GUIStyle hdr, lbl, small;

        public void Bind(EvolutionTrainer t) { trainer = t; }

        void EnsureStyles()
        {
            if (px == null) { px = new Texture2D(1, 1); px.SetPixel(0, 0, Color.white); px.Apply(); }
            if (hdr == null)
            {
                hdr = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13, normal = { textColor = new Color(0.6f, 0.85f, 1f) } };
                lbl = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = Color.white } };
                small = new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) show = !show;
        }

        void OnGUI()
        {
            if (!show || trainer == null) return;
            EnsureStyles();
            const float W = 320f, X = 12f, Y = 60f;
            float h = 318f;
            GUI.color = new Color(0.05f, 0.07f, 0.10f, 0.92f);
            GUI.DrawTexture(new Rect(X, Y, W, h), px);
            GUI.color = Color.white;
            var r = new Rect(X + 10, Y + 8, W - 20, 20);

            GUI.Label(r, "TRAINING  (F3)", hdr); r.y += 22;

            // backend selector
            var cfg = trainer.config;
            GUI.Label(new Rect(r.x, r.y, 70, 20), "Model:", lbl);
            string[] backends = { "Motion-GA", "Sensor-Policy", "Diffusion" };
            int bi = (int)cfg.backend;
            for (int i = 0; i < 3; i++)
            {
                var br = new Rect(r.x + 64 + i * 84, r.y, 82, 20);
                bool on = bi == i;
                GUI.color = on ? new Color(0.2f, 0.6f, 1f) : new Color(0.15f, 0.18f, 0.22f);
                if (GUI.Button(br, backends[i])) { cfg.backend = (TrainingConfig.Backend)i; trainer.ApplyConfig(); }
                GUI.color = Color.white;
            }
            r.y += 26;

            // controls
            float bw = (W - 28) / 3f;
            if (GUI.Button(new Rect(r.x, r.y, bw, 24), trainer.Running ? "Stop" : "Start"))
            {
                if (trainer.Running) trainer.StopTraining(); else { trainer.ApplyConfig(); trainer.StartTraining(); }
            }
            if (GUI.Button(new Rect(r.x + bw + 4, r.y, bw, 24), "+1 Gen")) { trainer.ApplyConfig(); trainer.StepOneGeneration(); }
            if (GUI.Button(new Rect(r.x + 2 * (bw + 4), r.y, bw - 4, 24), "Reset")) trainer.ResetTraining();
            r.y += 30;

            // progress
            GUI.Label(r, $"Gen {trainer.generation}   {(trainer.Running ? "<RUNNING>" : "idle")}", lbl); r.y += 16;
            GUI.Label(r, $"Level: {cfg.LevelName()}  (diff {cfg.difficulty:F2})", small); r.y += 14;
            GUI.Label(r, trainer.status, small); r.y += 16;
            GUI.Label(r, $"best {trainer.lastBestFitness:F2}   mean {trainer.lastMeanFitness:F2}   succ {trainer.lastSuccessRate * 100f:F0}%", lbl); r.y += 20;

            // curves
            DrawCurves(new Rect(r.x, r.y, W - 20, 120));
        }

        void DrawCurves(Rect area)
        {
            GUI.color = new Color(0.02f, 0.03f, 0.05f, 1f);
            GUI.DrawTexture(area, px); GUI.color = Color.white;
            var best = trainer.bestHistory; var mean = trainer.meanHistory; var succ = trainer.successHistory;
            if (best.Count < 2) { GUI.Label(new Rect(area.x + 6, area.y + 50, area.width, 20), "  (run training to see curves)", small); return; }

            // fitness range
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in best) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            foreach (var v in mean) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            if (hi - lo < 1e-3f) hi = lo + 1f;

            PlotLine(area, best, lo, hi, new Color(0.3f, 1f, 0.5f));     // best = green
            PlotLine(area, mean, lo, hi, new Color(0.5f, 0.7f, 1f));     // mean = blue
            PlotLine(area, succ, 0f, 1f, new Color(1f, 0.8f, 0.2f));     // success = amber (0..1)

            GUI.Label(new Rect(area.x + 4, area.y + 2, area.width, 14), "<color=#5f9>best</color> <color=#8bf>mean</color> <color=#fc3>succ</color>",
                new GUIStyle(small) { richText = true });
        }

        void PlotLine(Rect area, System.Collections.Generic.List<float> data, float lo, float hi, Color col)
        {
            int n = data.Count; if (n < 2) return;
            float pad = 4f;
            for (int i = 1; i < n; i++)
            {
                float x0 = area.x + pad + (area.width - 2 * pad) * (i - 1) / (n - 1);
                float x1 = area.x + pad + (area.width - 2 * pad) * i / (n - 1);
                float y0 = area.yMax - pad - (area.height - 2 * pad) * Mathf.InverseLerp(lo, hi, data[i - 1]);
                float y1 = area.yMax - pad - (area.height - 2 * pad) * Mathf.InverseLerp(lo, hi, data[i]);
                DrawSeg(x0, y0, x1, y1, col);
            }
        }

        void DrawSeg(float x0, float y0, float x1, float y1, Color col)
        {
            // thin line via a rotated 1px texture
            Vector2 d = new Vector2(x1 - x0, y1 - y0);
            float len = d.magnitude; if (len < 0.5f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var prev = GUI.matrix; GUI.color = col;
            GUIUtility.RotateAroundPivot(ang, new Vector2(x0, y0));
            GUI.DrawTexture(new Rect(x0, y0 - 1f, len, 2f), px);
            GUI.matrix = prev; GUI.color = Color.white;
        }
    }
}
