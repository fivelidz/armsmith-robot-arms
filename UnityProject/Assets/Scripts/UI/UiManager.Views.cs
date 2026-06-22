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
        Label _dbObjective, _dbReward, _dbSuccess, _dbEE, _dbGrip;
        VisualElement _dbJointHost, _dbRewardBar;
        Button _dbModeBtn, _dbGripBtn;

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

            driverBody.Add(UiTheme.SectionHead("Demonstration"));
            var recRow = UiTheme.Row();
            recRow.Add(UiTheme.Btn("⏺ Record (Backspace)", () => { }, UiTheme.Red));
            recRow.Add(UiTheme.Btn("▶ Auto-solve (F1 agent)", () => { }, UiTheme.Green));
            driverBody.Add(recRow);

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
            exRow.Add(UiTheme.Btn("▼ STL (F9)", () => { }, UiTheme.Green));
            exRow.Add(UiTheme.Btn("▼ Waypoints (F10)", () => { }, UiTheme.Orange));
            taskBody.Add(exRow);
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
                var row = UiTheme.Row(); row.style.justifyContent = Justify.SpaceBetween;
                row.style.borderTopColor = UiTheme.Border; row.style.borderTopWidth = 1; row.style.paddingTop = 3; row.style.paddingBottom = 3;
                var swatch = new VisualElement(); swatch.style.width = 10; swatch.style.height = 10; swatch.style.marginRight = 6;
                swatch.style.backgroundColor = UiTheme.JointColors[i % UiTheme.JointColors.Length]; UiTheme.SetRadius(swatch, 2);
                var nameCell = UiTheme.Row(); nameCell.Add(swatch); nameCell.Add(UiTheme.Caption($"J{i} {arm.jointSpecs[i].name}"));
                var angle = UiTheme.Lbl("0.0°", UiTheme.Teal, 11); angle.name = $"jangle{i}"; angle.style.width = 64; angle.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Add(nameCell); row.Add(angle);
                _dbJointHost.Add(row);
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
                    var l = _dbJointHost.Q<Label>($"jangle{i}");
                    if (l != null) l.text = $"{ang[i]:F1}°";
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

        // ════════════════════════════ TRAINING VIEW ════════════════════════════
        Label _trGen, _trBest, _trMean, _trSuccess, _trBackend, _trObsTotal;
        Button _trStartBtn;
        VisualElement _trCurve;

        void BuildTrainingView()
        {
            VisualElement pipeBody, dashBody, obsBody;
            var pipe = ScrollPanel(UiTheme.PanelHeader("Intelligence Pipeline", UiTheme.Teal, "text → control"), out pipeBody);
            var dash = ScrollPanel(UiTheme.PanelHeader("Live Training Dashboard", UiTheme.Orange, "GA + Policy"), out dashBody);
            var obs  = ScrollPanel(UiTheme.PanelHeader("Observation Composition", UiTheme.Green), out obsBody);

            // -- pipeline (text -> plan -> skill -> control -> physics) --
            string[] stages = { "TEXT — natural-language instruction", "TASK PLAN — AgentCommands grammar",
                "SKILL — pick/place/reach coroutines", "CONTROL — DLS-IK → ServoModel 4096-tick",
                "PHYSICS — ArticulationBody + friction (emergent grasp)" };
            Color[] sc = { UiTheme.Teal, UiTheme.Teal, UiTheme.Text, UiTheme.Orange, UiTheme.Green };
            for (int i = 0; i < stages.Length; i++)
            {
                var node = UiTheme.Panel(sc[i]); var nb = new VisualElement(); UiTheme.Pad(nb, 8); node.Add(nb);
                nb.Add(UiTheme.Lbl(stages[i], sc[i], 11));
                pipeBody.Add(node);
                if (i < stages.Length - 1) { var arrow = UiTheme.Lbl("↓", UiTheme.Muted, 12); arrow.style.unityTextAlign = TextAnchor.MiddleCenter; pipeBody.Add(arrow); }
            }
            pipeBody.Add(UiTheme.Caption("Same plan runs in sim AND on the real arm — text is only correct at the plan level."));

            // -- dashboard --
            dashBody.Add(UiTheme.StatRow("BACKEND", "—", out _trBackend, UiTheme.Teal));
            dashBody.Add(UiTheme.StatRow("GENERATION", "0", out _trGen));
            dashBody.Add(UiTheme.StatRow("BEST FITNESS", "—", out _trBest, UiTheme.Orange));
            dashBody.Add(UiTheme.StatRow("POP MEAN", "—", out _trMean));
            dashBody.Add(UiTheme.StatRow("SUCCESS RATE", "—", out _trSuccess, UiTheme.Green));
            _trCurve = new VisualElement(); _trCurve.style.height = 80; _trCurve.style.backgroundColor = UiTheme.Card2; UiTheme.SetBorder(_trCurve, UiTheme.Border, 1); UiTheme.SetRadius(_trCurve, 4); _trCurve.style.marginTop = 6;
            _trCurve.generateVisualContent += DrawFitnessCurve;
            dashBody.Add(_trCurve);

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

            // -- observation composition (sensor toggles -> obs total) --
            obsBody.Add(UiTheme.Caption("Which sensor channels feed the policy this generation"));
            obsBody.Add(UiTheme.StatRow("OBS CHANNELS", "—", out _trObsTotal, UiTheme.Green));
            AddObsToggle(obsBody, "MotorEncoders", () => trainer.config.useMotorEncoders, v => trainer.config.useMotorEncoders = v);
            AddObsToggle(obsBody, "TaskState", () => trainer.config.useTaskState, v => trainer.config.useTaskState = v);
            AddObsToggle(obsBody, "IMU", () => trainer.config.useImu, v => trainer.config.useImu = v);
            AddObsToggle(obsBody, "RangeFinder", () => trainer.config.useRangeFinder, v => trainer.config.useRangeFinder = v);
            AddObsToggle(obsBody, "Lidar2D", () => trainer.config.useLidar, v => trainer.config.useLidar = v);
            AddObsToggle(obsBody, "DepthCamera", () => trainer.config.useDepthCamera, v => trainer.config.useDepthCamera = v);
            AddObsToggle(obsBody, "EFlesh Tactile", () => trainer.config.useTactile, v => trainer.config.useTactile = v);
            obsBody.Add(UiTheme.Caption("F = task_reward − λ₁·time − λ₂·energy − λ₃·collisions"));

            var cols = Columns(pipe, dash, obs); cols.style.flexGrow = 1;
            _content.Add(cols);
            _refresh = RefreshTraining;
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

        void RefreshTraining()
        {
            if (trainer == null) return;
            if (_trBackend != null) _trBackend.text = trainer.policyMode ? "Sensor-Policy (closed-loop)" : "Motion-GA (open-loop)";
            if (_trGen != null) _trGen.text = trainer.generation.ToString();
            if (_trBest != null) _trBest.text = trainer.lastBestFitness <= float.NegativeInfinity ? "—" : trainer.lastBestFitness.ToString("F2");
            if (_trMean != null) _trMean.text = trainer.lastMeanFitness.ToString("F2");
            if (_trSuccess != null) _trSuccess.text = $"{trainer.lastSuccessRate * 100f:F0}%";
            if (_trStartBtn != null) { _trStartBtn.text = trainer.Running ? "■ Stop (T)" : "▶ Start (T)"; UiTheme.SetActive(_trStartBtn, trainer.Running, UiTheme.Green); }
            if (_trObsTotal != null && sensorHub != null) _trObsTotal.text = sensorHub.BuildObservation().Length.ToString();
            if (_trCurve != null) _trCurve.MarkDirtyRepaint();
        }

        void DrawFitnessCurve(MeshGenerationContext ctx)
        {
            if (trainer == null) return;
            var hist = trainer.bestHistory;
            if (hist == null || hist.Count < 2) return;
            var p = ctx.painter2D;
            float w = _trCurve.contentRect.width, h = _trCurve.contentRect.height;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in hist) { if (v < min) min = v; if (v > max) max = v; }
            if (max - min < 0.01f) max = min + 1f;
            p.strokeColor = UiTheme.Green; p.lineWidth = 2f; p.BeginPath();
            for (int i = 0; i < hist.Count; i++)
            {
                float x = (i / (float)(hist.Count - 1)) * w;
                float y = h - Mathf.InverseLerp(min, max, hist[i]) * h;
                if (i == 0) p.MoveTo(new Vector2(x, y)); else p.LineTo(new Vector2(x, y));
            }
            p.Stroke();
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
                ("Nav tabs", "Menu · Dashboard · Training · Options · Help"),
                ("Esc", "back to Dashboard"),
                ("Shift+H", "this Help view"),
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
