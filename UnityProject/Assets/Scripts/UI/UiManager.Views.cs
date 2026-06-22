using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArmSmith.UI
{
    /// <summary>The view builders for UiManager (Menu, Dashboard, Training, Options, Help). Split into a
    /// partial so the orchestration core stays readable. Each Build*View() fills _content and sets _refresh
    /// to a per-frame updater that keeps the live readouts current.</summary>
    public partial class UiManager
    {
        // helper: a two/three-column responsive row of panels
        VisualElement Columns(params VisualElement[] cols)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            foreach (var c in cols) { c.style.flexGrow = 1; c.style.marginRight = 8; row.Add(c); }
            return row;
        }

        VisualElement ScrollPanel(VisualElement accentHeader, out VisualElement body)
        {
            var panel = UiTheme.Panel();
            panel.Add(accentHeader);
            var sv = new ScrollView();
            sv.style.flexGrow = 1;
            UiTheme.Pad(sv, 10);
            panel.Add(sv);
            body = sv.contentContainer;
            return panel;
        }

        // ── LIVE-VIEWPORT layout: a fixed-width control column + a TRANSPARENT region where the 3D scene
        // (MainCam renders full-screen behind the UI) shows through. Lets the user see the arm while editing.
        // A thin framed label marks the live area so it reads as a viewport, not empty space.

        /// <summary>A transparent "window" onto the live 3D scene, with a corner caption + framed border.</summary>
        VisualElement LiveViewport(string caption, params VisualElement[] overlayTopRight)
        {
            var vp = new VisualElement();
            vp.style.flexGrow = 1; vp.style.marginLeft = 8; vp.style.marginBottom = 8;
            vp.style.backgroundColor = new Color(0, 0, 0, 0);   // transparent -> 3D scene shows through
            UiTheme.SetBorder(vp, UiTheme.BorderHi, 1); UiTheme.SetRadius(vp, 8);
            // top bar with caption + optional controls, semi-transparent so the scene stays visible
            var bar = UiTheme.Row(); bar.style.justifyContent = Justify.SpaceBetween; bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = new Color(UiTheme.Card2.r, UiTheme.Card2.g, UiTheme.Card2.b, 0.72f);
            bar.style.paddingLeft = 10; bar.style.paddingRight = 8; bar.style.paddingTop = 5; bar.style.paddingBottom = 5;
            bar.style.borderTopLeftRadius = 8; bar.style.borderTopRightRadius = 8;
            var lblRow = UiTheme.Row();
            var dot = new Label("●"); dot.style.color = UiTheme.Green; dot.style.fontSize = 9; dot.style.marginRight = 5; lblRow.Add(dot);
            lblRow.Add(UiTheme.Caption(caption));
            bar.Add(lblRow);
            if (overlayTopRight != null && overlayTopRight.Length > 0)
            { var r = UiTheme.Row(); foreach (var e in overlayTopRight) r.Add(e); bar.Add(r); }
            vp.Add(bar);
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; vp.Add(spacer);   // the see-through area
            return vp;
        }

        /// <summary>Standard editing layout: a fixed-width scrollable control column on the left and a live
        /// 3D viewport filling the rest. Returns the control body (add your panels there).</summary>
        VisualElement EditLayout(string panelTitle, Color accent, string badge, float colWidth,
                                 string viewportCaption, out VisualElement body, params VisualElement[] vpOverlay)
        {
            var wrap = new VisualElement(); wrap.style.flexDirection = FlexDirection.Row; wrap.style.flexGrow = 1;
            var col = ScrollPanel(UiTheme.PanelHeader(panelTitle, accent, badge), out body);
            col.style.width = colWidth; col.style.flexShrink = 0;
            wrap.Add(col);
            wrap.Add(LiveViewport(viewportCaption, vpOverlay));
            _content.Add(wrap);
            return body;
        }

        // ════════════════════════════ MENU VIEW ════════════════════════════
        // Three balanced columns that FILL the screen: [brand + navigation] · [scenario grid] · [live preview
        // + quick-start]. No more one-card-orphan second row — the grid wraps evenly and the right column
        // uses the previously-empty half for a live 3D preview of the current task.
        void BuildMenuView()
        {
            var wrap = new VisualElement(); wrap.style.flexDirection = FlexDirection.Row; wrap.style.flexGrow = 1;

            // ── LEFT: brand + navigation ──
            var left = UiTheme.Panel(); left.style.width = 290; left.style.flexShrink = 0; left.style.marginRight = 8;
            var lb = new VisualElement(); UiTheme.Pad(lb, 18); left.Add(lb);
            lb.Add(UiTheme.Caption("Robotic Arm Design & Evolution"));
            var title = UiTheme.Row(); title.style.marginTop = 4; title.style.marginBottom = 2;
            var t1 = new Label("ARM"); t1.style.color = UiTheme.Teal; t1.style.fontSize = 40; t1.style.unityFontStyleAndWeight = FontStyle.Bold; t1.style.letterSpacing = 3f;
            var t2 = new Label("SMITH"); t2.style.color = UiTheme.Orange; t2.style.fontSize = 40; t2.style.unityFontStyleAndWeight = FontStyle.Bold; t2.style.letterSpacing = 3f;
            title.Add(t1); title.Add(t2); lb.Add(title);
            lb.Add(UiTheme.Lbl("Design · Control · Evolve · Export", UiTheme.Muted, 12));
            lb.Add(UiTheme.Lbl("v0.9 · Unity 6000.4 · URP · ArticulationBody", UiTheme.TextDim, 10));

            lb.Add(UiTheme.SectionHead("Workspace"));
            lb.Add(NavBtn("▶  Control / Drive", View.Dashboard, UiTheme.Green));
            lb.Add(NavBtn("✎  Build Arm", View.Build, UiTheme.Teal));
            lb.Add(NavBtn("⊕  Modules", View.Modules, UiTheme.Teal));
            lb.Add(NavBtn("⚙  Training", View.Training, UiTheme.Orange));

            lb.Add(UiTheme.SectionHead("Library & Setup"));
            lb.Add(NavBtn("◫  Catalogue", View.Catalogue, UiTheme.Teal));
            lb.Add(NavBtn("⚙  Options & Calibration", View.Options, UiTheme.Teal));
            lb.Add(NavBtn("?  Help & Controls", View.Help, UiTheme.Muted));

            // ── CENTER: scenario grid (even wrap, fills space) ──
            VisualElement gridBody;
            var center = ScrollPanel(UiTheme.PanelHeader("Scenario Select", UiTheme.Teal, "7 tasks"), out gridBody);
            center.style.flexGrow = 1; center.style.marginRight = 8;
            gridBody.Add(UiTheme.Caption("Choose a manipulation task — click Launch to load it live"));
            var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.FlexStart; gridBody.Add(grid);
            foreach (ScenarioType st in Enum.GetValues(typeof(ScenarioType)))
                grid.Add(ScenarioCard(st));

            // ── RIGHT: live preview of the current task + quick-start ──
            var right = new VisualElement(); right.style.width = 320; right.style.flexShrink = 0;
            right.style.flexDirection = FlexDirection.Column;
            // a live 3D viewport (transparent → shows the arm in the scene) takes the top
            var vp = LiveViewport("LIVE WORKSPACE");
            vp.style.flexGrow = 1; vp.style.marginLeft = 0; vp.style.marginBottom = 8;
            right.Add(vp);
            // quick-start panel beneath it
            VisualElement qsBody;
            var qs = ScrollPanel(UiTheme.PanelHeader("Quick Start", UiTheme.Green), out qsBody);
            qs.style.flexShrink = 0;
            _menuTaskLbl = UiTheme.Lbl("", UiTheme.TextHi, 12); _menuTaskLbl.style.unityFontStyleAndWeight = FontStyle.Bold; qsBody.Add(_menuTaskLbl);
            _menuTaskBlurb = UiTheme.Lbl("", UiTheme.Muted, 11); _menuTaskBlurb.style.whiteSpace = WhiteSpace.Normal; qsBody.Add(_menuTaskBlurb);
            var qrow = UiTheme.Row(); qrow.style.marginTop = 6;
            qrow.Add(UiTheme.BtnPrimary("▶  Drive It", () => SwitchTo(View.Dashboard), UiTheme.Green));
            qrow.Add(UiTheme.Btn("⚙ Train It", () => SwitchTo(View.Training), UiTheme.Orange));
            qsBody.Add(qrow);
            qsBody.Add(UiTheme.Btn("⟳  Auto-solve current task", () => { if (agent != null) agent.AutoSort(); }, UiTheme.Teal));
            right.Add(qs);

            wrap.Add(left); wrap.Add(center); wrap.Add(right);
            _content.Add(wrap);
            _refresh = RefreshMenu;
        }

        Label _menuTaskLbl, _menuTaskBlurb;
        void RefreshMenu()
        {
            if (scenarios == null) return;
            if (_menuTaskLbl != null) _menuTaskLbl.text = "Current: " + scenarios.current;
            if (_menuTaskBlurb != null) _menuTaskBlurb.text = ScenarioBlurb(scenarios.current);
        }

        Button NavBtn(string text, View v, Color c)
        {
            var b = UiTheme.Btn(text, () => SwitchTo(v), c);
            b.style.width = Length.Percent(100); b.style.unityTextAlign = TextAnchor.MiddleLeft;
            return b;
        }

        VisualElement ScenarioCard(ScenarioType st)
        {
            int diff = ScenarioDifficulty(st);
            Color accent = diff == 3 ? UiTheme.Orange : (diff == 1 ? UiTheme.Green : UiTheme.Teal);
            var card = UiTheme.CardEl(accent, 226);
            var name = UiTheme.Lbl(st.ToString(), UiTheme.TextHi, 14); name.style.unityFontStyleAndWeight = FontStyle.Bold; card.Add(name);
            var blurb = UiTheme.Lbl(ScenarioBlurb(st), UiTheme.Muted, 11); blurb.style.whiteSpace = WhiteSpace.Normal; blurb.style.marginTop = 3; blurb.style.marginBottom = 6; blurb.style.minHeight = 44; card.Add(blurb);
            var dots = UiTheme.Row(); dots.style.marginBottom = 8;
            for (int i = 0; i < 3; i++) { var d = new Label("●"); d.style.fontSize = 10; d.style.marginRight = 2; d.style.color = i < diff ? accent : UiTheme.TextDim; dots.Add(d); }
            var dl = UiTheme.Lbl(diff == 1 ? " easy" : diff == 2 ? " medium" : " hard", UiTheme.Muted, 9); dl.style.marginLeft = 4; dots.Add(dl);
            card.Add(dots);
            var btn = UiTheme.BtnPrimary("▶  Launch", () => { if (scenarios != null) scenarios.LoadScenario(st); SwitchTo(View.Dashboard); }, accent);
            btn.style.width = Length.Percent(100); card.Add(btn);
            return card;
        }

        static string ScenarioBlurb(ScenarioType st)
        {
            switch (st)
            {
                case ScenarioType.ReachTouch:    return "Move the gripper tip to the pink target (<4cm).";
                case ScenarioType.PushToZone:    return "Push the cube onto the blue pad.";
                case ScenarioType.PickPlaceCube: return "Grasp the cube and set it on the pad.";
                case ScenarioType.TrayToTray:    return "Lift the cube from Tray A into Tray B.";
                case ScenarioType.StackTwo:      return "Stack the cube on top of cube B.";
                case ScenarioType.DropInBin:     return "Carry the cube and drop it in the bin.";
                case ScenarioType.SortIntoTray:  return "Sort all scattered cubes into the tray.";
                default: return "";
            }
        }
        static int ScenarioDifficulty(ScenarioType st)
        {
            switch (st)
            {
                case ScenarioType.ReachTouch: return 1;
                case ScenarioType.PushToZone: case ScenarioType.PickPlaceCube: case ScenarioType.DropInBin: return 2;
                default: return 3;
            }
        }

        // ════════════════════════════ DASHBOARD VIEW ════════════════════════════
        Label _dbObjective, _dbReward, _dbSuccess, _dbEE, _dbGrip, _dbExportStatus;
        VisualElement _dbJointHost, _dbRewardBar, _dbContactHost, _dbTrainBanner, _kbHost, _dbCamFeeds;
        Button _dbModeBtn, _dbGripBtn, _dbMouseBtn;
        int _dbCamCount = -1;

        void RebuildCamFeeds()
        {
            if (_dbCamFeeds == null) return;
            _dbCamFeeds.Clear();
            int cams = 0;
            if (attachments != null)
            {
                var row = UiTheme.Row(); row.style.flexWrap = Wrap.Wrap;
                foreach (var pp in attachments.placed)
                {
                    if (pp.def == null || pp.def.kind != ArmSmith.Modules.PartKind.Camera || pp.rt == null) continue;
                    var col = UiTheme.Col(); col.style.marginRight = 8; col.style.marginBottom = 6;
                    var img = new Image { image = pp.rt }; img.style.width = 120; img.style.height = 120;
                    UiTheme.SetBorder(img, UiTheme.BorderHi, 1); UiTheme.SetRadius(img, 4);
                    col.Add(img); col.Add(UiTheme.Caption(pp.def.name));
                    row.Add(col); cams++;
                }
                _dbCamFeeds.Add(row);
            }
            if (cams == 0) _dbCamFeeds.Add(UiTheme.Caption("No cameras mounted — add one in Build / Modules (⊕ 3D Part)."));
            _dbCamCount = cams;
        }

        // key-rebind capture state: when set, the next key pressed becomes this action's binding
        KeyBindings.Action? _kbCapturing;
        Button _kbCapturingBtn;

        void RebuildKeyBindings()
        {
            if (_kbHost == null) return;
            _kbHost.Clear();
            foreach (var a in KeyBindings.All)
            {
                var act = a;
                var row = UiTheme.Row(); row.style.justifyContent = Justify.SpaceBetween; row.style.marginTop = 2; row.style.marginBottom = 2;
                row.Add(UiTheme.Caption(KeyBindings.Label(act)));
                var btn = UiTheme.Btn(KeyBindings.Get(act).ToString(), null, UiTheme.Teal);
                btn.style.minWidth = 96;
                btn.clicked += () => {
                    _kbCapturing = act; _kbCapturingBtn = btn;
                    btn.text = "PRESS KEY…"; UiTheme.SetActive(btn, true, UiTheme.Orange);
                };
                row.Add(btn);
                _kbHost.Add(row);
            }
        }

        /// <summary>Called from UiManager.Update while a binding capture is active.</summary>
        void PollKeyRebind()
        {
            if (_kbCapturing == null) return;
            foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.Mouse0 || kc == KeyCode.Mouse1 || kc == KeyCode.Mouse2) continue;
                if (Input.GetKeyDown(kc))
                {
                    if (kc != KeyCode.Escape) KeyBindings.Set(_kbCapturing.Value, kc);
                    _kbCapturing = null; _kbCapturingBtn = null;
                    RebuildKeyBindings();
                    break;
                }
            }
        }

        void ExportStl()
        {
            if (arm == null || arm.baseBody == null) { if (_dbExportStatus != null) _dbExportStatus.text = "no arm to export"; return; }
            try
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "Exports");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, $"armsmith_{System.DateTime.Now:yyyyMMdd_HHmmss}.stl");
                StlExporter.ExportHierarchy(arm.baseBody.transform, path);
                if (_dbExportStatus != null) _dbExportStatus.text = "saved " + System.IO.Path.GetFileName(path);
            }
            catch (System.Exception e) { if (_dbExportStatus != null) _dbExportStatus.text = "export failed: " + e.Message; }
        }

        void BuildDashboardView()
        {
            // LEFT: Driver/Teleop · CENTER: Joint telemetry · RIGHT: Task + Export quick
            VisualElement driverBody, jointBody, taskBody;
            var driver = ScrollPanel(UiTheme.PanelHeader("Driver / Teleop", UiTheme.Orange, "SIM LIVE", UiTheme.Green), out driverBody);
            var joints = ScrollPanel(UiTheme.PanelHeader("Joint Telemetry", UiTheme.Teal), out jointBody);
            var task   = ScrollPanel(UiTheme.PanelHeader("Task & Export", UiTheme.Green), out taskBody);

            // -- Driver --
            // training-status banner: when the trainer is driving the arm, tell the user + offer a clean
            // "take manual control" action (stops training + switches to Manual) so there's no silent fight.
            _dbTrainBanner = new VisualElement(); driverBody.Add(_dbTrainBanner);

            driverBody.Add(UiTheme.SectionHead("Control Mode"));
            var modeRow = UiTheme.Row();
            _dbModeBtn = UiTheme.Btn("IK Mode", ToggleMode, UiTheme.Teal);
            modeRow.Add(_dbModeBtn);
            _dbMouseBtn = UiTheme.Btn("Mouse-follow (M)", () => { if (controller != null) controller.mouseFollow = !controller.mouseFollow; });
            modeRow.Add(_dbMouseBtn);
            driverBody.Add(modeRow);

            driverBody.Add(UiTheme.SectionHead("Gripper"));
            _dbGripBtn = UiTheme.Btn("Close ✊ (Space)", ToggleGrip, UiTheme.Orange);
            driverBody.Add(_dbGripBtn);
            driverBody.Add(UiTheme.StatRow("EE POSITION", "—", out _dbEE));
            driverBody.Add(UiTheme.StatRow("GRIP", "—", out _dbGrip));
            // grasp/contact state chip (Foxglove contact-panel pattern)
            _dbContactHost = UiTheme.Row(); driverBody.Add(_dbContactHost);

            driverBody.Add(UiTheme.SectionHead("Demonstration & Solve"));
            var recRow = UiTheme.Row();
            recRow.Add(UiTheme.Btn("⏺ Record demo", () => { if (recorder != null) recorder.StartRecording(); }, UiTheme.Red));
            recRow.Add(UiTheme.Btn("▶ Auto-solve", () => { if (agent != null) agent.AutoSort(); }, UiTheme.Green));
            driverBody.Add(recRow);
            driverBody.Add(UiTheme.Caption("Record a hand-driven demo → seed training (LeRobot workflow)."));

            // ── KEY BINDINGS (remappable) ──
            driverBody.Add(UiTheme.SectionHead("Key Bindings"));
            driverBody.Add(UiTheme.Caption("Click a binding, then press a new key to remap it."));
            _kbHost = new VisualElement(); driverBody.Add(_kbHost);
            RebuildKeyBindings();
            driverBody.Add(UiTheme.Btn("Reset bindings to default", () => { KeyBindings.ResetToDefaults(); RebuildKeyBindings(); }, UiTheme.Muted));

            // -- Joint telemetry (live rows) --
            _dbJointHost = new VisualElement();
            jointBody.Add(_dbJointHost);
            RebuildJointRows();

            // -- Task & Export --
            taskBody.Add(UiTheme.SectionHead("Objective"));
            _dbObjective = UiTheme.Lbl("—", UiTheme.Text, 11); _dbObjective.style.whiteSpace = WhiteSpace.Normal; taskBody.Add(_dbObjective);
            taskBody.Add(UiTheme.StatRow("REWARD", "—", out _dbReward, UiTheme.Orange));
            taskBody.Add(UiTheme.StatRow("SUCCESS", "—", out _dbSuccess, UiTheme.Green));
            _dbRewardBar = UiTheme.ProgressBar(0f, UiTheme.Green, 10); taskBody.Add(_dbRewardBar);

            taskBody.Add(UiTheme.SectionHead("Quick Export"));
            var exRow = UiTheme.Row();
            exRow.Add(UiTheme.Btn("▼ STL", ExportStl, UiTheme.Green));
            exRow.Add(UiTheme.Btn("▼ Waypoints", () => { if (recorder != null) recorder.StartRecording(); }, UiTheme.Orange));
            taskBody.Add(exRow);
            _dbExportStatus = UiTheme.Lbl("", UiTheme.Green, 10); _dbExportStatus.style.whiteSpace = WhiteSpace.Normal; taskBody.Add(_dbExportStatus);
            taskBody.Add(UiTheme.Caption("Safety: joint-limits ✓ · torque ✓ · self-collision ✓"));

            // live camera feeds from mounted camera parts (see what the robot sees while driving)
            taskBody.Add(UiTheme.SectionHead("Camera Feeds"));
            _dbCamFeeds = new VisualElement(); taskBody.Add(_dbCamFeeds);
            RebuildCamFeeds();

            var cols = Columns(driver, joints, task);
            cols.style.flexGrow = 1;
            _content.Add(cols);

            _refresh = RefreshDashboard;
        }

        void RebuildJointRows()
        {
            _dbJointHost.Clear();
            if (arm == null) return;
            int n = arm.jointSpecs.Count;
            for (int i = 0; i < n; i++)
            {
                // colour swatch (joint identity) + name, then a GAUGE of angle-within-limit (RViz JointState pattern)
                var line = UiTheme.Col(); line.style.marginTop = 3;
                var head = UiTheme.Row();
                var swatch = new VisualElement(); swatch.style.width = 10; swatch.style.height = 10; swatch.style.marginRight = 6;
                swatch.style.backgroundColor = UiTheme.JointColors[i % UiTheme.JointColors.Length]; UiTheme.SetRadius(swatch, 2);
                head.Add(swatch); head.Add(UiTheme.Caption($"J{i} {arm.jointSpecs[i].name}"));
                line.Add(head);
                var gauge = UiTheme.Gauge("angle", 0.5f, "0.0°", out _, 0.5f); gauge.name = $"jgauge{i}";
                line.Add(gauge);
                _dbJointHost.Add(line);
            }
        }

        void RefreshDashboard()
        {
            if (arm != null)
            {
                if (_dbJointHost.childCount != arm.jointSpecs.Count) RebuildJointRows();
                var ang = arm.GetJointAngles();
                for (int i = 0; i < arm.jointSpecs.Count; i++)
                {
                    var g = _dbJointHost.Q<VisualElement>($"jgauge{i}");
                    if (g != null)
                    {
                        var js = arm.jointSpecs[i];
                        float frac = Mathf.InverseLerp(js.minAngle, js.maxAngle, ang[i]);
                        // colour by how close to a joint limit (amber/red near the ends)
                        float edge = Mathf.Max(frac, 1f - frac);   // 0.5 = centre, ->1 near a limit
                        float pctOfLimit = Mathf.InverseLerp(0.5f, 1f, edge);
                        UiTheme.SetGauge(g, frac, $"{ang[i]:F1}°", pctOfLimit);
                    }
                }
            }
            if (_dbModeBtn != null && controller != null)
            {
                bool ik = controller.mode == ArmController.Mode.IK;
                _dbModeBtn.text = ik ? "IK Mode" : "Manual Mode";
                UiTheme.SetActive(_dbModeBtn, ik, ik ? UiTheme.Teal : UiTheme.Orange);
            }
            if (_dbMouseBtn != null && controller != null)
                UiTheme.SetActive(_dbMouseBtn, controller.mouseFollow, UiTheme.Teal);
            // training banner: show when the trainer is actively driving the arm
            if (_dbTrainBanner != null)
            {
                bool training = trainer != null && trainer.Running;
                if (training && _dbTrainBanner.childCount == 0)
                {
                    var banner = UiTheme.CardEl(UiTheme.Orange);
                    banner.style.marginRight = 0;
                    banner.Add(UiTheme.Lbl("⚙ TRAINING IS DRIVING THE ARM", UiTheme.Orange, 11));
                    var sub = UiTheme.Lbl("The evolution trainer controls the arm during a run. Take over to drive manually.", UiTheme.Muted, 10);
                    sub.style.whiteSpace = WhiteSpace.Normal; banner.Add(sub);
                    var take = UiTheme.BtnPrimary("✋  Take Manual Control", () => {
                        if (trainer != null) trainer.StopTraining();
                        if (controller != null) controller.mode = ArmController.Mode.Manual;
                    }, UiTheme.Orange);
                    take.style.width = Length.Percent(100); take.style.marginTop = 6; banner.Add(take);
                    _dbTrainBanner.Add(banner);
                }
                else if (!training && _dbTrainBanner.childCount > 0)
                {
                    _dbTrainBanner.Clear();
                }
            }
            if (_dbGripBtn != null && arm != null && arm.gripper != null)
            {
                bool closed = arm.gripper.closeAmount > 0.5f;
                _dbGripBtn.text = closed ? "Open ✋ (Space)" : "Close ✊ (Space)";
                UiTheme.SetActive(_dbGripBtn, closed, UiTheme.Orange);
            }
            if (_dbEE != null && arm != null)
            {
                Vector3 ee = arm.gripper != null ? arm.gripper.TipPosition : (arm.endEffector != null ? arm.endEffector.position : Vector3.zero);
                _dbEE.text = $"{ee.x:F2}, {ee.y:F2}, {ee.z:F2}";
            }
            if (_dbGrip != null && arm != null && arm.gripper != null)
                _dbGrip.text = arm.gripper.IsHolding ? $"HOLDING ({arm.gripper.closeAmount * 100f:F0}%)" : $"{arm.gripper.closeAmount * 100f:F0}% closed";
            // grasp/contact chip (semantic colour grammar)
            if (_dbContactHost != null && arm != null && arm.gripper != null)
            {
                _dbContactHost.Clear();
                bool holding = arm.gripper.IsHolding;
                bool closing = arm.gripper.closeAmount > 0.5f;
                if (holding) _dbContactHost.Add(UiTheme.StatusChip("GRASPED ✓", UiTheme.Green));
                else if (closing) _dbContactHost.Add(UiTheme.StatusChip("CLOSING…", UiTheme.Orange));
                else _dbContactHost.Add(UiTheme.StatusChip("OPEN", UiTheme.Muted));
            }
            if (scenarios != null)
            {
                if (_dbObjective != null) _dbObjective.text = scenarios.Objective();
                if (_dbReward != null) _dbReward.text = scenarios.LastReward.ToString("F2");
                if (_dbSuccess != null) { _dbSuccess.text = scenarios.SuccessNow ? "ACHIEVED ✓" : (scenarios.Succeeded ? "done" : "in progress"); _dbSuccess.style.color = scenarios.SuccessNow ? UiTheme.Green : UiTheme.Muted; }
                if (_dbRewardBar != null) UiTheme.SetProgress(_dbRewardBar, Mathf.InverseLerp(-2f, 14f, scenarios.LastReward));
            }
            // rebuild camera-feed thumbnails only when the mounted-camera count changes
            if (_dbCamFeeds != null && attachments != null)
            {
                int cams = 0; foreach (var pp in attachments.placed) if (pp.def != null && pp.def.kind == ArmSmith.Modules.PartKind.Camera && pp.rt != null) cams++;
                if (cams != _dbCamCount) RebuildCamFeeds();
            }
        }

        void ToggleMode()
        {
            if (controller == null) return;
            controller.mode = controller.mode == ArmController.Mode.IK ? ArmController.Mode.Manual : ArmController.Mode.IK;
        }
        void ToggleGrip()
        {
            if (arm == null || arm.gripper == null) return;
            arm.gripper.SetClose(arm.gripper.closeAmount > 0.5f ? 0f : 1f);
        }

        // ════════════════════════════ MODULES VIEW (loadout + add menu) ════════════════════════════
        // Onshape/Fusion "parts catalog + mounted browser" + game loadout pattern.
        struct ModuleDef { public string name; public string type; public string spec; public int channels; public Color accent; }
        static readonly ModuleDef[] kModuleCatalog = {
            new ModuleDef{ name="Motor Encoders", type="MotorEncoders", spec="joint angles ×6 · baseline proprioception", channels=6, accent=UiTheme.Teal },
            new ModuleDef{ name="Task State",     type="TaskState",     spec="EE pose + gripper + vel + vector-to-target", channels=16, accent=UiTheme.Teal },
            new ModuleDef{ name="IMU",            type="IMU",           spec="orientation + gyro + accel (9 ch)", channels=9, accent=UiTheme.Orange },
            new ModuleDef{ name="Range Finder",   type="RangeFinder",   spec="1-pt ToF from gripper (1 ch)", channels=1, accent=UiTheme.Orange },
            new ModuleDef{ name="Lidar 2D",       type="Lidar2D",       spec="planar fan scan (16 ch)", channels=16, accent=UiTheme.Orange },
            new ModuleDef{ name="Depth Camera",   type="DepthCamera",   spec="wrist-cam depth patch (1 ch)", channels=1, accent=UiTheme.Orange },
            new ModuleDef{ name="EFlesh Tactile", type="EFleshTactile", spec="per-finger contact force (1 ch)", channels=1, accent=UiTheme.Green },
        };

        Label _modBudget;

        void BuildModulesView()
        {
            VisualElement body;
            EditLayout("Modules", UiTheme.Orange, "sensors & end-effectors", 420,
                "LIVE ARM — mounted modules show on the 3D frame", out body);

            // budget readout (mass / channels / power) — gamifies the "more sensors = heavier/slower" trade-off
            _modBudget = UiTheme.Lbl("", UiTheme.TextHi, 12); _modBudget.style.whiteSpace = WhiteSpace.Normal;
            _modBudget.style.unityFontStyleAndWeight = FontStyle.Bold;
            body.Add(_modBudget);
            _modBudgetBars = new VisualElement(); body.Add(_modBudgetBars);

            body.Add(UiTheme.SectionHead("Mounted Loadout"));
            if (sensorHub != null)
            {
                foreach (var s in sensorHub.Sensors)
                {
                    var sensor = s;
                    var row = UiTheme.Row(); row.style.justifyContent = Justify.SpaceBetween;
                    row.style.borderTopColor = UiTheme.Border; row.style.borderTopWidth = 1; row.style.paddingTop = 4; row.style.paddingBottom = 4;
                    var left = UiTheme.Row();
                    var dot = new Label("●"); dot.style.fontSize = 10; dot.style.marginRight = 5; dot.style.color = sensor.Enabled ? UiTheme.Green : UiTheme.TextDim; left.Add(dot);
                    left.Add(UiTheme.Lbl(sensor.Name, sensor.Enabled ? UiTheme.TextHi : UiTheme.Muted, 11));
                    row.Add(left);
                    var right = UiTheme.Row();
                    right.Add(UiTheme.Lbl(sensor.Channels.Length + " ch", UiTheme.Muted, 10));
                    var eye = UiTheme.Btn(sensor.Enabled ? "ON" : "OFF", null, sensor.Enabled ? UiTheme.Green : UiTheme.Muted);
                    eye.style.minWidth = 50; eye.style.marginLeft = 8;
                    eye.clicked += () => { sensor.Enabled = !sensor.Enabled; SyncMaskFromHub(); SwitchTo(View.Modules); };
                    right.Add(eye);
                    row.Add(right);
                    body.Add(row);
                }
            }

            // catalog cards (with a clear PERFORMANCE explanation per module)
            body.Add(UiTheme.SectionHead("Add a Module"));
            foreach (var m in kModuleCatalog)
            {
                var md = m;
                bool mounted = sensorHub != null && sensorHub.Get(md.type) != null && sensorHub.Get(md.type).Enabled;
                var card = UiTheme.CardEl(md.accent); card.style.marginRight = 0;
                var t = UiTheme.Row(); t.style.justifyContent = Justify.SpaceBetween;
                var tl = UiTheme.Row();
                var nm = UiTheme.Lbl(md.name, UiTheme.TextHi, 13); nm.style.unityFontStyleAndWeight = FontStyle.Bold; tl.Add(nm);
                tl.Add(UiTheme.Badge(md.channels + " ch", md.accent));
                t.Add(tl);
                if (mounted) t.Add(UiTheme.Badge("MOUNTED", UiTheme.Green));
                card.Add(t);
                card.Add(UiTheme.Lbl(md.spec, UiTheme.Muted, 10));
                // PERFORMANCE: what it costs + what it helps (clearly explained + shown as a bar)
                card.Add(ModulePerfRow("obs cost", md.channels / 16f, UiTheme.Orange, md.channels + " channels added to the policy input"));
                card.Add(ModulePerfRow("grasp benefit", ModuleBenefit(md.type), UiTheme.Green, ModuleBenefitText(md.type)));
                var act = UiTheme.Row(); act.style.marginTop = 6;
                act.Add(UiTheme.BtnPrimary(mounted ? "✓ Mounted" : "⊕ Mount", () => {
                    if (sensorHub != null) { sensorHub.SetEnabled(md.type, true); SyncMaskFromHub(); }
                    SwitchTo(View.Modules);
                }, mounted ? UiTheme.Muted : UiTheme.Green));
                if (mounted) act.Add(UiTheme.Btn("Remove", () => {
                    if (sensorHub != null) { sensorHub.SetEnabled(md.type, false); SyncMaskFromHub(); }
                    SwitchTo(View.Modules);
                }, UiTheme.Red));
                // KSP-style: also place a real 3D part on the arm for this sensor (cameras/range/lidar/imu/tactile)
                string partId = SensorToPartId(md.type);
                if (partId != null && attachments != null)
                    act.Add(UiTheme.Btn("⊕ 3D Part", () => {
                        int wrist = arm != null ? Mathf.Max(0, arm.jointSpecs.Count - 2) : 0;
                        attachments.Place(partId, wrist, new Vector3(0f, 0.04f, 0.03f), Vector3.zero, 1.2f);
                        if (saveSystem != null) saveSystem.AutoSaveConditions();
                        SwitchTo(View.Build);   // jump to the build bench to adjust it
                    }, UiTheme.Teal));
                card.Add(act);
                body.Add(card);
            }

            _refresh = RefreshModules;
        }

        VisualElement _modBudgetBars;

        // map a SensorHub module type to its KSP attachment part id (null = no 3D part for this one)
        static string SensorToPartId(string sensorType)
        {
            switch (sensorType)
            {
                case "DepthCamera":   return "cam_wrist";
                case "RangeFinder":   return "range";
                case "Lidar2D":       return "lidar";
                case "IMU":           return "imu";
                case "EFleshTactile": return "tactile";
                default:              return null;   // MotorEncoders/TaskState are intrinsic, no physical part
            }
        }

        static float ModuleBenefit(string type)
        {
            switch (type) { case "EFleshTactile": return 0.9f; case "RangeFinder": return 0.6f; case "DepthCamera": return 0.7f;
                case "MotorEncoders": return 0.8f; case "TaskState": return 0.85f; case "IMU": return 0.4f; case "Lidar2D": return 0.5f; default: return 0.5f; }
        }
        static string ModuleBenefitText(string type)
        {
            switch (type) {
                case "EFleshTactile": return "knows when fingers touch — best for reliable grasps";
                case "RangeFinder":   return "distance-to-object helps approach & descent timing";
                case "DepthCamera":   return "shape/where of nearby objects (vision policies)";
                case "MotorEncoders": return "baseline proprioception — almost always needed";
                case "TaskState":     return "EE pose + target vector — strong for placing";
                case "IMU":           return "orientation/accel — useful for dynamic motions";
                case "Lidar2D":       return "planar scan of the workspace surroundings";
                default: return ""; }
        }
        VisualElement ModulePerfRow(string label, float frac01, Color color, string explain)
        {
            var col = UiTheme.Col(); col.style.marginTop = 3;
            var g = UiTheme.Gauge(label, Mathf.Clamp01(frac01), "", out _, frac01); col.Add(g);
            var e = UiTheme.Lbl(explain, UiTheme.Muted, 9); e.style.whiteSpace = WhiteSpace.Normal; col.Add(e);
            return col;
        }

        void SyncMaskFromHub()
        {
            // mirror the hub's enabled state into the TrainingConfig mask so training uses it
            if (trainer == null || sensorHub == null) return;
            var c = trainer.config;
            c.useMotorEncoders = sensorHub.Get("MotorEncoders")?.Enabled ?? c.useMotorEncoders;
            c.useTaskState = sensorHub.Get("TaskState")?.Enabled ?? c.useTaskState;
            c.useImu = sensorHub.Get("IMU")?.Enabled ?? c.useImu;
            c.useRangeFinder = sensorHub.Get("RangeFinder")?.Enabled ?? c.useRangeFinder;
            c.useLidar = sensorHub.Get("Lidar2D")?.Enabled ?? c.useLidar;
            c.useDepthCamera = sensorHub.Get("DepthCamera")?.Enabled ?? c.useDepthCamera;
            c.useTactile = sensorHub.Get("EFleshTactile")?.Enabled ?? c.useTactile;
            if (saveSystem != null) saveSystem.AutoSaveConditions();   // loadout changes persist immediately
        }

        void RefreshModules()
        {
            if (_modBudget == null || sensorHub == null) return;
            int active = 0, ch = 0;
            foreach (var s in sensorHub.Sensors) if (s.Enabled) { active++; ch += s.Channels.Length; }
            float massG = 20f * active, powerW = 0.4f * active;
            _modBudget.text = $"{active} modules · {ch} obs channels";
            if (_modBudgetBars != null)
            {
                _modBudgetBars.Clear();
                _modBudgetBars.Add(UiTheme.Gauge("obs dim", Mathf.Clamp01(ch / 80f), ch.ToString(), out _, ch / 80f));
                _modBudgetBars.Add(UiTheme.Gauge("mass", Mathf.Clamp01(massG / 200f), $"≈{massG:F0} g", out _, massG / 200f));
                _modBudgetBars.Add(UiTheme.Gauge("power", Mathf.Clamp01(powerW / 4f), $"≈{powerW:F1} W", out _, powerW / 4f));
            }
        }

        // ════════════════════════════ BUILD VIEW (joint editor + creations) ════════════════════════════
        // Fusion feature-tree (parametric chain) + generative-design outcome gallery patterns.
        Label _bldStats, _bldStatus;
        VisualElement _bldChain, _bldGallery;

        void BuildBuildView()
        {
            // Left edit column + LIVE 3D viewport (drive the arm while editing). Viewport top-right gets
            // quick drive controls so you can immediately move the arm as you change values.
            var driveBtn = UiTheme.Btn("✊ Grip", () => { if (arm != null && arm.gripper != null) arm.gripper.Toggle(); }, UiTheme.Orange);
            var modeBtn = UiTheme.Btn("Mode", ToggleModeGlobal, UiTheme.Teal);
            VisualElement chainBody;
            EditLayout("Build Arm", UiTheme.Teal, "parametric chain", 440,
                "LIVE ARM — moves as you edit · drive it directly", out chainBody, modeBtn, driveBtn);

            // arm stats (live)
            _bldStats = UiTheme.Lbl("", UiTheme.TextHi, 12); _bldStats.style.whiteSpace = WhiteSpace.Normal; _bldStats.style.unityFontStyleAndWeight = FontStyle.Bold; chainBody.Add(_bldStats);
            chainBody.Add(UiTheme.Caption("Edit joint limits live — the arm updates instantly. Drag a joint past a limit to feel it clamp."));

            chainBody.Add(UiTheme.SectionHead("Kinematic Chain"));
            _bldChain = new VisualElement(); chainBody.Add(_bldChain);
            RebuildChain();
            _bldStatus = UiTheme.Lbl("", UiTheme.Green, 10); _bldStatus.style.whiteSpace = WhiteSpace.Normal; chainBody.Add(_bldStatus);

            // ── KSP-style ATTACHMENTS (parts bin → mount on a link → adjust pose) ──
            chainBody.Add(UiTheme.SectionHead("Attachments — Parts Bin"));
            chainBody.Add(UiTheme.Caption("Pick a part, choose a link, Place it. Then adjust its pose live (KSP-style)."));
            BuildMountTargetRow(chainBody);
            var bin = new VisualElement(); bin.style.flexDirection = FlexDirection.Row; bin.style.flexWrap = Wrap.Wrap; chainBody.Add(bin);
            foreach (var def in ArmSmith.Modules.AttachmentSystem.Catalog)
                bin.Add(PartCard(def));

            chainBody.Add(UiTheme.SectionHead("Mounted Parts"));
            _bldAttached = new VisualElement(); chainBody.Add(_bldAttached);
            RebuildAttached();

            chainBody.Add(UiTheme.SectionHead("Creations Library"));
            chainBody.Add(UiTheme.Caption("Best-of-generation solutions — ▶ Replay runs one on the live arm."));
            _bldGallery = new VisualElement(); chainBody.Add(_bldGallery);
            RebuildGallery();

            _refresh = RefreshBuild;
        }

        void RebuildChain()
        {
            if (_bldChain == null || arm == null) return;
            _bldChain.Clear();
            for (int i = 0; i < arm.jointSpecs.Count; i++)
            {
                var js = arm.jointSpecs[i];
                int idx = i;
                var node = UiTheme.Panel(UiTheme.JointColors[i % UiTheme.JointColors.Length]);
                var nb = new VisualElement(); UiTheme.Pad(nb, 8); node.Add(nb);
                var head = UiTheme.Row();
                var sw = new VisualElement(); sw.style.width = 10; sw.style.height = 10; sw.style.marginRight = 6;
                sw.style.backgroundColor = UiTheme.JointColors[i % UiTheme.JointColors.Length]; UiTheme.SetRadius(sw, 2);
                head.Add(sw);
                var nm = UiTheme.Lbl($"J{i} · {js.name}", UiTheme.TextHi, 12); nm.style.unityFontStyleAndWeight = FontStyle.Bold; head.Add(nm);
                head.Add(UiTheme.Badge(js.axis.ToString(), UiTheme.Muted));
                nb.Add(head);
                // DRIVE slider — move this joint live (Manual mode). Tagged so RefreshBuild can sync it.
                float cur = idx < arm.GetJointAngles().Length ? arm.GetJointAngles()[idx] : 0f;
                var driveRow = UiTheme.SliderRow("drive °", js.minAngle, js.maxAngle, cur, out var driveS, out var driveL, "°", UiTheme.JointColors[i % UiTheme.JointColors.Length]);
                driveS.name = $"drive{idx}"; driveL.name = $"drivelbl{idx}";
                driveS.RegisterValueChangedCallback(e => {
                    if (controller == null) return;
                    controller.mode = ArmController.Mode.Manual;
                    var ang = new List<float>(arm.GetJointAngles());
                    while (ang.Count <= idx) ang.Add(0f);
                    ang[idx] = e.newValue; controller.SetTargets(ang);
                    driveL.text = e.newValue.ToString("0") + "°";
                });
                nb.Add(driveRow);
                // limit sliders (min/max angle) — live edit
                var minRow = UiTheme.SliderRow("min °", -180f, 0f, js.minAngle, out var minS, out var minL, "°");
                minS.RegisterValueChangedCallback(e => { arm.jointSpecs[idx].minAngle = e.newValue; minL.text = e.newValue.ToString("0") + "°"; });
                var maxRow = UiTheme.SliderRow("max °", 0f, 180f, js.maxAngle, out var maxS, out var maxL, "°");
                maxS.RegisterValueChangedCallback(e => { arm.jointSpecs[idx].maxAngle = e.newValue; maxL.text = e.newValue.ToString("0") + "°"; });
                nb.Add(minRow); nb.Add(maxRow);
                _bldChain.Add(node);
            }
        }

        void RebuildGallery()
        {
            if (_bldGallery == null) return;
            _bldGallery.Clear();
            var lib = EvolutionStore.LoadLibrary();
            if (lib == null || lib.creations.Count == 0)
            {
                _bldGallery.Add(UiTheme.Caption("No creations yet — train, then best-of-generation solutions appear here."));
                return;
            }
            // newest first
            int shown = 0;
            for (int i = lib.creations.Count - 1; i >= 0 && shown < 12; i--, shown++)
            {
                var c = lib.creations[i];
                var card = UiTheme.Panel(c.successRate > 0.5f ? UiTheme.Green : UiTheme.Orange);
                var cb = new VisualElement(); UiTheme.Pad(cb, 8); card.Add(cb);
                var t = UiTheme.Row();
                t.Add(UiTheme.Lbl($"Gen {c.generation} · {c.scenario}", UiTheme.TextHi, 11));
                t.Add(UiTheme.Badge(c.backend, UiTheme.Muted));
                cb.Add(t);
                cb.Add(UiTheme.Lbl($"fitness {c.fitness:F2} · success {c.successRate * 100f:F0}% · {c.timestamp}", UiTheme.Muted, 10));
                var captured = c;
                cb.Add(UiTheme.Btn("▶ Replay", () => {
                    if (trainer != null) { trainer.ReplayCreation(captured); if (_bldStatus != null) _bldStatus.text = $"replaying Gen {captured.generation} creation"; }
                }, UiTheme.Green));
                _bldGallery.Add(card);
            }
        }

        void RefreshBuild()
        {
            if (_bldStats != null && arm != null)
            {
                float reach = arm.config != null ? arm.config.TotalReach() : 0f;
                _bldStats.text = $"DOF {arm.jointSpecs.Count} · reach {reach:F2} m · gripper {(arm.gripper != null ? "ok" : "—")}";
            }
            // keep the drive sliders in sync with the live joint angles (so they track when the arm moves
            // by other means), but don't fight the slider the user is currently dragging.
            if (_bldChain != null && arm != null)
            {
                var ang = arm.GetJointAngles();
                for (int i = 0; i < arm.jointSpecs.Count && i < ang.Length; i++)
                {
                    var s = _bldChain.Q<Slider>($"drive{i}");
                    if (s != null && !s.HasMouseCapture() && Mathf.Abs(s.value - ang[i]) > 0.5f)
                    {
                        s.SetValueWithoutNotify(ang[i]);
                        var l = _bldChain.Q<Label>($"drivelbl{i}");
                        if (l != null) l.text = ang[i].ToString("0") + "°";
                    }
                }
            }
        }

        // ── KSP-style attachment UI ──────────────────────────────────────────────────────────────────
        VisualElement _bldAttached;
        int _mountTargetLink = -2;   // -2 = "auto (gripper)"; -1 = base; >=0 = jointBody index
        Label _mountTargetLbl;

        void BuildMountTargetRow(VisualElement host)
        {
            var row = UiTheme.Row(); row.style.marginTop = 4; row.style.marginBottom = 4;
            row.Add(UiTheme.Caption("Mount on"));
            _mountTargetLbl = UiTheme.Lbl(MountTargetName(), UiTheme.Teal, 11); _mountTargetLbl.style.width = 130; _mountTargetLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(UiTheme.Btn("◀", () => { CycleMountTarget(-1); }, UiTheme.Muted));
            row.Add(_mountTargetLbl);
            row.Add(UiTheme.Btn("▶", () => { CycleMountTarget(1); }, UiTheme.Muted));
            host.Add(row);
        }

        void CycleMountTarget(int dir)
        {
            int n = arm != null ? arm.jointSpecs.Count : 6;
            // order: auto(-2), base(-1), 0..n-1
            _mountTargetLink += dir;
            if (_mountTargetLink < -2) _mountTargetLink = n - 1;
            if (_mountTargetLink > n - 1) _mountTargetLink = -2;
            if (_mountTargetLbl != null) _mountTargetLbl.text = MountTargetName();
        }

        string MountTargetName()
        {
            if (_mountTargetLink == -2) return "Auto (gripper)";
            if (_mountTargetLink == -1) return "Base";
            if (arm != null && _mountTargetLink < arm.jointSpecs.Count) return $"J{_mountTargetLink} {arm.jointSpecs[_mountTargetLink].name}";
            return "Link " + _mountTargetLink;
        }

        int ResolveMountLink()
        {
            if (_mountTargetLink == -2) return arm != null ? Mathf.Max(0, arm.jointSpecs.Count - 2) : 0;   // wrist-ish
            return _mountTargetLink;
        }

        VisualElement PartCard(ArmSmith.Modules.PartDef def)
        {
            var card = UiTheme.CardEl(def.color, 200);
            var t = UiTheme.Row(); t.style.justifyContent = Justify.SpaceBetween;
            var nm = UiTheme.Lbl(def.name, UiTheme.TextHi, 12); nm.style.unityFontStyleAndWeight = FontStyle.Bold; t.Add(nm);
            t.Add(UiTheme.Badge(def.kind.ToString(), def.color));
            card.Add(t);
            var bl = UiTheme.Lbl(def.blurb, UiTheme.Muted, 10); bl.style.whiteSpace = WhiteSpace.Normal; bl.style.minHeight = 40; card.Add(bl);
            card.Add(UiTheme.Lbl($"mass ≈{def.massKg * 1000f:F0} g" + (def.sensorType != null ? " · sensor: " + def.sensorType : " · structural"), UiTheme.Muted, 9));
            var place = UiTheme.BtnPrimary("⊕  Place", () => {
                if (attachments == null) return;
                int link = ResolveMountLink();
                // sensible default local pose on the link surface
                var pp = attachments.Place(def.id, link, new Vector3(0f, 0.03f, 0.02f), Vector3.zero, 1f);
                if (saveSystem != null) saveSystem.AutoSaveConditions();
                RebuildAttached();
            }, def.color);
            place.style.width = Length.Percent(100); card.Add(place);
            return card;
        }

        void RebuildAttached()
        {
            if (_bldAttached == null) return;
            _bldAttached.Clear();
            if (attachments == null || attachments.placed.Count == 0)
            { _bldAttached.Add(UiTheme.Caption("No parts mounted yet — place one from the bin above.")); return; }

            foreach (var pp in attachments.placed)
            {
                var part = pp;
                var def = part.def;
                var card = UiTheme.CardEl(def != null ? def.color : UiTheme.Teal); card.style.marginRight = 0;
                var t = UiTheme.Row(); t.style.justifyContent = Justify.SpaceBetween;
                t.Add(UiTheme.Lbl(def != null ? def.name : part.defId, UiTheme.TextHi, 12));
                string linkName = part.linkIndex < 0 ? "base" : (arm != null && part.linkIndex < arm.jointSpecs.Count ? $"J{part.linkIndex}" : "link" + part.linkIndex);
                t.Add(UiTheme.Badge("on " + linkName, UiTheme.Muted));
                card.Add(t);

                // adjust pose — position (along link), rotate, scale
                var pos = part.localPos; var eul = part.localEuler; float sc = part.scale;
                AddAdjust(card, "fwd  (z)", -0.1f, 0.1f, pos.z, v => { pos.z = v; attachments.Move(part, pos, eul, sc); });
                AddAdjust(card, "up   (y)", -0.05f, 0.1f, pos.y, v => { pos.y = v; attachments.Move(part, pos, eul, sc); });
                AddAdjust(card, "side (x)", -0.06f, 0.06f, pos.x, v => { pos.x = v; attachments.Move(part, pos, eul, sc); });
                AddAdjust(card, "yaw °", -180f, 180f, eul.y, v => { eul.y = v; attachments.Move(part, pos, eul, sc); });
                AddAdjust(card, "pitch °", -180f, 180f, eul.x, v => { eul.x = v; attachments.Move(part, pos, eul, sc); });
                AddAdjust(card, "scale", 0.4f, 2.5f, sc, v => { sc = v; attachments.Move(part, pos, eul, sc); });

                var actions = UiTheme.Row();
                if (def != null && def.kind == ArmSmith.Modules.PartKind.Camera && part.rt != null)
                {
                    var feed = new Image { image = part.rt }; feed.style.width = 96; feed.style.height = 96; UiTheme.SetBorder(feed, UiTheme.BorderHi, 1); UiTheme.SetRadius(feed, 4); feed.style.marginRight = 8;
                    actions.Add(feed);
                }
                actions.Add(UiTheme.Btn("✕ Remove", () => { attachments.Remove(part); if (saveSystem != null) saveSystem.AutoSaveConditions(); RebuildAttached(); }, UiTheme.Red));
                card.Add(actions);
                _bldAttached.Add(card);
            }
        }

        void AddAdjust(VisualElement host, string label, float min, float max, float val, Action<float> set)
        {
            var row = UiTheme.SliderRow(label, min, max, val, out var s, out var l, "");
            s.RegisterValueChangedCallback(e => { set(e.newValue); l.text = e.newValue.ToString("0.###"); });
            host.Add(row);
        }

        // ════════════════════════════ CATALOGUE VIEW (J2/J3) ════════════════════════════
        Label _catStatus, _catSelName, _catSelInfo;
        string _catSelId;

        void BuildCatalogueView()
        {
            var wrap = new VisualElement(); wrap.style.flexDirection = FlexDirection.Row; wrap.style.flexGrow = 1;

            // ── LEFT: scrollable thumbnail list ──
            VisualElement listBody;
            var list = ScrollPanel(UiTheme.PanelHeader("Catalogue", UiTheme.Teal, "robots & models"), out listBody);
            list.style.width = 320; list.style.flexShrink = 0; list.style.marginRight = 8;

            listBody.Add(UiTheme.SectionHead("Robot Models"));
            var entries = ArmSmith.Catalogue.RobotCatalogue.Entries;
            if (string.IsNullOrEmpty(_catSelId) && entries.Count > 0) _catSelId = entries[0].id;
            foreach (var d in entries)
            {
                var dd = d;
                bool sel = dd.id == _catSelId;
                var thumb = UiTheme.CardEl(dd.hasMeshes ? UiTheme.Teal : UiTheme.Orange); thumb.style.marginRight = 0;
                if (sel) { thumb.style.backgroundColor = UiTheme.Surface; UiTheme.SetBorder(thumb, dd.hasMeshes ? UiTheme.Teal : UiTheme.Orange, 1); thumb.style.borderLeftWidth = 3; }
                var t = UiTheme.Row(); t.style.justifyContent = Justify.SpaceBetween;
                var nm = UiTheme.Lbl(dd.displayName, sel ? UiTheme.TextHi : UiTheme.Text, 12); nm.style.unityFontStyleAndWeight = FontStyle.Bold; t.Add(nm);
                t.Add(UiTheme.Badge($"{dd.dof}-DOF", dd.hasMeshes ? UiTheme.Teal : UiTheme.Orange));
                thumb.Add(t);
                thumb.Add(UiTheme.Lbl(dd.source, UiTheme.Muted, 9));
                thumb.RegisterCallback<PointerDownEvent>(_ => { _catSelId = dd.id; SwitchTo(View.Catalogue); });
                listBody.Add(thumb);
            }

            // saved creations as thumbnails too
            var lib = EvolutionStore.LoadLibrary();
            if (lib != null && lib.creations.Count > 0)
            {
                listBody.Add(UiTheme.SectionHead("Saved Training Models"));
                int shown = 0;
                for (int i = lib.creations.Count - 1; i >= 0 && shown < 10; i--, shown++)
                {
                    var c = lib.creations[i];
                    var thumb = UiTheme.CardEl(c.successRate > 0.5f ? UiTheme.Green : UiTheme.Orange); thumb.style.marginRight = 0;
                    var t = UiTheme.Row(); t.style.justifyContent = Justify.SpaceBetween;
                    t.Add(UiTheme.Lbl($"Gen {c.generation} · {c.scenario}", UiTheme.Text, 11));
                    t.Add(UiTheme.Badge($"{c.successRate * 100f:F0}%", c.successRate > 0.5f ? UiTheme.Green : UiTheme.Orange));
                    thumb.Add(t);
                    thumb.Add(UiTheme.Lbl($"{c.backend} · fitness {c.fitness:F1}", UiTheme.Muted, 9));
                    var cc = c;
                    thumb.RegisterCallback<PointerDownEvent>(_ => { if (trainer != null) trainer.ReplayCreation(cc); });
                    listBody.Add(thumb);
                }
            }

            // ── RIGHT: selected model — big live viewport + details ──
            var rightCol = new VisualElement(); rightCol.style.flexGrow = 1; rightCol.style.flexDirection = FlexDirection.Column;
            var vp = LiveViewport("SELECTED MODEL — live");
            vp.style.flexGrow = 1; vp.style.marginLeft = 0;
            rightCol.Add(vp);

            VisualElement detail;
            var det = ScrollPanel(UiTheme.PanelHeader("Model Details", UiTheme.Green), out detail);
            det.style.flexShrink = 0;
            _catSelName = UiTheme.Lbl("", UiTheme.TextHi, 14); _catSelName.style.unityFontStyleAndWeight = FontStyle.Bold; detail.Add(_catSelName);
            _catSelInfo = UiTheme.Lbl("", UiTheme.Muted, 11); _catSelInfo.style.whiteSpace = WhiteSpace.Normal; detail.Add(_catSelInfo);
            _catStatus = UiTheme.Lbl("", UiTheme.Green, 10); _catStatus.style.whiteSpace = WhiteSpace.Normal; detail.Add(_catStatus);
            var sel2 = ArmSmith.Catalogue.RobotCatalogue.Get(_catSelId);
            var actions = UiTheme.Row(); actions.style.marginTop = 6;
            actions.Add(UiTheme.BtnPrimary("⚡ Generate / Load", () => {
                string path = ArmSmith.Catalogue.RobotCatalogue.ResolveKinematicsPath(_catSelId);
                if (_catStatus != null) _catStatus.text = path != null ? $"Ready: {System.IO.Path.GetFileName(path)} (active arm swaps on scene reload)" : "Failed to resolve.";
            }, UiTheme.Green));
            actions.Add(UiTheme.Btn("Import URDF…", () => {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "Import");
                System.IO.Directory.CreateDirectory(dir);
                int n = 0; foreach (var f in System.IO.Directory.GetFiles(dir, "*.urdf")) if (ArmSmith.Catalogue.UrdfImporter.Import(f) != null) n++;
                if (_catStatus != null) _catStatus.text = $"URDF import: {n} robot(s) from {dir}";
                SwitchTo(View.Catalogue);
            }, UiTheme.Orange));
            detail.Add(actions);
            detail.Add(UiTheme.Caption("Click a thumbnail to select. Saved training models replay on the live arm."));
            rightCol.Add(det);

            wrap.Add(list); wrap.Add(rightCol);
            _content.Add(wrap);
            _refresh = RefreshCatalogue;
        }

        void RefreshCatalogue()
        {
            var d = ArmSmith.Catalogue.RobotCatalogue.Get(_catSelId);
            if (d == null) return;
            if (_catSelName != null) _catSelName.text = d.displayName + $"  ({d.dof}-DOF)";
            if (_catSelInfo != null) _catSelInfo.text = d.notes + "\nSource: " + d.source + (d.hasMeshes ? " · real STL meshes" : " · procedural primitives");
        }

        // ════════════════════════════ TRAINING VIEW ════════════════════════════
        Label _trBackend, _trObsTotal, _trAdvisor, _trBestTile, _trMeanTile, _trSuccTile, _trCurr, _condStatus;
        Button _trStartBtn;
        VisualElement _trCurve, _trStepper;

        void SetCondStatus(string msg) { if (_condStatus != null) _condStatus.text = msg; }

        void BuildTrainingView()
        {
            VisualElement dashBody, condBody;
            var dash = ScrollPanel(UiTheme.PanelHeader("Dashboard & Observation", UiTheme.Orange, "GA + Policy"), out dashBody);
            var cond = ScrollPanel(UiTheme.PanelHeader("Training Conditions", UiTheme.Teal, "reward · DR · curriculum"), out condBody);
            dash.style.width = 360; dash.style.flexShrink = 0;
            cond.style.width = 420; cond.style.flexShrink = 0;
            // Observation+Advisor content now lives in the SAME column as the dashboard (combines panels 1 & 3
            // per feedback). The freed-up 3rd column becomes a LIVE viewport of the arm training in action.
            VisualElement obsBody = dashBody;

            // ── DASHBOARD: metric tiles + sparklines (W&B/TensorBoard pattern) ──
            dashBody.Add(UiTheme.StatRow("BACKEND", "—", out _trBackend, UiTheme.Teal));
            var tiles = UiTheme.Row(); tiles.style.flexWrap = Wrap.Wrap;
            tiles.Add(UiTheme.MetricTile("BEST FITNESS", UiTheme.Orange, () => SafeF(trainer?.lastBestFitness ?? 0f),
                () => trainer != null ? (IList<float>)trainer.bestHistory : null, out _trBestTile));
            tiles.Add(UiTheme.MetricTile("POP MEAN", UiTheme.Teal, () => trainer?.lastMeanFitness ?? 0f,
                () => trainer != null ? (IList<float>)trainer.meanHistory : null, out _trMeanTile));
            tiles.Add(UiTheme.MetricTile("SUCCESS %", UiTheme.Green, () => (trainer?.lastSuccessRate ?? 0f) * 100f,
                () => trainer != null ? (IList<float>)trainer.successHistory : null, out _trSuccTile, "F0"));
            dashBody.Add(tiles);

            // curriculum stepper (Isaac Lab CurriculumCfg viz): L0..L4 nodes, current highlighted
            dashBody.Add(UiTheme.SectionHead("Curriculum"));
            _trStepper = UiTheme.Row(); dashBody.Add(_trStepper);
            _trCurr = UiTheme.Lbl("", UiTheme.Muted, 10); dashBody.Add(_trCurr);
            RebuildStepper();

            // controls
            dashBody.Add(UiTheme.SectionHead("Controls"));
            var c1 = UiTheme.Row();
            _trStartBtn = UiTheme.BtnPrimary("▶ Start (T)", ToggleTraining, UiTheme.Green);
            c1.Add(_trStartBtn);
            c1.Add(UiTheme.Btn("+1 Gen (N)", () => { if (trainer != null) trainer.StepOneGeneration(); }));
            dashBody.Add(c1);
            var c2 = UiTheme.Row();
            c2.Add(UiTheme.Btn("Reset Gen 0", () => { if (trainer != null) trainer.ResetTraining(); }, UiTheme.Red));
            c2.Add(UiTheme.Btn("Mode F8 (GA/Policy)", () => { if (trainer != null) trainer.policyMode = !trainer.policyMode; }, UiTheme.Orange));
            dashBody.Add(c2);

            // ── CONDITIONS: presets + reward-term table + DR ranges + termination/success ──
            if (trainer != null)
            {
                condBody.Add(UiTheme.SectionHead("Presets"));
                var presetRow = UiTheme.Row(); presetRow.style.flexWrap = Wrap.Wrap;
                foreach (TrainingConfig.Preset p in Enum.GetValues(typeof(TrainingConfig.Preset)))
                {
                    var pp = p;
                    presetRow.Add(UiTheme.Btn(TrainingConfig.PresetName(pp), () => {
                        trainer.config.ApplyPreset(pp); trainer.ApplyConfig();
                        if (sensorHub != null) trainer.config.ApplySensorMask(sensorHub);
                        SwitchTo(View.Training);   // rebuild to reflect new values
                    }, UiTheme.Muted));
                }
                condBody.Add(presetRow);

                // reward-term table (Isaac Lab RewardsCfg: named, toggleable, weighted)
                condBody.Add(UiTheme.SectionHead("Reward Terms"));
                AddRewardTerm(condBody, "Reach  −dist(tip,target)", () => trainer.config.eReach, v => trainer.config.eReach = v, () => trainer.config.wReach, v => trainer.config.wReach = v, 0, 5);
                AddRewardTerm(condBody, "Grasp  + when holding", () => trainer.config.eGrasp, v => trainer.config.eGrasp = v, () => trainer.config.wGrasp, v => trainer.config.wGrasp = v, 0, 5);
                AddRewardTerm(condBody, "Place  −dist(obj,goal)", () => trainer.config.ePlace, v => trainer.config.ePlace = v, () => trainer.config.wPlace, v => trainer.config.wPlace = v, 0, 5);
                AddRewardTerm(condBody, "Success bonus", () => trainer.config.eSuccess, v => trainer.config.eSuccess = v, () => trainer.config.wSuccess, v => trainer.config.wSuccess = v, 0, 10);
                AddRewardTerm(condBody, "Energy  −Σ|Δθ|", () => trainer.config.eEnergy, v => trainer.config.eEnergy = v, () => trainer.config.wEnergy, v => trainer.config.wEnergy = v, 0, 0.02f);
                AddRewardTerm(condBody, "Self-penetration", () => trainer.config.eSelfPen, v => trainer.config.eSelfPen = v, () => trainer.config.wSelfPen, v => trainer.config.wSelfPen = v, 0, 5);
                AddRewardTerm(condBody, "Out-of-bounds", () => trainer.config.eOob, v => trainer.config.eOob = v, () => trainer.config.wOob, v => trainer.config.wOob = v, 0, 10);

                // domain randomization ranges (Isaac Lab EventsCfg)
                condBody.Add(UiTheme.SectionHead("Domain Randomization"));
                AddOptSlider(condBody, "DR master (×)", 0f, 1f, () => trainer.config.randomization, v => trainer.config.randomization = v, "");
                AddDrToggle(condBody, "Spawn position ±", () => trainer.config.drSpawnPos, v => trainer.config.drSpawnPos = v, () => trainer.config.drSpawnPosM, "m");
                AddDrToggle(condBody, "Object yaw ±", () => trainer.config.drYaw, v => trainer.config.drYaw = v, () => trainer.config.drYawDeg, "°");
                condBody.Add(UiTheme.ToggleRow("Mass ×0.85–1.15", null, trainer.config.drMass, out var tMass));
                tMass.RegisterValueChangedCallback(e => trainer.config.drMass = e.newValue);
                condBody.Add(UiTheme.ToggleRow("Friction ×0.7–1.3", null, trainer.config.drFriction, out var tFric));
                tFric.RegisterValueChangedCallback(e => trainer.config.drFriction = e.newValue);

                // termination / success (Isaac Lab TerminationsCfg: termination ≠ success)
                condBody.Add(UiTheme.SectionHead("Termination & Success"));
                AddOptSlider(condBody, "Timeout (s)", 5f, 60f, () => trainer.config.timeoutSec, v => trainer.config.timeoutSec = v, "s");
                AddOptSlider(condBody, "Success hold (s)", 0f, 2f, () => trainer.config.successHoldSec, v => trainer.config.successHoldSec = v, "s");
                AddOptSlider(condBody, "Advance @ success", 0.1f, 1f, () => trainer.config.advanceSuccessRate, v => trainer.config.advanceSuccessRate = v, "");
                condBody.Add(UiTheme.ToggleRow("Terminate on out-of-bounds", "end episode if object leaves table", trainer.config.termOnOob, out var tOob));
                tOob.RegisterValueChangedCallback(e => trainer.config.termOnOob = e.newValue);
                condBody.Add(UiTheme.ToggleRow("Predicate success (EV1)", "composable predicate tree", scenarios != null && scenarios.usePredicateSuccess, out var tPred2));
                tPred2.RegisterValueChangedCallback(e => { if (scenarios != null) scenarios.usePredicateSuccess = e.newValue; });

                // apply + persistence (conditions are auto-saved on apply, on interval, and on quit)
                condBody.Add(UiTheme.SectionHead("Conditions Persistence"));
                var saveRow = UiTheme.Row();
                saveRow.Add(UiTheme.Btn("Apply + Save", () => {
                    trainer.ApplyConfig();
                    if (sensorHub != null) trainer.config.ApplySensorMask(sensorHub);
                    if (saveSystem != null) { saveSystem.AutoSaveConditions(); SetCondStatus("conditions saved (autosave)"); }
                }, UiTheme.Orange));
                saveRow.Add(UiTheme.Btn("Reload", () => {
                    if (saveSystem != null && saveSystem.AutoLoadConditions()) { SetCondStatus("conditions reloaded"); SwitchTo(View.Training); }
                    else SetCondStatus("no saved conditions");
                }, UiTheme.Teal));
                condBody.Add(saveRow);
                _condStatus = UiTheme.Lbl("", UiTheme.Green, 10); _condStatus.style.whiteSpace = WhiteSpace.Normal; condBody.Add(_condStatus);
                condBody.Add(UiTheme.Caption("Conditions auto-save on apply, every 30s, and on quit — they persist across sessions."));
            }

            // ── OBSERVATION + ADVISOR (same column as the dashboard now) ──
            obsBody.Add(UiTheme.SectionHead("Observation & Advisor"));
            obsBody.Add(UiTheme.Caption("Which sensor channels feed the policy this generation"));
            obsBody.Add(UiTheme.StatRow("OBS CHANNELS", "—", out _trObsTotal, UiTheme.Green));
            AddObsToggle(obsBody, "MotorEncoders", () => trainer.config.useMotorEncoders, v => trainer.config.useMotorEncoders = v);
            AddObsToggle(obsBody, "TaskState", () => trainer.config.useTaskState, v => trainer.config.useTaskState = v);
            AddObsToggle(obsBody, "IMU", () => trainer.config.useImu, v => trainer.config.useImu = v);
            AddObsToggle(obsBody, "RangeFinder", () => trainer.config.useRangeFinder, v => trainer.config.useRangeFinder = v);
            AddObsToggle(obsBody, "Lidar2D", () => trainer.config.useLidar, v => trainer.config.useLidar = v);
            AddObsToggle(obsBody, "DepthCamera", () => trainer.config.useDepthCamera, v => trainer.config.useDepthCamera = v);
            AddObsToggle(obsBody, "EFlesh Tactile", () => trainer.config.useTactile, v => trainer.config.useTactile = v);

            obsBody.Add(UiTheme.SectionHead("Sensor Realism (F-r2)"));
            obsBody.Add(UiTheme.ToggleRow("Noise + latency", "imperfect sensors -> robust policy", SensorRealism.enabled, out var tReal));
            tReal.RegisterValueChangedCallback(e => SensorRealism.enabled = e.newValue);

            obsBody.Add(UiTheme.SectionHead("Module Advisor (S10)"));
            _trAdvisor = UiTheme.Lbl("Train with different sensor masks to compare sets.", UiTheme.Muted, 10);
            _trAdvisor.style.whiteSpace = WhiteSpace.Normal;
            obsBody.Add(_trAdvisor);

            // Layout: [dashboard+observation] [conditions] [LIVE training viewport]
            var wrap = new VisualElement(); wrap.style.flexDirection = FlexDirection.Row; wrap.style.flexGrow = 1;
            dash.style.marginRight = 8; cond.style.marginRight = 0;
            wrap.Add(dash); wrap.Add(cond);
            wrap.Add(LiveViewport("LIVE TRAINING — the arm attempting the task"));
            _content.Add(wrap);
            _refresh = RefreshTraining;
        }

        static float SafeF(float f) => f <= float.NegativeInfinity ? 0f : f;

        void RebuildStepper()
        {
            if (_trStepper == null || trainer == null) return;
            _trStepper.Clear();
            string[] levels = { "L0", "L1", "L2", "L3", "L4" };
            int cur = Mathf.Clamp(Mathf.RoundToInt(trainer.config.difficulty * 4f), 0, 4);
            for (int i = 0; i < levels.Length; i++)
            {
                var node = new Label(levels[i]);
                node.style.fontSize = 10; node.style.unityFontStyleAndWeight = FontStyle.Bold;
                node.style.paddingLeft = 6; node.style.paddingRight = 6; node.style.paddingTop = 3; node.style.paddingBottom = 3;
                node.style.marginRight = 3;
                bool done = i < cur, active = i == cur;
                Color c = active ? UiTheme.Teal : (done ? UiTheme.Green : UiTheme.Muted);
                node.style.color = active ? UiTheme.TextHi : c;
                node.style.backgroundColor = active ? new Color(UiTheme.Teal.r, UiTheme.Teal.g, UiTheme.Teal.b, 0.2f) : new Color(0, 0, 0, 0);
                UiTheme.SetBorder(node, c, 1); UiTheme.SetRadius(node, 3);
                _trStepper.Add(node);
                if (i < levels.Length - 1) { var a = new Label("→"); a.style.color = UiTheme.Muted; a.style.marginRight = 3; _trStepper.Add(a); }
            }
        }

        void AddRewardTerm(VisualElement host, string label, Func<bool> getEn, Action<bool> setEn,
                           Func<float> getW, Action<float> setW, float min, float max)
        {
            var row = UiTheme.Row(); row.style.justifyContent = Justify.SpaceBetween; row.style.marginTop = 2; row.style.marginBottom = 2;
            var tog = new Toggle { value = getEn() }; tog.style.marginRight = 4; tog.style.flexShrink = 0;
            tog.RegisterValueChangedCallback(e => setEn(e.newValue));
            var name = UiTheme.Caption(label); name.style.flexGrow = 1;
            var s = new Slider(min, max) { value = getW() }; s.style.width = 90; s.style.flexShrink = 0; s.style.marginLeft = 4; s.style.marginRight = 4;
            var v = UiTheme.Lbl(getW().ToString("0.###"), UiTheme.Orange, 10); v.style.width = 44; v.style.flexShrink = 0; v.style.unityTextAlign = TextAnchor.MiddleRight;
            s.RegisterValueChangedCallback(e => { setW(e.newValue); v.text = e.newValue.ToString("0.###"); });
            row.Add(tog); row.Add(name); row.Add(s); row.Add(v);
            host.Add(row);
        }

        void AddDrToggle(VisualElement host, string label, Func<bool> getEn, Action<bool> setEn, Func<float> getVal, string suf)
        {
            var row = UiTheme.Row(); row.style.justifyContent = Justify.SpaceBetween; row.style.marginTop = 2; row.style.marginBottom = 2;
            var tog = new Toggle { value = getEn() }; tog.style.marginRight = 4; tog.style.flexShrink = 0;
            tog.RegisterValueChangedCallback(e => setEn(e.newValue));
            row.Add(tog);
            var name = UiTheme.Caption(label); name.style.flexGrow = 1; row.Add(name);
            row.Add(UiTheme.Lbl(getVal().ToString("0.##") + suf, UiTheme.Teal, 10));
            host.Add(row);
        }

        void AddObsToggle(VisualElement host, string name, Func<bool> get, Action<bool> set)
        {
            if (trainer == null) { host.Add(UiTheme.Caption(name + " (no trainer)")); return; }
            var row = UiTheme.ToggleRow(name, null, get(), out var tog);
            tog.RegisterValueChangedCallback(e => { set(e.newValue); if (sensorHub != null) trainer.config.ApplySensorMask(sensorHub); });
            host.Add(row);
        }

        void ToggleTraining()
        {
            if (trainer == null) return;
            if (trainer.Running) trainer.StopTraining(); else trainer.StartTraining();
        }

        int _trLastDifficultyTick = -1;
        void RefreshTraining()
        {
            if (trainer == null) return;
            if (_trBackend != null) _trBackend.text = trainer.policyMode ? "Sensor-Policy (closed-loop)" : "Motion-GA (open-loop)";
            if (_trBestTile != null) _trBestTile.text = trainer.lastBestFitness <= float.NegativeInfinity ? "—" : trainer.lastBestFitness.ToString("F2");
            if (_trMeanTile != null) _trMeanTile.text = trainer.lastMeanFitness.ToString("F2");
            if (_trSuccTile != null) _trSuccTile.text = $"{trainer.lastSuccessRate * 100f:F0}";
            if (_trCurr != null) _trCurr.text = $"{trainer.config.LevelName()} · difficulty {trainer.config.difficulty:F2} · gen {trainer.generation}";
            if (_trStartBtn != null) { _trStartBtn.text = trainer.Running ? "■ Stop (T)" : "▶ Start (T)"; UiTheme.SetActive(_trStartBtn, trainer.Running, trainer.Running ? UiTheme.Red : UiTheme.Green); }
            if (_trObsTotal != null && sensorHub != null) _trObsTotal.text = sensorHub.BuildObservation().Length.ToString();
            if (_trAdvisor != null && scenarios != null)
            {
                var rec = ModuleAdvisor.Recommend(scenarios.current.ToString());
                _trAdvisor.text = rec != null
                    ? $"Best so far for {scenarios.current}: {rec.sensorSet} — {rec.bestSuccess * 100f:F0}% success, {rec.channels} ch (n={rec.samples})"
                    : "Train with different sensor masks to compare sets.";
            }
            // rebuild the curriculum stepper only when the level actually changes (auto-curriculum advance)
            int tick = Mathf.RoundToInt(trainer.config.difficulty * 4f);
            if (tick != _trLastDifficultyTick) { _trLastDifficultyTick = tick; RebuildStepper(); }
            // refresh the metric-tile sparklines
            foreach (var sl in _content.Query<UiTheme.Sparkline>().ToList()) sl.MarkDirtyRepaint();
        }

        // ════════════════════════════ OPTIONS VIEW ════════════════════════════
        readonly List<(Slider s, Label l, Func<float> get, Action<float> set, string suf)> _optBinds = new List<(Slider, Label, Func<float>, Action<float>, string)>();
        Label _optRobotStatus; bool _optRobotConnected; string _optRobotPort = "/dev/ttyACM0";

        void BuildOptionsView()
        {
            _optBinds.Clear();
            // ONE sectioned settings panel + a live viewport (so you watch the sim while you tune).
            VisualElement b;
            EditLayout("Options & Calibration", UiTheme.Teal, "settings · real robot", 460,
                "LIVE SIM — changes apply instantly", out b);

            // ── Real robot plug-in & calibration ──
            b.Add(UiTheme.SectionHead("Real Robot — Plug-in & Calibration"));
            _optRobotStatus = UiTheme.Lbl("No real robot connected (simulation only).", UiTheme.Muted, 11);
            _optRobotStatus.style.whiteSpace = WhiteSpace.Normal; b.Add(_optRobotStatus);
            var portRow = UiTheme.Row();
            var portField = new TextField { value = "/dev/ttyACM0" }; portField.style.flexGrow = 1; portField.style.marginRight = 6;
            portRow.Add(UiTheme.Caption("Serial port")); portRow.Add(portField);
            b.Add(portRow);
            var connRow = UiTheme.Row();
            connRow.Add(UiTheme.BtnPrimary("⚡ Connect", () => { _optRobotConnected = !_optRobotConnected; _optRobotPort = portField.value; }, UiTheme.Green));
            connRow.Add(UiTheme.Btn("Scan ports", () => { _optRobotStatus.text = "Scan: export waypoints/joint-map for the LeRobot bridge (scripts/realbot/)."; }, UiTheme.Teal));
            b.Add(connRow);
            b.Add(UiTheme.SectionHead("Calibrate"));
            b.Add(UiTheme.Caption("Set the arm's current pose as the servo zero, then capture min/max per joint."));
            var calRow = UiTheme.Row();
            calRow.Add(UiTheme.Btn("⊙ Set Zero (C)", () => { if (controller != null) controller.CalibrateZeroHere(); }, UiTheme.Orange));
            calRow.Add(UiTheme.Btn("⤓ Go to Zero", () => { if (controller != null) controller.GoToZero(); }, UiTheme.Teal));
            b.Add(calRow);
            b.Add(UiTheme.Caption("Calibration + waypoints export to the real STS3215 bus (scripts/realbot/armsmith_player.py)."));

            // ── Simulation & physics ──
            b.Add(UiTheme.SectionHead("Simulation & Physics"));
            AddOptSlider(b, "Sim Speed (×)", 0f, 10f, () => Time.timeScale, v => Time.timeScale = v, "×");
            b.Add(UiTheme.Caption("⚠ Motors are NOT instant — servo stiffness/damping (ServoModel) set the response."));
            AddOptSlider(b, "Solver Iterations", 4f, 24f, () => Physics.defaultSolverIterations, v => Physics.defaultSolverIterations = Mathf.RoundToInt(v), "");
            AddOptSlider(b, "Gravity (m/s²)", 0f, 20f, () => -Physics.gravity.y, v => Physics.gravity = new Vector3(0, -v, 0), "");

            // ── Control & display ──
            b.Add(UiTheme.SectionHead("Control & Display"));
            if (controller != null)
            {
                b.Add(UiTheme.ToggleRow("Mouse-follow IK", "arm follows the cursor (M)", controller.mouseFollow, out var tMouse));
                tMouse.RegisterValueChangedCallback(e => controller.mouseFollow = e.newValue);
            }
            b.Add(UiTheme.Caption("Overlays: world axes (X) · bounds (B) · cam HUD (V) · servo callouts (\\)"));
            AddOptSlider(b, "UI reference width", 1280f, 2560f, () => UiTheme.GetPanelSettings().referenceResolution.x,
                v => UiTheme.GetPanelSettings().referenceResolution = new Vector2Int(Mathf.RoundToInt(v), 1080), "px");

            // ── Randomisation & curriculum ──
            if (scenarios != null)
            {
                b.Add(UiTheme.SectionHead("Randomisation & Curriculum"));
                AddOptSlider(b, "Object spawn randomness", 0f, 1f, () => scenarios.randomness, v => scenarios.randomness = v, "");
            }
            if (trainer != null)
            {
                AddOptSlider(b, "Difficulty", 0f, 1f, () => trainer.config.difficulty, v => trainer.config.difficulty = v, "");
                AddOptSlider(b, "Randomisation", 0f, 1f, () => trainer.config.randomization, v => trainer.config.randomization = v, "");
                b.Add(UiTheme.ToggleRow("Curriculum auto-advance", "bump difficulty when success high", trainer.config.autoCurriculum, out var tAuto));
                tAuto.RegisterValueChangedCallback(e => trainer.config.autoCurriculum = e.newValue);
                b.Add(UiTheme.ToggleRow("Predicate success eval (EV1)", "use composable predicate tree", scenarios != null && scenarios.usePredicateSuccess, out var tPred));
                tPred.RegisterValueChangedCallback(e => { if (scenarios != null) scenarios.usePredicateSuccess = e.newValue; });
                b.Add(UiTheme.SectionHead("GA Hyperparameters"));
                AddOptSlider(b, "Population", 4f, 48f, () => trainer.config.populationSize, v => trainer.config.populationSize = Mathf.RoundToInt(v), "");
                AddOptSlider(b, "Mutation rate", 0f, 1f, () => trainer.config.mutationRate, v => trainer.config.mutationRate = v, "");
                b.Add(UiTheme.BtnPrimary("Apply + Save Settings", () => { if (trainer != null) trainer.ApplyConfig(); if (saveSystem != null) saveSystem.AutoSaveConditions(); }, UiTheme.Orange));
            }

            _refresh = RefreshOptions;
        }

        void AddOptSlider(VisualElement host, string label, float min, float max, Func<float> get, Action<float> set, string suf)
        {
            var row = UiTheme.SliderRow(label, min, max, get(), out var s, out var l, suf);
            s.RegisterValueChangedCallback(e => { set(e.newValue); l.text = e.newValue.ToString("0.##") + suf; });
            host.Add(row);
            _optBinds.Add((s, l, get, set, suf));
        }

        void RefreshOptions()
        {
            // keep sliders in sync with values that change elsewhere (e.g. Time.timeScale via +/- keys)
            foreach (var b in _optBinds)
            {
                float cur = b.get();
                if (!b.s.HasMouseCapture() && Mathf.Abs(b.s.value - cur) > 0.001f)
                { b.s.SetValueWithoutNotify(cur); b.l.text = cur.ToString("0.##") + b.suf; }
            }
            if (_optRobotStatus != null)
                _optRobotStatus.text = _optRobotConnected
                    ? $"● Connected to real robot on {_optRobotPort} — calibration + waypoints will stream to the STS3215 bus."
                    : "No real robot connected (simulation only). Enter a serial port and Connect to mirror to hardware.";
        }

        // ════════════════════════════ HELP VIEW ════════════════════════════
        void BuildHelpView()
        {
            VisualElement body;
            var panel = ScrollPanel(UiTheme.PanelHeader("Help & Controls", UiTheme.Teal, "convey everything"), out body);

            AddHelpSection(body, "Interface", new[] {
                ("F1", "toggle this interface overlay"),
                ("Nav tabs", "Menu · Dashboard · Build · Modules · Catalogue · Training · Options · Help"),
                ("MODE pill (nav)", "click ◀ IK/MANUAL ▶ to switch control mode"),
                ("Shift+S", "sensor-only teleop (operate from sensor data only)"),
                ("Esc", "back to Dashboard"),
                ("Shift+H", "this Help view"),
            });
            AddHelpSection(body, "Build & Modules", new[] {
                ("Build view", "edit joint limits (parametric chain) + replay creations"),
                ("Modules view", "mount/enable sensor modules; see obs-channel + mass budget"),
                ("Catalogue view", "generate parametric arms / import a URDF robot"),
            });
            AddHelpSection(body, "Control", new[] {
                ("M", "mouse-follow IK on/off"),
                ("scroll / [ ]", "IK target depth"),
                ("T/G Y/H U/J I/K O/L P/;", "per-servo direct keys (joints 0..5)"),
                (", / .", "claw open / close"),
                ("double-click", "grab / place object"),
            });
            AddHelpSection(body, "Camera", new[] {
                ("RMB", "orbit"), ("MMB", "pan"), ("Ctrl+scroll", "zoom"),
                ("V", "camera HUD"), ("B", "bounds"), ("X", "axes"),
            });
            AddHelpSection(body, "Scenarios & Tasks", new[] {
                ("1–7", "select scenario"), ("Esc", "reset scenario"),
                ("F1 (agent)", "auto-solve current task"),
            });
            AddHelpSection(body, "Training", new[] {
                ("T", "start/stop training"), ("N", "+1 generation"),
                ("F8", "policy(sensor) vs motion mode"), ("F11", "export best"),
                ("F3/F4/F7", "legacy Training/Conditions/Generations panels"),
            });
            AddHelpSection(body, "Sensors & Export", new[] {
                ("Shift+F2..F7", "sensor ablation toggles"), ("F12", "module usage panel"),
                ("F9", "export STL"), ("F10", "export waypoints"),
                ("\\ + click", "servo callouts"),
            });

            _content.Add(panel);
            _refresh = null;
        }

        void AddHelpSection(VisualElement host, string title, (string key, string desc)[] rows)
        {
            host.Add(UiTheme.SectionHead(title));
            foreach (var (key, desc) in rows)
            {
                var row = UiTheme.Row(); row.style.marginTop = 2; row.style.marginBottom = 2;
                var k = UiTheme.Lbl(key, UiTheme.Teal, 11); k.style.width = 180; k.style.flexShrink = 0; k.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(k); row.Add(UiTheme.Lbl(desc, UiTheme.Text, 11));
                host.Add(row);
            }
        }
    }
}
