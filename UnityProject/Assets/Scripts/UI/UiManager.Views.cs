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

        // ════════════════════════════ MENU VIEW ════════════════════════════
        void BuildMenuView()
        {
            var wrap = new VisualElement(); wrap.style.flexDirection = FlexDirection.Row; wrap.style.flexGrow = 1;

            // left: title + nav
            var left = UiTheme.Panel(); left.style.width = 340; left.style.flexShrink = 0; left.style.marginRight = 8;
            var lb = new VisualElement(); UiTheme.Pad(lb, 16); left.Add(lb);
            lb.Add(UiTheme.Caption("Robotic Arm Design & Evolution System"));
            var title = UiTheme.Row();
            var t1 = new Label("ARM"); t1.style.color = UiTheme.Teal; t1.style.fontSize = 44; t1.style.unityFontStyleAndWeight = FontStyle.Bold; t1.style.letterSpacing = 3f;
            var t2 = new Label("SMITH"); t2.style.color = UiTheme.Orange; t2.style.fontSize = 44; t2.style.unityFontStyleAndWeight = FontStyle.Bold; t2.style.letterSpacing = 3f;
            title.Add(t1); title.Add(t2); lb.Add(title);
            lb.Add(UiTheme.Lbl("Design · Control · Evolve · Export", UiTheme.Muted, 12));
            lb.Add(UiTheme.Lbl("v0.9 — Unity 6000.4.2f1 · URP · ArticulationBody", UiTheme.TextDim, 10));
            lb.Add(UiTheme.SectionHead("Navigate"));
            lb.Add(UiTheme.Btn("▶ Open Dashboard", () => SwitchTo(View.Dashboard), UiTheme.Green));
            lb.Add(UiTheme.Btn("⚙ Training", () => SwitchTo(View.Training), UiTheme.Orange));
            lb.Add(UiTheme.Btn("⚙ Options", () => SwitchTo(View.Options)));
            lb.Add(UiTheme.Btn("? Help & Controls", () => SwitchTo(View.Help)));

            // right: scenario select
            VisualElement body;
            var right = ScrollPanel(UiTheme.PanelHeader("Scenario Select", UiTheme.Teal, "7 scenarios"), out body);
            body.Add(UiTheme.Caption("Choose a manipulation task — click to load it live"));
            var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap; body.Add(grid);
            foreach (ScenarioType st in Enum.GetValues(typeof(ScenarioType)))
                grid.Add(ScenarioCard(st));

            wrap.Add(left); wrap.Add(right);
            _content.Add(wrap);
        }

        VisualElement ScenarioCard(ScenarioType st)
        {
            var card = UiTheme.Panel(); card.style.width = 220; card.style.marginRight = 8;
            var b = new VisualElement(); UiTheme.Pad(b, 10); card.Add(b);
            var name = UiTheme.Lbl(st.ToString(), UiTheme.TextHi, 13); name.style.unityFontStyleAndWeight = FontStyle.Bold; b.Add(name);
            b.Add(UiTheme.Lbl(ScenarioBlurb(st), UiTheme.Muted, 10));
            int diff = ScenarioDifficulty(st);
            var dots = UiTheme.Row();
            for (int i = 0; i < 3; i++) { var d = new Label("●"); d.style.fontSize = 9; d.style.color = i < diff ? (diff == 3 ? UiTheme.Orange : UiTheme.Teal) : UiTheme.TextDim; dots.Add(d); }
            dots.Add(UiTheme.Lbl(diff == 1 ? " easy" : diff == 2 ? " medium" : " hard", UiTheme.Muted, 9));
            b.Add(dots);
            var btn = UiTheme.Btn("Launch", () => { if (scenarios != null) scenarios.LoadScenario(st); SwitchTo(View.Dashboard); }, UiTheme.Green);
            btn.style.marginTop = 6; b.Add(btn);
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
        VisualElement _dbJointHost, _dbRewardBar, _dbContactHost;
        Button _dbModeBtn, _dbGripBtn;

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
            driverBody.Add(UiTheme.SectionHead("Control Mode"));
            var modeRow = UiTheme.Row();
            _dbModeBtn = UiTheme.Btn("IK Mode", ToggleMode, UiTheme.Teal);
            modeRow.Add(_dbModeBtn);
            modeRow.Add(UiTheme.Btn("Mouse-follow (M)", () => { if (controller != null) controller.mouseFollow = !controller.mouseFollow; }));
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
                _dbModeBtn.text = ik ? "IK Mode" : "Manual";
                UiTheme.SetActive(_dbModeBtn, ik, ik ? UiTheme.Teal : UiTheme.Orange);
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
            VisualElement loadBody, catBody;
            var load = ScrollPanel(UiTheme.PanelHeader("Mounted Loadout", UiTheme.Teal, "on the arm"), out loadBody);
            var cat  = ScrollPanel(UiTheme.PanelHeader("Module Catalog", UiTheme.Orange, "click to mount"), out catBody);

            // budget readout (mass / channels) — gamifies the "more sensors = heavier" trade-off
            _modBudget = UiTheme.Lbl("", UiTheme.Muted, 11); _modBudget.style.whiteSpace = WhiteSpace.Normal;
            loadBody.Add(_modBudget);
            loadBody.Add(UiTheme.SectionHead("Active Modules"));

            if (sensorHub != null)
            {
                foreach (var s in sensorHub.Sensors)
                {
                    var sensor = s;
                    var row = UiTheme.Row(); row.style.justifyContent = Justify.SpaceBetween;
                    row.style.borderTopColor = UiTheme.Border; row.style.borderTopWidth = 1; row.style.paddingTop = 4; row.style.paddingBottom = 4;
                    var left = UiTheme.Row();
                    var eye = UiTheme.Btn(sensor.Enabled ? "👁 on" : "✕ off", null, sensor.Enabled ? UiTheme.Green : UiTheme.Muted);
                    eye.clicked += () => { sensor.Enabled = !sensor.Enabled; SyncMaskFromHub(); SwitchTo(View.Modules); };
                    left.Add(eye);
                    left.Add(UiTheme.Lbl(sensor.Name, UiTheme.TextHi, 11));
                    row.Add(left);
                    row.Add(UiTheme.Lbl(sensor.Channels.Length + " ch", UiTheme.Muted, 10));
                    loadBody.Add(row);
                }
            }
            loadBody.Add(UiTheme.Caption("Toggle a module's eye to include/exclude it from the policy observation."));

            // catalog cards
            catBody.Add(UiTheme.Caption("Add-on sensors & end-effector modules. Mounting attaches to a link socket."));
            foreach (var m in kModuleCatalog)
            {
                var md = m;
                var card = UiTheme.Panel(md.accent);
                var cb = new VisualElement(); UiTheme.Pad(cb, 8); card.Add(cb);
                var t = UiTheme.Row();
                var nm = UiTheme.Lbl(md.name, UiTheme.TextHi, 12); nm.style.unityFontStyleAndWeight = FontStyle.Bold; t.Add(nm);
                t.Add(UiTheme.Badge(md.channels + " ch", md.accent));
                bool mounted = sensorHub != null && sensorHub.Get(md.type) != null;
                if (mounted) t.Add(UiTheme.Badge("mounted", UiTheme.Green));
                cb.Add(t);
                cb.Add(UiTheme.Lbl(md.spec, UiTheme.Muted, 10));
                // advisor hint (S10): suggest tactile/range for grasp tasks
                if (scenarios != null && (md.type == "EFleshTactile" || md.type == "RangeFinder"))
                    cb.Add(UiTheme.Lbl("advisor: improves grasp success", UiTheme.Green, 9));
                var act = UiTheme.Row();
                act.Add(UiTheme.Btn(mounted ? "Enable" : "Mount", () => {
                    if (sensorHub != null) { sensorHub.SetEnabled(md.type, true); SyncMaskFromHub(); }
                    SwitchTo(View.Modules);
                }, UiTheme.Green));
                act.Add(UiTheme.Btn("Disable", () => {
                    if (sensorHub != null) { sensorHub.SetEnabled(md.type, false); SyncMaskFromHub(); }
                    SwitchTo(View.Modules);
                }, UiTheme.Muted));
                cb.Add(act);
                catBody.Add(card);
            }

            // mount sockets info (from ModuleMount)
            if (moduleMount != null && moduleMount.mountPoints.Count > 0)
            {
                catBody.Add(UiTheme.SectionHead("Mount Sockets"));
                foreach (var mp in moduleMount.mountPoints)
                    catBody.Add(UiTheme.Caption($"• {mp.name} (link {mp.linkIndex})"));
            }

            var cols = Columns(load, cat); cols.style.flexGrow = 1;
            _content.Add(cols);
            _refresh = RefreshModules;
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
        }

        void RefreshModules()
        {
            if (_modBudget == null || sensorHub == null) return;
            int active = 0, ch = 0;
            foreach (var s in sensorHub.Sensors) if (s.Enabled) { active++; ch += s.Channels.Length; }
            float massKg = 0.02f * active;   // ~20g per module (illustrative budget)
            _modBudget.text = $"Modules: {active} active · {ch} obs channels · ≈{massKg * 1000f:F0} g added mass";
        }

        // ════════════════════════════ BUILD VIEW (joint editor + creations) ════════════════════════════
        // Fusion feature-tree (parametric chain) + generative-design outcome gallery patterns.
        Label _bldStats, _bldStatus;
        VisualElement _bldChain, _bldGallery;

        void BuildBuildView()
        {
            VisualElement chainBody, galBody;
            var chain = ScrollPanel(UiTheme.PanelHeader("Joint / Link Editor", UiTheme.Teal, "parametric chain"), out chainBody);
            var gal   = ScrollPanel(UiTheme.PanelHeader("Creations Library", UiTheme.Orange, "saved & evolved"), out galBody);

            // arm stats (live)
            _bldStats = UiTheme.Lbl("", UiTheme.Muted, 11); _bldStats.style.whiteSpace = WhiteSpace.Normal; chainBody.Add(_bldStats);
            chainBody.Add(UiTheme.SectionHead("Kinematic Chain"));
            _bldChain = new VisualElement(); chainBody.Add(_bldChain);
            RebuildChain();
            _bldStatus = UiTheme.Lbl("", UiTheme.Green, 10); _bldStatus.style.whiteSpace = WhiteSpace.Normal; chainBody.Add(_bldStatus);
            chainBody.Add(UiTheme.Caption("Edit limits live. (Adding/removing DOF rebuilds the arm — load a catalogue robot for a different DOF.)"));

            // creations gallery
            galBody.Add(UiTheme.Caption("Best-of-generation creations — replay, or browse evolution history."));
            _bldGallery = new VisualElement(); galBody.Add(_bldGallery);
            RebuildGallery();

            var cols = Columns(chain, gal); cols.style.flexGrow = 1;
            _content.Add(cols);
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
                // limit dual-ish sliders (min/max angle) — live edit
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
        }

        // ════════════════════════════ CATALOGUE VIEW (J2/J3) ════════════════════════════
        Label _catStatus;

        void BuildCatalogueView()
        {
            VisualElement body;
            var panel = ScrollPanel(UiTheme.PanelHeader("Robot Catalogue", UiTheme.Teal, "import & generate"), out body);
            body.Add(UiTheme.Caption("Open-source robots — each is a kinematics JSON the builder can load. " +
                "Generate parametric arms or import a URDF; the active arm swaps on scene reload."));
            _catStatus = UiTheme.Lbl("", UiTheme.Green, 11); _catStatus.style.whiteSpace = WhiteSpace.Normal; body.Add(_catStatus);

            foreach (var d in ArmSmith.Catalogue.RobotCatalogue.Entries)
            {
                var card = UiTheme.Panel(d.hasMeshes ? UiTheme.Teal : UiTheme.Orange);
                var cb = new VisualElement(); UiTheme.Pad(cb, 10); card.Add(cb);
                var title = UiTheme.Row();
                var nm = UiTheme.Lbl(d.displayName, UiTheme.TextHi, 13); nm.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.Add(nm);
                title.Add(UiTheme.Badge($"{d.dof}-DOF", d.hasMeshes ? UiTheme.Teal : UiTheme.Orange));
                title.Add(UiTheme.Badge(d.source, UiTheme.Muted));
                cb.Add(title);
                cb.Add(UiTheme.Lbl(d.notes, UiTheme.Muted, 10));
                var actions = UiTheme.Row();
                string id = d.id;
                actions.Add(UiTheme.Btn("Resolve / Generate", () => {
                    string path = ArmSmith.Catalogue.RobotCatalogue.ResolveKinematicsPath(id);
                    if (_catStatus != null) _catStatus.text = path != null ? $"{id}: kinematics ready at {System.IO.Path.GetFileName(path)}" : $"{id}: failed to resolve.";
                }, UiTheme.Green));
                cb.Add(actions);
            }

            // URDF import affordance
            body.Add(UiTheme.SectionHead("Import URDF (J3)"));
            body.Add(UiTheme.Caption("Drop a .urdf into persistentDataPath/Import then click — it converts to " +
                "the kinematics schema and registers as a catalogue entry."));
            body.Add(UiTheme.Btn("Scan import folder", () => {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "Import");
                System.IO.Directory.CreateDirectory(dir);
                int n = 0;
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.urdf"))
                { if (ArmSmith.Catalogue.UrdfImporter.Import(f) != null) n++; }
                if (_catStatus != null) _catStatus.text = $"URDF import: {n} robot(s) imported from {dir}";
                SwitchTo(View.Catalogue);   // refresh the list
            }, UiTheme.Orange));

            _content.Add(panel);
            _refresh = null;
        }

        // ════════════════════════════ TRAINING VIEW ════════════════════════════
        Label _trBackend, _trObsTotal, _trAdvisor, _trBestTile, _trMeanTile, _trSuccTile, _trCurr;
        Button _trStartBtn;
        VisualElement _trCurve, _trStepper;

        void BuildTrainingView()
        {
            VisualElement dashBody, condBody, obsBody;
            var dash = ScrollPanel(UiTheme.PanelHeader("Live Dashboard", UiTheme.Orange, "GA + Policy"), out dashBody);
            var cond = ScrollPanel(UiTheme.PanelHeader("Training Conditions", UiTheme.Teal, "reward · DR · curriculum"), out condBody);
            var obs  = ScrollPanel(UiTheme.PanelHeader("Observation & Advisor", UiTheme.Green), out obsBody);

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
            _trStartBtn = UiTheme.Btn("▶ Start (T)", ToggleTraining, UiTheme.Green);
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

                condBody.Add(UiTheme.Btn("Apply to trainer", () => trainer.ApplyConfig(), UiTheme.Orange));
            }

            // ── OBSERVATION + ADVISOR ──
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

            var cols = Columns(dash, cond, obs); cols.style.flexGrow = 1;
            _content.Add(cols);
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
            if (_trStartBtn != null) { _trStartBtn.text = trainer.Running ? "■ Stop (T)" : "▶ Start (T)"; UiTheme.SetActive(_trStartBtn, trainer.Running, UiTheme.Green); }
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

        void BuildOptionsView()
        {
            _optBinds.Clear();
            VisualElement simBody, ctrlBody, randBody;
            var sim  = ScrollPanel(UiTheme.PanelHeader("Simulation & Physics", UiTheme.Teal), out simBody);
            var ctrl = ScrollPanel(UiTheme.PanelHeader("Control & Display", UiTheme.Orange), out ctrlBody);
            var rand = ScrollPanel(UiTheme.PanelHeader("Randomisation & Curriculum", UiTheme.Green), out randBody);

            // -- simulation --
            simBody.Add(UiTheme.SectionHead("Sim Speed"));
            AddOptSlider(simBody, "Sim Speed (×)", 0f, 10f, () => Time.timeScale, v => Time.timeScale = v, "×");
            simBody.Add(UiTheme.Caption("⚠ Motors are NOT instant — servo drive stiffness/damping (ServoModel) set response."));
            simBody.Add(UiTheme.SectionHead("Physics"));
            AddOptSlider(simBody, "Solver Iterations", 4f, 24f, () => Physics.defaultSolverIterations, v => Physics.defaultSolverIterations = Mathf.RoundToInt(v), "");
            AddOptSlider(simBody, "Gravity (m/s²)", 0f, 20f, () => -Physics.gravity.y, v => Physics.gravity = new Vector3(0, -v, 0), "");

            // -- control & display --
            if (controller != null)
            {
                ctrlBody.Add(UiTheme.SectionHead("Mouse / IK"));
                ctrlBody.Add(UiTheme.ToggleRow("Mouse-follow IK", "arm follows the cursor (M)", controller.mouseFollow, out var tMouse));
                tMouse.RegisterValueChangedCallback(e => controller.mouseFollow = e.newValue);
            }
            ctrlBody.Add(UiTheme.SectionHead("Display Overlays"));
            ctrlBody.Add(UiTheme.Caption("World axes (X) · bounds (B) · cam HUD (V) · servo callouts (\\)"));
            ctrlBody.Add(UiTheme.SectionHead("UI Scale"));
            AddOptSlider(ctrlBody, "Reference width", 1280f, 2560f, () => UiTheme.GetPanelSettings().referenceResolution.x,
                v => UiTheme.GetPanelSettings().referenceResolution = new Vector2Int(Mathf.RoundToInt(v), 1080), "px");

            // -- randomisation & curriculum --
            if (scenarios != null)
            {
                randBody.Add(UiTheme.SectionHead("Object Spawn"));
                AddOptSlider(randBody, "Position randomness", 0f, 1f, () => scenarios.randomness, v => scenarios.randomness = v, "");
            }
            if (trainer != null)
            {
                randBody.Add(UiTheme.SectionHead("Difficulty & Curriculum"));
                AddOptSlider(randBody, "Difficulty", 0f, 1f, () => trainer.config.difficulty, v => trainer.config.difficulty = v, "");
                AddOptSlider(randBody, "Randomisation", 0f, 1f, () => trainer.config.randomization, v => trainer.config.randomization = v, "");
                randBody.Add(UiTheme.ToggleRow("Curriculum auto-advance", "bump difficulty when success high", trainer.config.autoCurriculum, out var tAuto));
                tAuto.RegisterValueChangedCallback(e => trainer.config.autoCurriculum = e.newValue);
                randBody.Add(UiTheme.ToggleRow("Predicate success eval (EV1)", "use composable predicate tree", scenarios != null && scenarios.usePredicateSuccess, out var tPred));
                tPred.RegisterValueChangedCallback(e => { if (scenarios != null) scenarios.usePredicateSuccess = e.newValue; });
                randBody.Add(UiTheme.SectionHead("GA Hyperparameters"));
                AddOptSlider(randBody, "Population", 4f, 48f, () => trainer.config.populationSize, v => trainer.config.populationSize = Mathf.RoundToInt(v), "");
                AddOptSlider(randBody, "Mutation rate", 0f, 1f, () => trainer.config.mutationRate, v => trainer.config.mutationRate = v, "");
                randBody.Add(UiTheme.Btn("Apply to trainer", () => { if (trainer != null) trainer.ApplyConfig(); }, UiTheme.Orange));
            }

            var cols = Columns(sim, ctrl, rand); cols.style.flexGrow = 1;
            _content.Add(cols);
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
