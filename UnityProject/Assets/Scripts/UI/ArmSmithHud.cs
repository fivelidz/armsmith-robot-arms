// ArmSmithHud.cs
// UI Toolkit MonoBehaviour that wires ArmSmithUI.uxml + ArmSmithUI.uss to the live game objects.
// Attach to any GameObject in the scene.  Assign public fields from the Inspector or
// let GameBootstrap wire them via code (see summary comment at bottom for integration snippet).
//
// Unity version: 6000.4.2f1   C# 9

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArmSmith
{
    /// <summary>
    /// Runtime HUD for ARMSMITH — loads ArmSmithUI.uxml, queries named elements,
    /// wires button callbacks to the existing game API, and refreshes readout labels in Update.
    ///
    /// Public fields the GameBootstrap (or Inspector) must supply:
    ///   arm, controller, scenarios, trainer, recorder
    ///   uxmlAsset, ussAsset  (drag Assets/UI/ArmSmith*.* in Inspector, or set via code)
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ArmSmithHud : MonoBehaviour
    {
        // ──────────────────────────────────────────────────────────────────────────
        //  Public fields — assign from Inspector or GameBootstrap
        // ──────────────────────────────────────────────────────────────────────────
        [Header("Game Objects")]
        public ProceduralArm       arm;
        public ArmController       controller;
        public ScenarioManager     scenarios;
        public EvolutionTrainer    trainer;
        public BehaviourRecorder   recorder;

        [Header("UI Assets")]
        [Tooltip("Drag Assets/UI/ArmSmithUI.uxml here")]
        public VisualTreeAsset     uxmlAsset;
        [Tooltip("Drag Assets/UI/ArmSmithUI.uss here (optional — already referenced inside UXML)")]
        public StyleSheet          ussAsset;

        // ──────────────────────────────────────────────────────────────────────────
        //  Cached element references
        // ──────────────────────────────────────────────────────────────────────────

        // Top bar
        Button   _btnScenarioPrev, _btnScenarioNext;
        Label    _lblScenario;
        Button   _btnSimPlay, _btnSimReset;

        // Designer
        VisualElement _designerJoints;
        DropdownField _ddGripper;
        Label         _lblReach, _lblDof, _lblDofBadge;

        // Driver
        Button        _btnMode, _btnGripper;
        Label         _lblReward, _lblTime, _lblJoints;
        Button        _btnRecord, _btnPlay;
        Label         _lblRecIndicator;

        // Evolution
        VisualElement _evoPopulation;
        Label         _lblGeneration, _lblBest, _lblEvoStatus;
        Button        _btnTrain, _btnNextGen, _btnBreed;

        // Export
        Button        _btnExportStl, _btnExportWaypoints, _btnSendRobot;
        DropdownField _ddArmType;
        Label         _lblExportStatus;

        // Mode / viewport overlay
        Label         _lblModeDisplay;

        // ──────────────────────────────────────────────────────────────────────────
        //  State
        // ──────────────────────────────────────────────────────────────────────────
        readonly List<Button> _evoThumbs = new List<Button>();
        bool                  _trainingWasRunning;  // for Train button label toggle
        float                 _exportStatusTimer;

        // ──────────────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────────────────────────────────

        void Start()
        {
            var doc = GetComponent<UIDocument>();

            // Load UXML if assigned; otherwise rely on whatever is already on UIDocument
            if (uxmlAsset != null)
                doc.visualTreeAsset = uxmlAsset;

            if (doc.visualTreeAsset == null)
            {
                Debug.LogError("[ArmSmithHud] No VisualTreeAsset assigned. Drag ArmSmithUI.uxml to the uxmlAsset field.");
                return;
            }

            var root = doc.rootVisualElement;

            // Apply extra stylesheet if provided (UXML already references it via <Style>, but
            // supplying the asset here lets hot-reload work in the Editor too)
            if (ussAsset != null && !root.styleSheets.Contains(ussAsset))
                root.styleSheets.Add(ussAsset);

            QueryElements(root);
            WireButtons();
            BuildDesignerJoints();
            BuildEvoThumbnails();
        }

        void Update()
        {
            RefreshReadouts();

            // Fade export status message
            if (_exportStatusTimer > 0f)
            {
                _exportStatusTimer -= Time.deltaTime;
                if (_exportStatusTimer <= 0f && _lblExportStatus != null)
                    _lblExportStatus.text = "";
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Element querying
        // ──────────────────────────────────────────────────────────────────────────

        void QueryElements(VisualElement root)
        {
            // Top bar
            _btnScenarioPrev  = root.Q<Button>("btn-scenario-prev");
            _btnScenarioNext  = root.Q<Button>("btn-scenario-next");
            _lblScenario      = root.Q<Label> ("lbl-scenario");
            _btnSimPlay       = root.Q<Button>("btn-sim-play");
            _btnSimReset      = root.Q<Button>("btn-sim-reset");
            _lblModeDisplay   = root.Q<Label> ("lbl-mode-display");

            // Designer
            _designerJoints   = root.Q<VisualElement>("designer-joints");
            _ddGripper        = root.Q<DropdownField>("dd-gripper");
            _lblReach         = root.Q<Label>("lbl-reach");
            _lblDof           = root.Q<Label>("lbl-dof");
            _lblDofBadge      = root.Q<Label>("lbl-dof-badge");

            // Driver
            _btnMode          = root.Q<Button>("btn-mode");
            _btnGripper       = root.Q<Button>("btn-gripper");
            _lblReward        = root.Q<Label> ("lbl-reward");
            _lblTime          = root.Q<Label> ("lbl-time");
            _lblJoints        = root.Q<Label> ("lbl-joints");
            _btnRecord        = root.Q<Button>("btn-record");
            _btnPlay          = root.Q<Button>("btn-play");
            _lblRecIndicator  = root.Q<Label> ("lbl-rec-indicator");

            // Evolution
            _evoPopulation    = root.Q<VisualElement>("evo-population");
            _lblGeneration    = root.Q<Label> ("lbl-generation");
            _lblBest          = root.Q<Label> ("lbl-best");
            _lblEvoStatus     = root.Q<Label> ("lbl-evo-status");
            _btnTrain         = root.Q<Button>("btn-train");
            _btnNextGen       = root.Q<Button>("btn-nextgen");
            _btnBreed         = root.Q<Button>("btn-breed");

            // Export
            _btnExportStl       = root.Q<Button>       ("btn-export-stl");
            _btnExportWaypoints = root.Q<Button>       ("btn-export-waypoints");
            _btnSendRobot       = root.Q<Button>       ("btn-send-robot");
            _ddArmType          = root.Q<DropdownField>("dd-armtype");
            _lblExportStatus    = root.Q<Label>        ("lbl-export-status");

            // Warn on any missing element (helps catch typos early)
            WarnNull(_btnScenarioPrev,  "btn-scenario-prev");
            WarnNull(_btnScenarioNext,  "btn-scenario-next");
            WarnNull(_lblScenario,      "lbl-scenario");
            WarnNull(_designerJoints,   "designer-joints");
            WarnNull(_ddGripper,        "dd-gripper");
            WarnNull(_lblReach,         "lbl-reach");
            WarnNull(_lblDof,           "lbl-dof");
            WarnNull(_btnMode,          "btn-mode");
            WarnNull(_btnGripper,       "btn-gripper");
            WarnNull(_lblReward,        "lbl-reward");
            WarnNull(_lblTime,          "lbl-time");
            WarnNull(_lblJoints,        "lbl-joints");
            WarnNull(_btnRecord,        "btn-record");
            WarnNull(_btnPlay,          "btn-play");
            WarnNull(_evoPopulation,    "evo-population");
            WarnNull(_lblGeneration,    "lbl-generation");
            WarnNull(_lblBest,          "lbl-best");
            WarnNull(_btnTrain,         "btn-train");
            WarnNull(_btnNextGen,       "btn-nextgen");
            WarnNull(_btnBreed,         "btn-breed");
            WarnNull(_btnExportStl,     "btn-export-stl");
            WarnNull(_btnExportWaypoints,"btn-export-waypoints");
            WarnNull(_btnSendRobot,     "btn-send-robot");
            WarnNull(_ddArmType,        "dd-armtype");
        }

        static void WarnNull(object obj, string name)
        {
            if (obj == null)
                Debug.LogWarning($"[ArmSmithHud] Element not found in UXML: '{name}'");
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Button wiring
        // ──────────────────────────────────────────────────────────────────────────

        void WireButtons()
        {
            // ── Top bar ──────────────────────────────────────────────────────────

            _btnScenarioPrev?.RegisterCallback<ClickEvent>(_ =>
            {
                if (scenarios == null) return;
                // ScenarioManager.Cycle() is private; replicate the offset logic via LoadScenario
                int n = System.Enum.GetValues(typeof(ScenarioType)).Length;
                var next = (ScenarioType)(((int)scenarios.current - 1 + n) % n);
                scenarios.LoadScenario(next);
                UpdateScenarioLabel();
            });

            _btnScenarioNext?.RegisterCallback<ClickEvent>(_ =>
            {
                if (scenarios == null) return;
                int n = System.Enum.GetValues(typeof(ScenarioType)).Length;
                var next = (ScenarioType)(((int)scenarios.current + 1) % n);
                scenarios.LoadScenario(next);
                UpdateScenarioLabel();
            });

            _btnSimPlay?.RegisterCallback<ClickEvent>(_ =>
            {
                // Toggle sim pause — mirror what Enter does in GameBootstrap
                Time.timeScale = Time.timeScale > 0.01f ? 0f : 1f;
                if (_btnSimPlay != null)
                    _btnSimPlay.text = Time.timeScale > 0.01f ? "⏸ PAUSE" : "▶  PLAY";
            });

            _btnSimReset?.RegisterCallback<ClickEvent>(_ =>
            {
                scenarios?.LoadScenario(scenarios.current);
            });

            // ── Driver ──────────────────────────────────────────────────────────

            _btnMode?.RegisterCallback<ClickEvent>(_ =>
            {
                if (controller == null) return;
                controller.mode = controller.mode == ArmController.Mode.IK
                    ? ArmController.Mode.Manual
                    : ArmController.Mode.IK;
            });

            _btnGripper?.RegisterCallback<ClickEvent>(_ =>
            {
                // arm.gripper is the Gripper component; Toggle() is the correct public method
                arm?.gripper?.Toggle();
            });

            _btnRecord?.RegisterCallback<ClickEvent>(_ =>
            {
                if (recorder == null) return;
                if (recorder.IsRecording)
                    recorder.StopRecording();
                else
                    recorder.StartRecording();
            });

            _btnPlay?.RegisterCallback<ClickEvent>(_ =>
            {
                if (recorder == null) return;
                if (recorder.IsPlaying)
                    recorder.StopPlayback();
                else
                    recorder.StartPlayback();
            });

            // ── Evolution ────────────────────────────────────────────────────────

            _btnTrain?.RegisterCallback<ClickEvent>(_ =>
            {
                if (trainer == null) return;
                if (trainer.Running)
                    trainer.StopTraining();
                else
                    trainer.StartTraining();
            });

            _btnNextGen?.RegisterCallback<ClickEvent>(_ =>
            {
                if (trainer == null || trainer.Running) return;
                // RunGeneration returns IEnumerator; must be started via StartCoroutine
                StartCoroutine(trainer.RunGeneration());
            });

            _btnBreed?.RegisterCallback<ClickEvent>(_ =>
            {
                // Breed is private in EvolutionTrainer; the public surface is:
                // - trainer.playerSelectionMode (toggle to use selected parents)
                // - Start a new generation which will pick up selected set in Breed()
                if (trainer == null || trainer.Running) return;
                trainer.playerSelectionMode = trainer.selected.Count > 0;
                StartCoroutine(trainer.RunGeneration());
            });

            // ── Export ──────────────────────────────────────────────────────────

            _btnExportStl?.RegisterCallback<ClickEvent>(_ =>
            {
                if (arm == null) { ShowExportStatus("No arm — cannot export STL"); return; }
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "Exports");
                System.IO.Directory.CreateDirectory(dir);
                string armName = arm.config != null ? arm.config.armName : "Arm";
                string path = System.IO.Path.Combine(dir, $"{armName}.stl");
                StlExporter.ExportHierarchy(arm.transform, path);
                ShowExportStatus($"STL → {path}");
            });

            _btnExportWaypoints?.RegisterCallback<ClickEvent>(_ =>
            {
                if (recorder == null) { ShowExportStatus("No recorder"); return; }
                string path = recorder.StopRecording();  // writes JSON; returns null if nothing recorded
                if (path != null)
                    ShowExportStatus($"WP → {path}");
                else
                    ShowExportStatus("Nothing recorded yet (press ⏺ REC first)");
            });

            _btnSendRobot?.RegisterCallback<ClickEvent>(_ =>
            {
                // Placeholder: real implementation would call into a robot bridge script
                // (e.g. scripts/realbot/armsmith_player.py via UDP/socket).
                // For now, export best trajectory then log it.
                if (trainer?.best == null)
                {
                    ShowExportStatus("No evolved best yet — run training first");
                    return;
                }
                var traj = trainer.BestToTrajectory();
                if (traj != null && recorder != null)
                {
                    recorder.SetTrajectory(traj);
                    string path = recorder.StopRecording();
                    ShowExportStatus($"Exported best trajectory → {path}  (send manually via scripts/realbot/)");
                }
            });
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Designer joint rows  (built once on Start; refreshed in Update if DOF changes)
        // ──────────────────────────────────────────────────────────────────────────

        int _lastJointCount = -1;

        void BuildDesignerJoints()
        {
            if (_designerJoints == null || arm == null) return;
            _designerJoints.Clear();
            int n = arm.jointSpecs.Count;
            _lastJointCount = n;

            for (int i = 0; i < n; i++)
            {
                var js = arm.jointSpecs[i];
                var row = new VisualElement();
                row.AddToClassList("joint-row");

                // Header row: name left, type right
                var header = new VisualElement();
                header.AddToClassList("joint-row-header");
                var nameLbl = new Label($"J{i + 1}  {js.name}");
                nameLbl.AddToClassList("joint-name-label");
                var typeLbl = new Label(js.axis.ToString().ToLower());
                typeLbl.AddToClassList("joint-type-label");
                header.Add(nameLbl);
                header.Add(typeLbl);
                row.Add(header);

                // Data row: angle + torque  (names allow runtime Q lookup)
                var dataRow = new VisualElement();
                dataRow.style.flexDirection = FlexDirection.Row;
                dataRow.style.justifyContent = Justify.SpaceBetween;

                var angleLbl = new Label("0.0°");
                angleLbl.name = $"joint-angle-{i}";
                angleLbl.AddToClassList("joint-angle-label");

                var torqueLbl = new Label("—");
                torqueLbl.name = $"joint-torque-{i}";
                torqueLbl.AddToClassList("joint-torque-label");

                dataRow.Add(angleLbl);
                dataRow.Add(torqueLbl);
                row.Add(dataRow);

                _designerJoints.Add(row);
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Evolution population thumbnails
        // ──────────────────────────────────────────────────────────────────────────

        int _lastPopCount = -1;

        void BuildEvoThumbnails()
        {
            if (_evoPopulation == null || trainer == null) return;
            _evoPopulation.Clear();
            _evoThumbs.Clear();

            int n = trainer.population.Count;
            _lastPopCount = n;

            for (int i = 0; i < n; i++)
            {
                int idx = i; // capture for closure
                var g = trainer.population[i];

                var thumb = new Button();
                thumb.AddToClassList("evo-thumb");

                // Fitness label
                var fitLbl = new Label(FormatFitness(g.fitness));
                fitLbl.AddToClassList("evo-thumb-fitness");
                thumb.Add(fitLbl);

                // ID label
                var idLbl = new Label($"#{idx + 1}");
                idLbl.AddToClassList("evo-thumb-id");
                thumb.Add(idLbl);

                // Fitness bar
                var barBg = new VisualElement();
                barBg.AddToClassList("evo-fit-bar");
                var barFill = new VisualElement();
                barFill.name = $"evo-bar-fill-{idx}";
                barFill.AddToClassList("evo-fit-fill");
                barFill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
                barBg.Add(barFill);
                thumb.Add(barBg);

                // Click → toggle selection (EvolutionTrainer.ToggleSelect(i))
                thumb.RegisterCallback<ClickEvent>(_ =>
                {
                    trainer.ToggleSelect(idx);
                    RefreshEvoThumbSelection();
                });

                _evoThumbs.Add(thumb);
                _evoPopulation.Add(thumb);
            }
        }

        void RefreshEvoThumbSelection()
        {
            if (trainer == null) return;
            for (int i = 0; i < _evoThumbs.Count; i++)
            {
                bool sel = trainer.selected.Contains(i);
                if (sel)
                    _evoThumbs[i].AddToClassList("selected");
                else
                    _evoThumbs[i].RemoveFromClassList("selected");
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Update: refresh all readout labels every frame
        // ──────────────────────────────────────────────────────────────────────────

        void RefreshReadouts()
        {
            // ── Designer joint rows ──────────────────────────────────────────────
            if (arm != null)
            {
                // Rebuild rows if DOF changed (arm was rebuilt)
                if (arm.jointSpecs.Count != _lastJointCount)
                {
                    BuildDesignerJoints();
                    BuildEvoThumbnails();
                }

                float[] angles = arm.GetJointAngles();
                for (int i = 0; i < arm.jointSpecs.Count; i++)
                {
                    var angleLbl = _designerJoints?.Q<Label>($"joint-angle-{i}");
                    if (angleLbl != null)
                        angleLbl.text = $"{angles[i]:F1}°";
                }

                // Arm stats
                float reach = arm.config != null ? arm.config.TotalReach() : 0f;
                int dof     = arm.jointSpecs.Count;
                if (_lblReach   != null) _lblReach.text   = $"{reach:F2} m";
                if (_lblDof     != null) _lblDof.text     = dof.ToString();
                if (_lblDofBadge != null) _lblDofBadge.text = $"DOF: {dof}";
            }

            // ── Driver readouts ──────────────────────────────────────────────────
            if (controller != null && arm != null)
            {
                // Mode button label
                string modeStr = controller.mode == ArmController.Mode.IK ? "IK MODE" : "MANUAL";
                if (_btnMode != null)
                {
                    _btnMode.text = modeStr;
                    // Highlight orange when manual
                    if (controller.mode == ArmController.Mode.Manual)
                        _btnMode.AddToClassList("btn-active");
                    else
                        _btnMode.RemoveFromClassList("btn-active");
                }

                if (_lblModeDisplay != null)
                    _lblModeDisplay.text = controller.mode.ToString().ToUpper();

                // Gripper button label
                if (_btnGripper != null && arm.gripper != null)
                {
                    bool closed = arm.gripper.closeAmount > 0.5f;
                    _btnGripper.text = closed ? "GRIP ●" : "GRIP ○";
                    if (closed) _btnGripper.AddToClassList("btn-active");
                    else        _btnGripper.RemoveFromClassList("btn-active");
                }

                // Joint angles summary for lbl-joints
                if (_lblJoints != null && controller.TargetAngles != null)
                {
                    var sb = new System.Text.StringBuilder();
                    int n = controller.TargetAngles.Length;
                    for (int i = 0; i < n; i++)
                    {
                        sb.Append($"J{i+1}:{controller.TargetAngles[i]:F0}°");
                        if (i < n - 1) sb.Append("  ");
                    }
                    _lblJoints.text = sb.ToString();
                }
            }

            // Task reward & timer
            if (scenarios != null)
            {
                if (_lblReward != null)
                    _lblReward.text = $"{scenarios.LastReward:F2}";
                if (_lblTime   != null)
                    _lblTime.text   = $"{scenarios.Elapsed:F1}s";
                if (_lblScenario != null)
                    _lblScenario.text = ScenarioDisplayName(scenarios.current);
            }

            // Record / Play button states
            if (recorder != null)
            {
                if (_btnRecord != null)
                {
                    if (recorder.IsRecording)
                    {
                        _btnRecord.text = "⏹ STOP";
                        _btnRecord.AddToClassList("btn-active");
                    }
                    else
                    {
                        _btnRecord.text = "⏺ REC";
                        _btnRecord.RemoveFromClassList("btn-active");
                    }
                }

                if (_btnPlay != null)
                {
                    if (recorder.IsPlaying)
                    {
                        _btnPlay.text = "⏹ STOP";
                        _btnPlay.AddToClassList("btn-active");
                    }
                    else
                    {
                        _btnPlay.text = "▶ PLAY";
                        _btnPlay.RemoveFromClassList("btn-active");
                    }
                }

                if (_lblRecIndicator != null)
                    _lblRecIndicator.text = recorder.IsRecording ? "⏺ REC" : "";
            }

            // ── Evolution readouts ────────────────────────────────────────────────
            if (trainer != null)
            {
                if (_lblGeneration != null)
                    _lblGeneration.text = $"Gen {trainer.generation}";

                if (_lblBest != null)
                    _lblBest.text = trainer.best != null
                        ? $"Best: {trainer.best.fitness:F2}"
                        : "Best: —";

                if (_lblEvoStatus != null)
                    _lblEvoStatus.text = trainer.Running ? "RUNNING" : trainer.status;

                // Train button label
                if (_btnTrain != null)
                {
                    bool running = trainer.Running;
                    _btnTrain.text = running ? "⏹ STOP" : "▶ TRAIN";
                    if (running) _btnTrain.AddToClassList("btn-active");
                    else         _btnTrain.RemoveFromClassList("btn-active");
                }

                // Rebuild thumbnails if population size changed
                if (trainer.population.Count != _lastPopCount)
                    BuildEvoThumbnails();

                // Refresh fitness bars and labels
                float maxFit = float.NegativeInfinity;
                float minFit = float.PositiveInfinity;
                foreach (var g in trainer.population)
                {
                    if (g.fitness > float.NegativeInfinity) { if (g.fitness > maxFit) maxFit = g.fitness; }
                    if (g.fitness < float.PositiveInfinity) { if (g.fitness < minFit) minFit = g.fitness; }
                }
                float range = (maxFit - minFit);
                if (range < 0.0001f) range = 1f;

                for (int i = 0; i < _evoThumbs.Count && i < trainer.population.Count; i++)
                {
                    var g = trainer.population[i];
                    var thumb = _evoThumbs[i];

                    // Update fitness text (first Label child)
                    var fitLbl = thumb.Q<Label>(className: "evo-thumb-fitness");
                    if (fitLbl != null) fitLbl.text = FormatFitness(g.fitness);

                    // Update bar fill width
                    var fill = thumb.Q<VisualElement>($"evo-bar-fill-{i}");
                    if (fill != null)
                    {
                        float pct = (g.fitness <= float.NegativeInfinity) ? 0f
                                    : Mathf.Clamp01((g.fitness - minFit) / range) * 100f;
                        fill.style.width = new StyleLength(new Length(pct, LengthUnit.Percent));
                    }

                    // Champion styling for best genome
                    bool isChamp = (i == 0 && trainer.best != null);
                    if (isChamp) thumb.AddToClassList("champion");
                    else         thumb.RemoveFromClassList("champion");
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────────────────

        void UpdateScenarioLabel()
        {
            if (_lblScenario != null && scenarios != null)
                _lblScenario.text = ScenarioDisplayName(scenarios.current);
        }

        static string ScenarioDisplayName(ScenarioType t)
        {
            switch (t)
            {
                case ScenarioType.ReachTouch:    return "T0 · Reach & Touch";
                case ScenarioType.PushToZone:    return "T1 · Push to Zone";
                case ScenarioType.PickPlaceCube: return "T2 · Pick & Place";
                case ScenarioType.TrayToTray:    return "T3 · Tray to Tray";
                case ScenarioType.StackTwo:      return "T4 · Stack Cubes";
                case ScenarioType.DropInBin:     return "T5 · Drop in Bin";
                default:                         return t.ToString();
            }
        }

        static string FormatFitness(float f)
            => f <= float.NegativeInfinity ? "—" : f.ToString("F2");

        void ShowExportStatus(string msg)
        {
            if (_lblExportStatus != null)
                _lblExportStatus.text = msg;
            _exportStatusTimer = 5f;
            Debug.Log($"[ArmSmithHud] {msg}");
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Public convenience — GameBootstrap can call this after creating the HUD
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Wire all references in one call (alternative to setting public fields in Inspector).
        /// Call this from GameBootstrap.BuildHud() after creating the UIDocument + this component.
        /// </summary>
        public void Bind(ProceduralArm a, ArmController c, ScenarioManager s,
                         EvolutionTrainer t, BehaviourRecorder r)
        {
            arm        = a;
            controller = c;
            scenarios  = s;
            trainer    = t;
            recorder   = r;
        }
    }
}

/*
────────────────────────────────────────────────────────────────────────────────
  GameBootstrap integration snippet
  ─────────────────────────────────
  Add this to GameBootstrap.BuildHud() after creating the UGUI canvas:

        // ── UI Toolkit HUD ────────────────────────────────────────────────
        var hudGo  = new GameObject("ArmSmithHud");
        var uiDoc  = hudGo.AddComponent<UIDocument>();

        // Sort order: render above the uGUI canvas
        uiDoc.sortingOrder = 10;

        // Optional: set panel settings (create an instance via
        //   Assets > Create > UI Toolkit > Panel Settings Asset)
        // uiDoc.panelSettings = <your PanelSettings asset>;

        var hud = hudGo.AddComponent<ArmSmithHud>();

        // Assign UXML + USS via Resources folder:
        //   move ArmSmithUI.uxml + ArmSmithUI.uss to Assets/Resources/UI/
        //   then:
        // hud.uxmlAsset = Resources.Load<VisualTreeAsset>("UI/ArmSmithUI");
        // hud.ussAsset  = Resources.Load<StyleSheet>("UI/ArmSmithUI");

        // Or just drag them in the Inspector before hitting Play.

        hud.Bind(arm, controller, scenarios, trainer, recorder);
────────────────────────────────────────────────────────────────────────────────
*/
