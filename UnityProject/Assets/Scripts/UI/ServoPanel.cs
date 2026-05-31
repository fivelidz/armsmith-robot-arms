using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith
{
    /// <summary>
    /// On-screen panel showing live MOTOR (servo) values for each joint: name, current angle, target
    /// angle, servo tick (0..4096, what the real STS3215 receives), and a fill bar. These are the values
    /// that translate an arm point into motor commands — used for control feedback, training visibility,
    /// and real-robot export. Docked bottom-left, toggle with the period... (key set in GameBootstrap).
    /// </summary>
    public class ServoPanel : MonoBehaviour
    {
        public ProceduralArm arm;
        public ArmController controller;
        Text text;
        public bool show = true;

        public void Build(Transform canvas, ProceduralArm a, ArmController c)
        {
            arm = a; controller = c;

            var go = new GameObject("ServoPanel");
            go.transform.SetParent(canvas, false);

            // background
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.07f, 0.09f, 0.82f);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(10, 10);
            rt.sizeDelta = new Vector2(420, 230);

            var txtGo = new GameObject("txt");
            txtGo.transform.SetParent(go.transform, false);
            text = txtGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 15;
            text.color = Color.white;
            text.supportRichText = true;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 8); trt.offsetMax = new Vector2(-10, -8);
        }

        void Update()
        {
            if (text == null || arm == null) return;
            text.enabled = show;
            if (!show) return;

            float[] ang = arm.GetJointAngles();
            var sb = new StringBuilder();
            sb.AppendLine("<b><color=#fc6>SERVO MOTORS</color></b>  (deg \u2192 tick / 4096)");
            int n = arm.jointBodies.Count;
            for (int i = 0; i < n; i++)
            {
                var js = arm.jointSpecs[i];
                float cur = ang[i];
                float tgt = (controller != null && controller.TargetAngles != null && i < controller.TargetAngles.Length)
                    ? controller.TargetAngles[i] : cur;
                int tick = i < arm.servos.Count ? arm.servos[i].AngleToTick(cur) : 0;
                string bar = Bar(cur, js.minAngle, js.maxAngle, 12);
                string key = ArmController.JointKeyLabel(i);
                sb.AppendLine($"<color=#9cf>#{i + 1} {js.name,-12}</color>[{key,-3}] {cur,6:F1}\u00b0 \u2192{tgt,6:F1}\u00b0  <color=#8f8>{tick,4}</color>");
                sb.AppendLine($"   <color=#456>{bar}</color>  ({js.minAngle:F0}\u00b0..{js.maxAngle:F0}\u00b0)");
            }
            if (arm.gripper != null)
                sb.AppendLine($"<color=#fa6>Gripper</color> [,/.] {(arm.gripper.closeAmount * 100):F0}% closed");
            text.text = sb.ToString();
        }

        static string Bar(float v, float lo, float hi, int width)
        {
            float t = Mathf.InverseLerp(lo, hi, v);
            int fill = Mathf.RoundToInt(t * width);
            var sb = new StringBuilder();
            for (int i = 0; i < width; i++) sb.Append(i < fill ? '\u2588' : '\u2591');
            return sb.ToString();
        }
    }
}
