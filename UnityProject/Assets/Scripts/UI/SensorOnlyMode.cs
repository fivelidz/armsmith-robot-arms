using UnityEngine;
using UnityEngine.UIElements;

namespace ArmSmith.UI
{
    /// <summary>
    /// SP1 — SENSOR-ONLY PLAY MODE. A teleop mode where the human operates the arm using ONLY the
    /// sensor-module information (the exact information budget a trained policy gets) — the 3rd-person
    /// god-view of true object positions is BLACKED OUT, and the live sensor channels are surfaced front
    /// and centre. This enables a direct human-vs-policy comparison and demonstrates what the policy must
    /// solve from. Toggle with Shift+S.
    ///
    /// Built as a UI Toolkit overlay (consistent with the new interface). Renders a near-opaque "blackout"
    /// over the scene plus a live readout of every enabled sensor module's channels.
    /// </summary>
    public class SensorOnlyMode : MonoBehaviour
    {
        public SensorHub sensorHub;
        public ArmController controller;
        public ProceduralArm arm;
        public bool active = false;

        UIDocument _doc;
        VisualElement _root, _readout;
        Label _hint;

        public void Bind(SensorHub hub, ArmController ctrl, ProceduralArm a) { sensorHub = hub; controller = ctrl; arm = a; }

        void Start()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) _doc = gameObject.AddComponent<UIDocument>();
            if (_doc.panelSettings == null) _doc.panelSettings = UiTheme.GetPanelSettings();
            Build();
            SetActive(active);
        }

        void Build()
        {
            _root = _doc.rootVisualElement;
            _root.Clear();
            _root.style.flexGrow = 1;
            // near-opaque blackout so the operator cannot see true object positions
            _root.style.backgroundColor = new Color(0.02f, 0.03f, 0.04f, 0.97f);

            var header = UiTheme.Row();
            header.style.height = 40; header.style.paddingLeft = 14; header.style.backgroundColor = UiTheme.Card2;
            var t = new Label("SENSOR-ONLY TELEOP"); t.style.color = UiTheme.Orange; t.style.fontSize = 16; t.style.unityFontStyleAndWeight = FontStyle.Bold; t.style.letterSpacing = 2f;
            header.Add(t);
            var sp = new VisualElement(); sp.style.flexGrow = 1; header.Add(sp);
            header.Add(UiTheme.Btn("Exit (Shift+S)", () => SetActive(false), UiTheme.Teal));
            _root.Add(header);

            var body = new VisualElement(); UiTheme.Pad(body, 16); body.style.flexGrow = 1; _root.Add(body);
            body.Add(UiTheme.Lbl("You are driving the arm from SENSOR DATA ONLY — the true object positions are hidden.", UiTheme.Muted, 12));
            body.Add(UiTheme.Lbl("This is exactly the information a trained policy receives. Control with the normal keys.", UiTheme.Muted, 12));
            _hint = UiTheme.Lbl("", UiTheme.Teal, 12); body.Add(_hint);

            _readout = new ScrollView(); _readout.style.flexGrow = 1; _readout.style.marginTop = 10;
            body.Add(_readout);
        }

        public void SetActive(bool on)
        {
            active = on;
            if (_root != null) _root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.S) && Input.GetKey(KeyCode.LeftShift)) SetActive(!active);
            if (!active || _readout == null || sensorHub == null) return;
            RefreshReadout();
        }

        float _accum;
        void RefreshReadout()
        {
            _accum += Time.unscaledDeltaTime;
            if (_accum < 0.1f) return;   // 10 Hz refresh
            _accum = 0f;

            _readout.Clear();
            if (_hint != null && controller != null)
                _hint.text = $"mode: {(controller.mode == ArmController.Mode.IK ? "IK" : "Manual")}   grip: {(arm != null && arm.gripper != null ? (arm.gripper.closeAmount * 100f).ToString("F0") + "%" : "—")}";

            foreach (var s in sensorHub.Sensors)
            {
                if (!s.Enabled) continue;
                var panel = UiTheme.Panel(UiTheme.Teal);
                panel.Add(UiTheme.PanelHeader(s.Name, UiTheme.Teal, s.Channels.Length + " ch"));
                var b = new VisualElement(); UiTheme.Pad(b, 8); panel.Add(b);
                float[] vals = (s is SensorBase sb) ? sb.ObserveNoisy() : s.Observe();
                var ch = s.Channels;
                int n = Mathf.Min(vals.Length, ch.Length);
                for (int i = 0; i < n; i++)
                {
                    var row = UiTheme.Row(); row.style.justifyContent = Justify.SpaceBetween;
                    row.Add(UiTheme.Caption(ch[i]));
                    row.Add(UiTheme.Lbl(vals[i].ToString("F3"), UiTheme.Teal, 11));
                    b.Add(row);
                }
                _readout.Add(panel);
            }
        }
    }
}
