using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith
{
    /// <summary>
    /// Robot-arm BUILDER panel (left dock, toggle with G... key set in GameBootstrap). Shows:
    ///   - Arm model + live stats (DOF, reach, total servos).
    ///   - Sensor MODULE toggles (clickable buttons: enable/disable each module to compare).
    ///   - TRAINING / generations view: start/stop, +1 gen, motion vs policy mode, generation counter,
    ///     best fitness, and a compact population fitness bar list.
    /// Implements I50 (builder UI with modules + training/generations).
    /// </summary>
    public class BuilderPanel : MonoBehaviour
    {
        public ProceduralArm arm;
        public SensorHub hub;
        public EvolutionTrainer trainer;
        public bool show = true;

        GameObject root;
        Text stats;
        Text training;
        Button[] moduleButtons;
        Text[] moduleLabels;

        public void Build(Transform canvas, ProceduralArm a, SensorHub h, EvolutionTrainer t)
        {
            arm = a; hub = h; trainer = t;

            root = new GameObject("BuilderPanel");
            root.transform.SetParent(canvas, false);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.07f, 0.10f, 0.9f);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(10, 30);
            // Height is computed from content so the bottom training block never overlaps the module list.
            int n = hub.sensors.Count;
            const float statsH = 70f, modTop = 84f, modStep = 25f, trainH = 170f, pad = 12f;
            float panelH = modTop + n * modStep + trainH + pad;   // stats + modules + training, stacked
            rt.sizeDelta = new Vector2(250, panelH);

            stats = MakeText(root.transform, 14, new Vector2(0, 1), new Vector2(8, -8), new Vector2(234, statsH));
            stats.color = new Color(0.8f, 0.9f, 1f);

            // module toggle buttons
            moduleButtons = new Button[n]; moduleLabels = new Text[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var bgo = new GameObject("mod_" + hub.sensors[i].Name);
                bgo.transform.SetParent(root.transform, false);
                var img = bgo.AddComponent<Image>();
                var brt = img.rectTransform;
                brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 1);
                brt.sizeDelta = new Vector2(234, 22);
                brt.anchoredPosition = new Vector2(8, -modTop - i * modStep);
                var btn = bgo.AddComponent<Button>();
                btn.onClick.AddListener(() => hub.sensors[idx].Enabled = !hub.sensors[idx].Enabled);
                moduleButtons[i] = btn;
                moduleLabels[i] = MakeText(bgo.transform, 12, Vector2.zero, new Vector2(6, 0), new Vector2(222, 22));
                moduleLabels[i].alignment = TextAnchor.MiddleLeft;
            }

            // training view: anchored TOP-DOWN immediately BELOW the module list (was bottom-anchored, which
            // overlapped the last module rows once fonts/heights grew). Clear, non-overlapping zone now.
            float trainY = -(modTop + n * modStep + 6f);
            training = MakeText(root.transform, 13, new Vector2(0, 1), new Vector2(8, trainY), new Vector2(234, trainH));
            training.alignment = TextAnchor.UpperLeft;
        }

        Text MakeText(Transform parent, int size, Vector2 anchor, Vector2 pos, Vector2 sz)
        {
            var go = new GameObject("t"); go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size; t.color = Color.white; t.supportRichText = true;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            return t;
        }

        void Update()
        {
            if (root == null) return;
            if (Input.GetKeyDown(KeyCode.G) && Input.GetKey(KeyCode.LeftShift)) show = !show; // Shift+G toggle
            root.SetActive(show);
            if (!show) return;

            stats.text = $"<b><color=#9cf>ARM BUILDER</color></b>\n" +
                         $"model: {(arm.config != null ? arm.config.armName : "-")}\n" +
                         $"DOF: {arm.jointBodies.Count}   servos: {arm.servos.Count}\n" +
                         $"reach: {(arm.config != null ? arm.config.TotalReach() : 0):F2} m";

            for (int i = 0; i < moduleButtons.Length; i++)
            {
                var s = hub.sensors[i];
                moduleButtons[i].GetComponent<Image>().color = s.Enabled
                    ? new Color(0.15f, 0.4f, 0.25f, 1f) : new Color(0.3f, 0.12f, 0.12f, 1f);
                moduleLabels[i].text = $"{(s.Enabled ? "\u2611" : "\u2610")} {s.Name} ({s.Channels.Length})";
            }

            var sb = new StringBuilder();
            sb.AppendLine("<b><color=#7cf>TRAINING</color></b>  (T run, N +1, F8 mode)");
            sb.AppendLine($"mode: {(trainer.policyMode ? "POLICY (sensors)" : "motion")}");
            sb.AppendLine($"running: {(trainer.Running ? "<color=#6f6>YES</color>" : "no")}  gen {trainer.generation}");
            float best = trainer.policyMode
                ? (trainer.bestPolicy != null ? trainer.bestPolicy.fitness : float.NaN)
                : (trainer.best != null ? trainer.best.fitness : float.NaN);
            sb.AppendLine($"best fitness: {(float.IsNaN(best) ? "-" : best.ToString("F2"))}");
            sb.AppendLine($"success rate: <color=#6f6>{(trainer.lastSuccessRate * 100f):F0}%</color> (over {trainer.evalResets} random resets)");
            // compact population fitness bars
            if (!trainer.policyMode && trainer.population != null)
            {
                int shown = Mathf.Min(8, trainer.population.Count);
                for (int i = 0; i < shown; i++)
                {
                    float f = trainer.population[i].fitness;
                    sb.AppendLine($"  #{i} {(f <= -1e29f ? "(unevaluated)" : f.ToString("F1"))}");
                }
            }
            training.text = sb.ToString();
        }
    }
}
