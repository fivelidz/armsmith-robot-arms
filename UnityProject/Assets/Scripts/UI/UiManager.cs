using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArmSmith.UI
{
    /// <summary>
    /// ARMSMITH unified interface system (UI Toolkit). This is the "incorporated" interface the user asked
    /// for: a single overlay with a TOP NAV BAR + live STATUS BAR + switchable VIEWS (Dashboard, Training,
    /// Options, Help, Menu), all in the robotics-console design language from design/ui_html/. It renders a
    /// runtime UIDocument (PanelSettings built in code by UiTheme — no manual asset authoring), binds to the
    /// live game objects, and refreshes readouts each frame so EVERYTHING is conveyed to the user.
    ///
    /// It is ADDITIVE: the legacy IMGUI/uGUI panels keep working. Toggle the whole overlay with F1; switch
    /// views with the nav tabs or number-less hotkeys; the Menu view is the entry surface.
    ///
    /// Built + wired by GameBootstrap.BuildHud(). Headless-tested by Editor/UiToolkitCheck.
    /// </summary>
    public partial class UiManager : MonoBehaviour
    {
        public ProceduralArm arm;
        public ArmController controller;
        public ScenarioManager scenarios;
        public EvolutionTrainer trainer;
        public SensorHub sensorHub;
        public BehaviourRecorder recorder;
        public AgentCommands agent;          // text/auto-solve agent (Dashboard auto-solve button)
        public ModuleMount moduleMount;      // mount sockets + mounted modules (Modules view)
        public SaveSystem saveSystem;        // persist/restore all conditions + settings (Save/Load buttons)
        public ArmSmith.Modules.AttachmentSystem attachments;   // KSP-style 3D part attachment (Build/Modules)

        /// <summary>The legacy uGUI HUD canvas. When the new interface overlay is shown we HIDE this so the
        /// two don't overlap; restored when the overlay is hidden. Optional (null = ignore).</summary>
        public GameObject legacyHud;

        public enum View { Menu, Dashboard, Build, Modules, Catalogue, Training, Options, Help }
        public View current = View.Dashboard;
        public bool visible = true;

        UIDocument _doc;
        VisualElement _root, _navBar, _content, _statusBar;
        readonly Dictionary<View, Button> _navButtons = new Dictionary<View, Button>();

        // live status-bar labels
        Label _stSim, _stArm, _stTask, _stIk, _stGen, _stFps, _stMode;
        Button _navModeBtn;

        void ToggleModeGlobal()
        {
            if (controller == null) return;
            controller.mode = controller.mode == ArmController.Mode.IK ? ArmController.Mode.Manual : ArmController.Mode.IK;
        }
        // per-view refreshers (called each frame for the active view)
        Action _refresh;

        float _fps; int _frames; float _fpsTimer;

        public void Bind(ProceduralArm a, ArmController c, ScenarioManager s, EvolutionTrainer t,
                         SensorHub hub, BehaviourRecorder rec, AgentCommands ag = null, ModuleMount mm = null,
                         SaveSystem ss = null, ArmSmith.Modules.AttachmentSystem at = null)
        { arm = a; controller = c; scenarios = s; trainer = t; sensorHub = hub; recorder = rec; agent = ag; moduleMount = mm; saveSystem = ss; attachments = at; }

        void Start()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) _doc = gameObject.AddComponent<UIDocument>();
            if (_doc.panelSettings == null) _doc.panelSettings = UiTheme.GetPanelSettings();

            BuildRoot();
            SwitchTo(current);
            SetVisible(visible);
        }

        void Update()
        {
            // global toggle + view hotkeys
            if (Input.GetKeyDown(KeyCode.F1) && !Input.GetKey(KeyCode.LeftShift)) SetVisible(!visible);
            if (visible)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && current != View.Dashboard && current != View.Menu) SwitchTo(View.Dashboard);
                if (Input.GetKeyDown(KeyCode.H) && Input.GetKey(KeyCode.LeftShift)) SwitchTo(View.Help);
            }

            // fps
            _frames++; _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f) { _fps = _frames / _fpsTimer; _frames = 0; _fpsTimer = 0f; }

            if (visible && current == View.Dashboard) PollKeyRebind();
            RefreshStatusBar();
            _refresh?.Invoke();
        }

        public void SetVisible(bool v)
        {
            visible = v;
            if (_root != null) _root.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
            // Hide the legacy uGUI HUD while the new interface is up, so they don't overlap. The legacy
            // panels are still reachable (toggle the overlay off with F1 to get them back).
            if (legacyHud != null) legacyHud.SetActive(!v);
        }

        // ── ROOT: nav + content + status ───────────────────────────────────────────────────────────────
        void BuildRoot()
        {
            _root = _doc.rootVisualElement;
            _root.Clear();
            var style = UiTheme.LoadStyle();
            if (style != null && !_root.styleSheets.Contains(style)) _root.styleSheets.Add(style);
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.flexGrow = 1;

            BuildNavBar();
            _content = new VisualElement();
            _content.style.flexGrow = 1;
            _content.style.paddingLeft = 8; _content.style.paddingRight = 8; _content.style.paddingTop = 6; _content.style.paddingBottom = 6;
            _root.Add(_content);
            BuildStatusBar();
        }

        void BuildNavBar()
        {
            _navBar = UiTheme.Row();
            _navBar.style.height = 44;
            _navBar.style.backgroundColor = UiTheme.Card2;
            _navBar.style.borderBottomColor = UiTheme.Border; _navBar.style.borderBottomWidth = 1;
            _navBar.style.paddingLeft = 12; _navBar.style.paddingRight = 12;

            // logo
            var logo = new Label("ARM");
            logo.style.color = UiTheme.Teal; logo.style.unityFontStyleAndWeight = FontStyle.Bold;
            logo.style.fontSize = 17; logo.style.letterSpacing = 4f;
            var logo2 = new Label("SMITH");
            logo2.style.color = UiTheme.Orange; logo2.style.unityFontStyleAndWeight = FontStyle.Bold;
            logo2.style.fontSize = 17; logo2.style.letterSpacing = 4f; logo2.style.marginRight = 16;
            _navBar.Add(logo); _navBar.Add(logo2);

            // nav tabs
            AddNavTab("Menu", View.Menu);
            AddNavTab("Control", View.Dashboard);
            AddNavTab("Build", View.Build);
            AddNavTab("Modules", View.Modules);
            AddNavTab("Catalogue", View.Catalogue);
            AddNavTab("Training", View.Training);
            AddNavTab("Options", View.Options);
            AddNavTab("Help", View.Help);

            // spacer
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; _navBar.Add(spacer);

            // big clickable MODE indicator (every sim viewer has one — kills mode confusion)
            _navModeBtn = UiTheme.Btn("MODE", ToggleModeGlobal, UiTheme.Teal);
            _navModeBtn.style.minWidth = 96;
            _navBar.Add(_navModeBtn);

            // sim transport (right)
            _navBar.Add(UiTheme.Btn("▶ Play", () => Time.timeScale = Mathf.Max(1f, Time.timeScale), UiTheme.Green));
            _navBar.Add(UiTheme.Btn("⏸ Pause", () => Time.timeScale = 0f, UiTheme.Orange));
            _navBar.Add(UiTheme.Btn("↩ Reset", () => { if (scenarios != null) scenarios.LoadScenario(scenarios.current); }, UiTheme.Teal));

            _root.Add(_navBar);
        }

        void AddNavTab(string label, View view)
        {
            var b = UiTheme.Btn(label, () => SwitchTo(view));
            b.style.borderTopWidth = 0; b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            b.style.borderBottomWidth = 2; b.style.borderBottomColor = new Color(0, 0, 0, 0);
            b.style.marginRight = 2;
            _navButtons[view] = b;
            _navBar.Add(b);
        }

        public void SwitchTo(View view)
        {
            current = view;
            foreach (var kv in _navButtons)
            {
                bool on = kv.Key == view;
                kv.Value.style.color = on ? UiTheme.TextHi : UiTheme.Teal;
                kv.Value.style.borderBottomColor = on ? UiTheme.Teal : new Color(0, 0, 0, 0);
                kv.Value.style.backgroundColor = on ? new Color(UiTheme.Teal.r, UiTheme.Teal.g, UiTheme.Teal.b, 0.10f) : new Color(0, 0, 0, 0);
            }
            _content.Clear();
            _refresh = null;
            switch (view)
            {
                case View.Menu:      BuildMenuView(); break;
                case View.Dashboard: BuildDashboardView(); break;
                case View.Build:     BuildBuildView(); break;
                case View.Modules:   BuildModulesView(); break;
                case View.Catalogue: BuildCatalogueView(); break;
                case View.Training:  BuildTrainingView(); break;
                case View.Options:   BuildOptionsView(); break;
                case View.Help:      BuildHelpView(); break;
            }
        }

        // ── STATUS BAR ─────────────────────────────────────────────────────────────────────────────────
        void BuildStatusBar()
        {
            _statusBar = UiTheme.Row();
            _statusBar.style.height = 28;
            _statusBar.style.backgroundColor = UiTheme.Card2;
            _statusBar.style.borderTopColor = UiTheme.Border; _statusBar.style.borderTopWidth = 1;
            _statusBar.style.paddingLeft = 10; _statusBar.style.paddingRight = 10;

            _statusBar.Add(UiTheme.StatusDot("SIM LIVE", UiTheme.Green, out _stSim));
            AddStatusSep();
            _stArm = StatusCell("Arm SO-101");
            AddStatusSep();
            _stTask = StatusCell("Task —");
            AddStatusSep();
            _stIk = StatusCell("IK —");
            AddStatusSep();
            _stMode = StatusCell("Mode —");
            AddStatusSep();
            _stGen = StatusCell("Gen 0");
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; _statusBar.Add(spacer);
            _stFps = StatusCell("FPS 0");

            _root.Add(_statusBar);
        }

        Label StatusCell(string text)
        {
            var l = UiTheme.Lbl(text, UiTheme.Text, 10);
            l.style.marginLeft = 8; l.style.marginRight = 8;
            _statusBar.Add(l);
            return l;
        }
        void AddStatusSep()
        {
            var s = new VisualElement(); s.style.width = 1; s.style.height = 16; s.style.backgroundColor = UiTheme.Border;
            _statusBar.Add(s);
        }

        void RefreshStatusBar()
        {
            if (_statusBar == null) return;
            if (_stArm != null) _stArm.text = arm != null ? $"Arm SO-101 · {arm.jointSpecs.Count}-DOF" : "Arm —";
            if (_stTask != null && scenarios != null) _stTask.text = $"Task {scenarios.current}";
            if (_stIk != null && controller != null) _stIk.text = controller.mode == ArmController.Mode.IK ? "IK DLS ✓" : "IK off";
            if (_stMode != null && controller != null) _stMode.text = controller.mode == ArmController.Mode.IK ? "Mode IK" : "Mode Manual";
            if (_stGen != null && trainer != null) _stGen.text = $"Gen {trainer.generation} · {(trainer.Running ? "running" : "idle")}";
            if (_stFps != null) _stFps.text = $"FPS {_fps:F0}";
            if (_stSim != null) _stSim.text = Time.timeScale > 0f ? "SIM LIVE" : "PAUSED";
            if (_navModeBtn != null && controller != null)
            {
                bool ik = controller.mode == ArmController.Mode.IK;
                _navModeBtn.text = ik ? "◀ IK ▶" : "◀ MANUAL ▶";
                UiTheme.SetActive(_navModeBtn, true, ik ? UiTheme.Teal : UiTheme.Orange);
            }
        }

        // ── views are in UiManager.Views.cs (partial) ──────────────────────────────────────────────────
    }
}
