using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// GENERATIONS &amp; CREATIONS panel (toggle F7). The interface the user asked for: SEE the evolving
    /// generations and the best "creations", and CONTROL training/breeding/selection. OnGUI immediate-mode
    /// so it can render rich scrolling content; scaled via UiScale to match the Canvas UI on high-res.
    ///
    /// Layout (left column, below the BuilderPanel):
    ///   - Header + live status (gen, running, best/mean/success).
    ///   - Controls row: Run/Stop, +1 Gen, Reset, Save ckpt, Load ckpt, backend toggle.
    ///   - Fitness curve (best=green, mean=blue, success=amber) vs generation.
    ///   - CREATIONS list (best-of-generation, newest first): gen, fitness, success, scenario, [Replay].
    ///   - POPULATION grid for the current generation with per-individual fitness bars; click to LOCK a
    ///     survivor (interactive evolution — uses the trainer's existing ToggleSelect/playerSelectionMode).
    /// </summary>
    public class GenerationsPanel : MonoBehaviour
    {
        public EvolutionTrainer trainer;
        public bool show = false;     // opt-in (F7)

        Texture2D px;
        GUIStyle hdr, lbl, small, btn;
        Vector2 scrollCreations, scrollPop;

        public void Bind(EvolutionTrainer t) { trainer = t; }

        void EnsureStyles()
        {
            if (px == null) { px = new Texture2D(1, 1); px.SetPixel(0, 0, Color.white); px.Apply(); }
            if (hdr == null)
            {
                hdr = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 17, normal = { textColor = new Color(0.6f, 1f, 0.7f) } };
                lbl = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(0.82f, 0.82f, 0.82f) }, richText = true };
                btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            }
        }

        void Update() { if (Input.GetKeyDown(KeyCode.F7)) show = !show; }

        void OnGUI()
        {
            if (!show || trainer == null) return;
            EnsureStyles();
            var uiPrev = UiScale.Begin();

            const float W = 430f, X = 12f, Y = 60f;
            float H = UiScale.LogicalHeight - 120f;
            GUI.color = new Color(0.05f, 0.09f, 0.07f, 0.94f);
            GUI.DrawTexture(new Rect(X, Y, W, H), px); GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(X + 10, Y + 8, W - 20, H - 16));
            GUILayout.Label("GENERATIONS &  CREATIONS  (F7)", hdr);

            // live status
            bool policy = trainer.policyMode;
            GUILayout.Label($"backend: <b>{(policy ? "Sensor-Policy" : "Motion-GA")}</b>   gen <b>{trainer.generation}</b>   {(trainer.Running ? "<color=#6f6>RUNNING</color>" : "idle")}", small);
            GUILayout.Label($"best <b>{trainer.lastBestFitness:F2}</b>   mean <b>{trainer.lastMeanFitness:F2}</b>   success <color=#fc3>{trainer.lastSuccessRate * 100f:F0}%</color>", small);

            // controls
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(trainer.Running ? "Stop" : "Run", btn, GUILayout.Height(26)))
            { if (trainer.Running) trainer.StopTraining(); else { trainer.ApplyConfig(); trainer.StartTraining(); } }
            if (GUILayout.Button("+1 Gen", btn, GUILayout.Height(26))) { trainer.ApplyConfig(); trainer.StepOneGeneration(); }
            if (GUILayout.Button("Reset", btn, GUILayout.Height(26))) trainer.ResetTraining();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save ckpt", btn, GUILayout.Height(24))) trainer.SaveCheckpoint();
            GUI.enabled = EvolutionStore.HasCheckpoint();
            if (GUILayout.Button("Load ckpt", btn, GUILayout.Height(24))) trainer.LoadCheckpoint();
            GUI.enabled = true;
            if (GUILayout.Button("Snapshot best", btn, GUILayout.Height(24))) trainer.CaptureCreation();
            GUILayout.EndHorizontal();

            // fitness curve
            DrawCurves(GUILayoutUtility.GetRect(W - 24, 90));

            // CREATIONS (best-of-generation), newest first
            GUILayout.Label("<b>Creations</b> (best per generation — click Replay to watch)", small);
            scrollCreations = GUILayout.BeginScrollView(scrollCreations, GUILayout.Height(150));
            var creations = trainer.creations;
            for (int i = creations.Count - 1; i >= 0; i--)
            {
                var c = creations[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"gen {c.generation}  fit <color=#9f9>{c.fitness:F1}</color>  succ {c.successRate * 100f:F0}%  <i>{c.scenario}</i>", small, GUILayout.Width(W - 110));
                if (GUILayout.Button("Replay", btn, GUILayout.Width(78), GUILayout.Height(20))) trainer.ReplayCreation(c);
                GUILayout.EndHorizontal();
            }
            if (creations.Count == 0) GUILayout.Label("  (run training — best of each generation is saved here)", small);
            GUILayout.EndScrollView();

            // POPULATION grid (current generation), click to lock survivors (interactive evolution)
            GUILayout.Label($"<b>Population</b> (gen {trainer.generation}) — click to LOCK a survivor", small);
            trainer.playerSelectionMode = GUILayout.Toggle(trainer.playerSelectionMode, " interactive evolution (breed only from locked)");
            scrollPop = GUILayout.BeginScrollView(scrollPop);
            DrawPopulation(W - 28);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
            UiScale.End(uiPrev);
        }

        void DrawPopulation(float width)
        {
            if (trainer.policyMode)
            {
                var pp = trainer.policyPop;
                for (int i = 0; i < pp.Count; i++) PopRow(i, pp[i].fitness, width);
            }
            else
            {
                var pop = trainer.population;
                for (int i = 0; i < pop.Count; i++) PopRow(i, pop[i].fitness, width);
            }
        }

        void PopRow(int i, float fitness, float width)
        {
            bool locked = trainer.selected.Contains(i);
            GUILayout.BeginHorizontal();
            GUI.color = locked ? new Color(0.3f, 0.7f, 1f) : new Color(0.2f, 0.25f, 0.3f);
            if (GUILayout.Button(locked ? $"#{i} \u25cf" : $"#{i}", btn, GUILayout.Width(54), GUILayout.Height(18)))
                trainer.ToggleSelect(i);
            GUI.color = Color.white;
            // fitness bar
            var r = GUILayoutUtility.GetRect(width - 60, 16);
            bool evald = fitness > -1e29f;
            GUI.color = new Color(0.1f, 0.12f, 0.15f); GUI.DrawTexture(r, px);
            if (evald)
            {
                float n = Mathf.InverseLerp(-2f, 6f, fitness);   // rough normalisation for the bar
                GUI.color = Color.Lerp(new Color(0.8f, 0.3f, 0.2f), new Color(0.3f, 1f, 0.5f), n);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(n), r.height), px);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 4, r.y - 1, r.width, 18), evald ? fitness.ToString("F2") : "(unevaluated)", small);
            GUILayout.EndHorizontal();
        }

        void DrawCurves(Rect area)
        {
            GUI.color = new Color(0.02f, 0.04f, 0.03f, 1f); GUI.DrawTexture(area, px); GUI.color = Color.white;
            var best = trainer.bestHistory; var mean = trainer.meanHistory; var succ = trainer.successHistory;
            if (best.Count < 2) { GUI.Label(new Rect(area.x + 6, area.y + 36, area.width, 20), "  (run training to see fitness curves)", small); return; }
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in best) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            foreach (var v in mean) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            if (hi - lo < 1e-3f) hi = lo + 1f;
            Plot(area, best, lo, hi, new Color(0.3f, 1f, 0.5f));
            Plot(area, mean, lo, hi, new Color(0.5f, 0.7f, 1f));
            Plot(area, succ, 0f, 1f, new Color(1f, 0.8f, 0.2f));
            GUI.Label(new Rect(area.x + 4, area.y + 2, area.width, 16),
                "<color=#5f9>best</color> <color=#8bf>mean</color> <color=#fc3>succ</color>", small);
        }

        void Plot(Rect area, System.Collections.Generic.List<float> data, float lo, float hi, Color col)
        {
            int n = data.Count; if (n < 2) return;
            float pad = 4f;
            for (int i = 1; i < n; i++)
            {
                float x0 = area.x + pad + (area.width - 2 * pad) * (i - 1) / (n - 1);
                float x1 = area.x + pad + (area.width - 2 * pad) * i / (n - 1);
                float y0 = area.yMax - pad - (area.height - 2 * pad) * Mathf.InverseLerp(lo, hi, data[i - 1]);
                float y1 = area.yMax - pad - (area.height - 2 * pad) * Mathf.InverseLerp(lo, hi, data[i]);
                Seg(x0, y0, x1, y1, col);
            }
        }

        void Seg(float x0, float y0, float x1, float y1, Color col)
        {
            Vector2 d = new Vector2(x1 - x0, y1 - y0); float len = d.magnitude; if (len < 0.5f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var prev = GUI.matrix; GUI.color = col;
            GUIUtility.RotateAroundPivot(ang, new Vector2(x0, y0));
            GUI.DrawTexture(new Rect(x0, y0 - 1f, len, 2f), px);
            GUI.matrix = prev; GUI.color = Color.white;
        }
    }
}
