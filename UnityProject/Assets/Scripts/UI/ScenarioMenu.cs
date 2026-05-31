using System;
using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith
{
    /// <summary>
    /// On-screen scenario selection menu (top-center). Lists every scenario as a clickable button with
    /// its name; the active one is highlighted and its objective shown. Click to load. Toggle with Tab-ish
    /// key (set in GameBootstrap: 'F1'... here we just always show it, compact). Implements I49.
    /// </summary>
    public class ScenarioMenu : MonoBehaviour
    {
        public ScenarioManager scenarios;
        Button[] buttons;
        Text objective;
        public bool show = true;
        GameObject root;

        public void Build(Transform canvas, ScenarioManager s)
        {
            scenarios = s;
            var go = new GameObject("ScenarioMenu");
            root = go;
            go.transform.SetParent(canvas, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.07f, 0.10f, 0.85f);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -8);
            rt.sizeDelta = new Vector2(760, 56);

            var values = (ScenarioType[])Enum.GetValues(typeof(ScenarioType));
            buttons = new Button[values.Length];
            float bw = 740f / values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                int idx = i;
                var bgo = new GameObject("btn_" + values[i]);
                bgo.transform.SetParent(go.transform, false);
                var img = bgo.AddComponent<Image>();
                img.color = new Color(0.12f, 0.15f, 0.18f, 1f);
                var brt = img.rectTransform;
                brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 1);
                brt.sizeDelta = new Vector2(bw - 4, 26);
                brt.anchoredPosition = new Vector2(8 + i * bw, -4);
                var btn = bgo.AddComponent<Button>();
                buttons[i] = btn;
                var st = values[i];
                btn.onClick.AddListener(() => scenarios.LoadScenario(st));

                var tgo = new GameObject("t"); tgo.transform.SetParent(bgo.transform, false);
                var t = tgo.AddComponent<Text>();
                t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                t.fontSize = 12; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white;
                t.text = ShortName(values[i]);
                var trt = t.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            }

            // objective line under the buttons
            var ogo = new GameObject("objective"); ogo.transform.SetParent(go.transform, false);
            objective = ogo.AddComponent<Text>();
            objective.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            objective.fontSize = 12; objective.color = new Color(0.9f, 0.85f, 0.5f);
            objective.alignment = TextAnchor.MiddleCenter;
            var ort = objective.rectTransform;
            ort.anchorMin = new Vector2(0, 0); ort.anchorMax = new Vector2(1, 0); ort.pivot = new Vector2(0.5f, 0);
            ort.offsetMin = new Vector2(6, 3); ort.offsetMax = new Vector2(-6, 22);
        }

        void Update()
        {
            if (root != null && Input.GetKeyDown(KeyCode.F1) && Input.GetKey(KeyCode.LeftShift)) show = !show;
            if (root != null) root.SetActive(show);
            if (!show || scenarios == null || buttons == null) return;
            var values = (ScenarioType[])Enum.GetValues(typeof(ScenarioType));
            for (int i = 0; i < buttons.Length; i++)
            {
                var img = buttons[i].GetComponent<Image>();
                img.color = values[i] == scenarios.current
                    ? new Color(0.2f, 0.55f, 0.35f, 1f)   // active = green
                    : new Color(0.12f, 0.15f, 0.18f, 1f);
            }
            if (objective != null) objective.text = scenarios.Objective();
        }

        static string ShortName(ScenarioType t)
        {
            switch (t)
            {
                case ScenarioType.ReachTouch: return "Reach";
                case ScenarioType.PushToZone: return "Push";
                case ScenarioType.PickPlaceCube: return "Pick&Place";
                case ScenarioType.TrayToTray: return "Tray\u2192Tray";
                case ScenarioType.StackTwo: return "Stack";
                case ScenarioType.DropInBin: return "Bin";
                case ScenarioType.SortIntoTray: return "Sort\u2192Tray";
                default: return t.ToString();
            }
        }
    }
}
