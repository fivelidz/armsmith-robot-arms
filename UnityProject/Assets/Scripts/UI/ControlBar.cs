using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith
{
    /// <summary>
    /// Clickable control bar (bottom-center): toggle buttons for all the VIEW overlays and key CONTROLS,
    /// so everything is mouse-operable (not just hotkeys). Buttons reflect live on/off state by colour.
    /// Implements I53/U4-U5. Sections: Views (lidar/range/depth/bounds/axes/cam-HUD/callouts) and
    /// Controls (mode IK/Manual, gripper, calibrate, pause, train).
    /// </summary>
    public class ControlBar : MonoBehaviour
    {
        public ArmController controller;
        public ProceduralArm arm;
        public SensorViz viz;
        public ArmGizmos gizmos;
        public CameraRig rig;
        public ServoCallouts callouts;
        public EvolutionTrainer trainer;
        public ScenarioManager scenarios;

        class Btn { public Image img; public Func<bool> isOn; public string label; public Text txt; }
        readonly List<Btn> buttons = new List<Btn>();
        Transform row;

        public void Build(Transform canvas, ArmController c, ProceduralArm a, SensorViz v, ArmGizmos g,
                          CameraRig r, ServoCallouts co, EvolutionTrainer t, ScenarioManager sm)
        {
            controller = c; arm = a; viz = v; gizmos = g; rig = r; callouts = co; trainer = t; scenarios = sm;

            var go = new GameObject("ControlBar");
            go.transform.SetParent(canvas, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.06f, 0.09f, 0.9f);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 8);
            rt.sizeDelta = new Vector2(1180, 40);
            row = go.transform;

            float x = 8f;
            x = AddLabel("VIEWS:", x);
            x = AddToggle("Lidar", x, () => viz.showLidar, () => viz.showLidar = !viz.showLidar);
            x = AddToggle("Range", x, () => viz.showRangeFinder, () => viz.showRangeFinder = !viz.showRangeFinder);
            x = AddToggle("Depth", x, () => viz.showDepth, () => viz.showDepth = !viz.showDepth);
            x = AddToggle("Bounds", x, () => gizmos.showWorkspace, () => gizmos.showWorkspace = !gizmos.showWorkspace);
            x = AddToggle("Axes", x, () => gizmos.showAxes, () => gizmos.showAxes = !gizmos.showAxes);
            x = AddToggle("Callouts", x, () => callouts.enabledCallouts, () => callouts.enabledCallouts = !callouts.enabledCallouts);
            x += 12;
            x = AddLabel("CTRL:", x);
            x = AddToggle("IK", x, () => controller.mode == ArmController.Mode.IK,
                          () => controller.mode = controller.mode == ArmController.Mode.IK ? ArmController.Mode.Manual : ArmController.Mode.IK);
            x = AddToggle("Mouse", x, () => controller.mouseFollow, () => controller.mouseFollow = !controller.mouseFollow);
            x = AddToggle("Grip", x, () => arm.gripper != null && arm.gripper.closeAmount > 0.5f,
                          () => { if (arm.gripper != null) arm.gripper.Toggle(); });
            x = AddToggle("Pause", x, () => controller.paused, () => controller.paused = !controller.paused);
            x = AddButton("Zero", x, () => controller.GoToZero());
            x = AddToggle("Train", x, () => trainer.Running, () => { if (trainer.Running) trainer.StopTraining(); else trainer.StartTraining(); });
            x = AddToggle("Random", x, () => scenarios != null && scenarios.randomness > 0.5f,
                          () => { if (scenarios != null) { scenarios.randomness = scenarios.randomness > 0.5f ? 0f : 1f; scenarios.Reroll(); } });
            x = AddButton("Reroll", x, () => { if (scenarios != null) scenarios.Reroll(); });
        }

        float AddLabel(string text, float x)
        {
            var go = new GameObject("lbl"); go.transform.SetParent(row, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 12; t.fontStyle = FontStyle.Bold; t.color = new Color(0.6f, 0.75f, 0.9f);
            t.alignment = TextAnchor.MiddleLeft; t.text = text;
            var rt = t.rectTransform; rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f); rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(48, 0);
            return x + 50;
        }

        float AddToggle(string label, float x, Func<bool> isOn, Action onClick)
        {
            var b = MakeButton(label, x, onClick);
            b.isOn = isOn;
            return x + 76;
        }

        float AddButton(string label, float x, Action onClick)
        {
            MakeButton(label, x, onClick);
            return x + 76;
        }

        Btn MakeButton(string label, float x, Action onClick)
        {
            var go = new GameObject("btn_" + label); go.transform.SetParent(row, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.13f, 0.16f, 0.2f, 1f);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(72, 28);
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick());

            var tgo = new GameObject("t"); tgo.transform.SetParent(go.transform, false);
            var txt = tgo.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 12; txt.alignment = TextAnchor.MiddleCenter; txt.color = Color.white; txt.text = label;
            var trt = txt.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            var b = new Btn { img = img, label = label, txt = txt };
            buttons.Add(b);
            return b;
        }

        void Update()
        {
            foreach (var b in buttons)
            {
                if (b.isOn == null) continue;
                b.img.color = b.isOn() ? new Color(0.15f, 0.5f, 0.3f, 1f) : new Color(0.13f, 0.16f, 0.2f, 1f);
            }
        }
    }
}
