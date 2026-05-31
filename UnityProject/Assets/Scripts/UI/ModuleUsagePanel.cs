using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith
{
    /// <summary>
    /// Sensor-module transparency panel. For EACH module it shows live output values AND a clear
    /// USED-IN-TRAINING indicator: is this module's data actually in the observation the trainer consumes
    /// right now? (USED / idle / OFF). In open-loop "motion" evolution NO sensors are used (genome is
    /// joint keyframes); in "policy" mode the ENABLED modules ARE used. Answers the user's request:
    /// "a notice of if they are actually being used or factored in when training." Toggle F12.
    /// </summary>
    public class ModuleUsagePanel : MonoBehaviour
    {
        public SensorHub hub;
        public EvolutionTrainer trainer;
        Text text;
        public bool show = true;

        public void Build(Transform canvas, SensorHub h, EvolutionTrainer t)
        {
            hub = h; trainer = t;
            var go = new GameObject("ModuleUsagePanel");
            go.transform.SetParent(canvas, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.07f, 0.09f, 0.85f);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-10, -380);   // below the wrist+env camera panels
            rt.sizeDelta = new Vector2(310, 250);

            var txtGo = new GameObject("txt");
            txtGo.transform.SetParent(go.transform, false);
            text = txtGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 12;
            text.color = Color.white;
            text.supportRichText = true;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8, 6); trt.offsetMax = new Vector2(-8, -6);
        }

        void Update()
        {
            if (text == null || hub == null) return;
            if (Input.GetKeyDown(KeyCode.F12)) show = !show;
            text.enabled = show;
            if (!show) return;

            bool trainingUsesSensors = trainer != null && trainer.policyMode;

            var sb = new StringBuilder();
            sb.AppendLine("<b><color=#9cf>SENSOR MODULES</color></b>  (F12 hide)");
            sb.AppendLine(trainingUsesSensors
                ? "<color=#6f6>train mode: POLICY \u2014 sensors USED</color>"
                : "<color=#fa6>train mode: MOTION \u2014 sensors NOT factored in</color>");
            foreach (var s in hub.sensors)
            {
                float[] v = s.Observe();
                bool used = s.Enabled && trainingUsesSensors;
                string flag = !s.Enabled ? "<color=#a55>OFF</color>"
                            : used ? "<color=#6f6>USED</color>" : "<color=#cc6>idle</color>";
                sb.AppendLine($"<color=#bcd>{s.Name}</color> [{flag}] {s.Channels.Length}ch  <color=#789>{Preview(v)}</color>");
            }
            sb.AppendLine($"<color=#888>observation total: {hub.ObservationSize()} channels</color>");
            return_text(sb);
        }

        void return_text(StringBuilder sb) => text.text = sb.ToString();

        static string Preview(float[] v)
        {
            if (v == null || v.Length == 0) return "-";
            int n = Mathf.Min(v.Length, 3);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(v[i].ToString("F2")).Append(i < n - 1 ? "," : "");
            if (v.Length > n) sb.Append($"..+{v.Length - n}");
            return sb.ToString();
        }
    }
}
