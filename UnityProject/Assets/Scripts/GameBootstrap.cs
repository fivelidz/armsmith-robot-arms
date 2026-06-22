using UnityEngine;
using UnityEngine.UI;

namespace ArmSmith
{
    /// <summary>
    /// Builds the entire ARMSMITH workshop scene procedurally at runtime: environment (table, light,
    /// ground), the robot arm (ProceduralArm from ArmConfig), the pick-and-place task (cube + target),
    /// the multi-camera rig (main orbit + wrist + env), control + recorder, and a minimal HUD.
    ///
    /// Attach this to one empty GameObject in an otherwise empty scene and press Play.
    /// This lets the whole game run without hand-authoring the .unity scene, and is what the MCP
    /// bridge / CI can validate.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public ArmConfig config;
        // Use the REAL SO-101 STL arm (URDF-accurate frames + downloaded meshes) as the default model.
        // The new per-servo keyboard controls (T/G, Y/H, ...) drive it directly without needing IK.
        public bool useRealStlMeshes = true;
        ProceduralArm arm;
        ArmController controller;
        CameraRig rig;
        ScenarioManager scenarios;
        ArmGizmos gizmos;
        BehaviourRecorder recorder;
        EvolutionTrainer trainer;
        AgentCommands agent;
        SensorHub sensorHub;
        ArmSmith.Modules.AttachmentSystem attachmentSystem;
        ArmSmith.Modules.MountNodeViz mountNodeViz;
        SensorViz sensorViz;
        MouseInteraction mouse;
        ServoPanel servoPanel;
        ModuleUsagePanel modulePanel;
        ArmSmith.Verification.VerificationPanel verificationPanel;
        GripDetector gripDetector;
        WorkspaceMap workspaceMap;
        ModuleMount moduleMount;
        // Path visualization (S7d)
        ArmSmith.Visualization.PathVisualizer pathViz;
        ArmSmith.Visualization.MultiGenViz multiGenViz;
        ArmSmith.Visualization.DiffusionPathDemo diffDemo;
        ArmSmith.Visualization.DenoisePathDemo denoiseDemo;
        ArmSmith.Visualization.DiffusionMotionPlanner mpdPlanner;
        ArmSmith.Visualization.PlannedPathFollower pathFollower;
        ArmSmith.DiffusionPolicyClient policyClient;
        ArmSmith.Visualization.TrajectorySample executedPath;
        float execTrailTimer;
        ServoCallouts servoCallouts;
        ScenarioMenu scenarioMenu;
        BuilderPanel builderPanel;
        ControlBar controlBar;
        CommandConsole commandConsole;
        ArmSmith.UI.UiManager uiManager;
        DemoRecorder demoRec;
        SequenceEditor sequence;
        SaveSystem saveSystem;
        Transform ikTarget;

        // HUD
        Text infoText;

        void Start()
        {
            // PhysX stability (S7d): 24/8 solver iterations combined with very stiff drives on light
            // links drove the articulation state to NaN, segfaulting PhysX in PxsSolverStartTask::
            // setupDescTask on the next Simulate (verified crash on AMD/Mesa Linux). Sane iteration
            // counts + lower stiffness (see UrdfArm) keep the solver numerically stable.
            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 2;
            Time.fixedDeltaTime = 1f / 120f;               // 120 Hz physics for stable articulation
            Time.maximumDeltaTime = 0.05f;                 // cap substep pile-up so timescale spikes can't blow up the solver

            // ArmConfig is [Serializable] (plain class) so Unity never leaves it null — it
            // deserializes a default instance with an EMPTY joints list. Guard on joint count.
            if (config == null || config.joints == null || config.joints.Count == 0)
                config = ArmConfig.CreateStarter();

            BuildEnvironment();
            BuildArm();
            BuildScenarios();
            BuildCameras();   // controller.Bind() sets the home pose here
            BuildSensors();   // pluggable sensor modules (needs wrist cam from BuildCameras)
            BuildTrainer();   // capture home pose AFTER Bind
            BuildHud();

            // S7d: do the arm-vs-static-environment collision ignore AFTER everything is built (worktop,
            // walls, scenario props all exist now), so no overlapping static collider is missed before the
            // first physics step depenetrates it into a PhysX NaN crash.
            if (arm != null) IgnoreArmVsEnvironment(arm);
        }

        void BuildSensors()
        {
            var go = new GameObject("SensorHub");
            sensorHub = go.AddComponent<SensorHub>();
            sensorHub.Init(arm, rig);

            sensorViz = go.AddComponent<SensorViz>();
            sensorViz.Bind(sensorHub);

            // KSP-style 3D ATTACHMENT system: snap camera/sensor/structural parts onto the arm's links.
            attachmentSystem = go.AddComponent<ArmSmith.Modules.AttachmentSystem>();
            attachmentSystem.Bind(arm, moduleMount, sensorHub, Mat);

            // in-scene attach-node markers (shown while building)
            mountNodeViz = go.AddComponent<ArmSmith.Modules.MountNodeViz>();
            mountNodeViz.Bind(arm, moduleMount, Mat);
        }

        void BuildTrainer()
        {
            var trGo = new GameObject("EvolutionTrainer");
            trainer = trGo.AddComponent<EvolutionTrainer>();
            trainer.Init(arm, controller, scenarios);
            trainer.SetSensorHub(sensorHub);   // closed-loop policy training uses sensor observations

            var agGo = new GameObject("AgentCommands");
            agent = agGo.AddComponent<AgentCommands>();
            agent.Bind(controller, arm, scenarios, trainer, ikTarget);

            var miGo = new GameObject("MouseInteraction");
            mouse = miGo.AddComponent<MouseInteraction>();
            mouse.Bind(controller, arm, ikTarget, rig.mainCam, recorder);

            var demoGo = new GameObject("DemoRecorder");
            demoRec = demoGo.AddComponent<DemoRecorder>();
            demoRec.Bind(arm, controller, scenarios, sensorHub);

            var seqGo = new GameObject("SequenceEditor");
            sequence = seqGo.AddComponent<SequenceEditor>();
            sequence.Bind(arm, controller);

            var saveGo = new GameObject("SaveSystem");
            saveSystem = saveGo.AddComponent<SaveSystem>();
            saveSystem.Bind(arm, controller, scenarios, sensorHub, sequence, trainer);
            saveSystem.attachments = attachmentSystem;
        }

        void BuildScenarios()
        {
            var go = new GameObject("ScenarioManager");
            scenarios = go.AddComponent<ScenarioManager>();
            scenarios.Init(arm, controller, () => Mat(Color.white));

            gizmos = arm.gameObject.AddComponent<ArmGizmos>();
            gizmos.arm = arm; gizmos.ikTarget = ikTarget;
        }

        // Workstation constants (metres). Worktop surface at y=0; arm base sits on it.
        const float TableW = 0.9f, TableD = 0.7f, TableThick = 0.04f, TableCenterZ = 0.30f, FloorY = -0.75f;

        void BuildEnvironment()
        {
            // Two-light setup for a clean workshop look.
            var sun = new GameObject("Sun");
            var l = sun.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = 1.0f; l.color = new Color(1f, 0.97f, 0.9f);
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            l.shadows = LightShadows.Soft;

            var fill = new GameObject("Fill");
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Directional; fl.intensity = 0.35f; fl.color = new Color(0.7f, 0.8f, 1f);
            fill.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.40f);

            // Floor (well below the worktop, no z-fight).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, FloorY, TableCenterZ);
            floor.transform.localScale = Vector3.one * 1.0f;
            floor.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.14f, 0.15f, 0.18f));

            // Back + side walls (workshop room).
            MakeWall("WallBack",  new Vector3(0f, FloorY + 0.75f, TableCenterZ + 0.9f), new Vector3(4f, 3f, 0.1f), new Color(0.22f, 0.24f, 0.28f));
            MakeWall("WallLeft",  new Vector3(-1.5f, FloorY + 0.75f, TableCenterZ), new Vector3(0.1f, 3f, 3.5f), new Color(0.20f, 0.22f, 0.26f));

            // Worktop: a solid table whose TOP is exactly y=0 (the arm's work plane).
            var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "Worktop";
            top.transform.position = new Vector3(0f, -TableThick * 0.5f, TableCenterZ);
            top.transform.localScale = new Vector3(TableW, TableThick, TableD);
            top.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.62f, 0.47f, 0.33f));

            // Four table legs.
            float lx = TableW * 0.5f - 0.06f, lz = TableD * 0.5f - 0.06f, legH = -FloorY - TableThick;
            MakeLeg(new Vector3(lx, FloorY + legH * 0.5f, TableCenterZ + lz), legH);
            MakeLeg(new Vector3(-lx, FloorY + legH * 0.5f, TableCenterZ + lz), legH);
            MakeLeg(new Vector3(lx, FloorY + legH * 0.5f, TableCenterZ - lz), legH);
            MakeLeg(new Vector3(-lx, FloorY + legH * 0.5f, TableCenterZ - lz), legH);
        }

        void MakeWall(string name, Vector3 pos, Vector3 scale, Color c)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = name; w.transform.position = pos; w.transform.localScale = scale;
            w.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
        }

        void MakeLeg(Vector3 center, float height)
        {
            var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leg.name = "Leg"; leg.transform.position = center;
            leg.transform.localScale = new Vector3(0.05f, height, 0.05f);
            leg.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.3f, 0.3f, 0.33f));
        }

        void BuildArm()
        {
            var armGo = new GameObject("Arm");
            armGo.transform.position = Vector3.zero;       // base on table worktop (y=0)
            arm = armGo.AddComponent<ProceduralArm>();

            if (useRealStlMeshes)
            {
                // OPTION B: use the WORKING procedural kinematics (correct joints + IK + grasp across the
                // REALISTIC SO-101: build the arm from the REAL URDF kinematics (real servo joint frames,
                // axes, limits) AND mount the real STL meshes. This is the authentic digital twin. The
                // joint-axis conversion is being fixed methodically (UrdfArm.cs) so each servo rotates
                // exactly like the real motor.
                string kinPath = System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                arm.BuildFromKinematics(kinPath);
                if (arm.baseBody == null)  // safety fallback if kinematics.json missing
                {
                    Debug.LogWarning("[GameBootstrap] BuildFromKinematics failed; falling back to procedural.");
                    config = ArmConfig.CreateStarter(); arm.useStlMeshes = false; arm.Build(config);
                }
            }
            else
            {
                arm.useStlMeshes = false;   // procedural visuals only
                arm.Build(config);
            }

            // Enable self-collision so the arm can't pass through itself (non-adjacent links collide).
            var selfCol = armGo.AddComponent<SelfCollision>();
            selfCol.Setup(arm);


            // Reachable-workspace map (Shift+\ to compute+show): green=reachable, red=unreachable cells.
            workspaceMap = armGo.AddComponent<WorkspaceMap>();

            // Module-mounting system: exposes mount sockets on the arm; modules can be placed/oriented.
            moduleMount = armGo.AddComponent<ModuleMount>();
            moduleMount.Setup(arm);
            if (attachmentSystem != null) attachmentSystem.mount = moduleMount;   // late-bind the sockets
            if (mountNodeViz != null) mountNodeViz.mount = moduleMount;

            // Multi-robot foundation: publish this arm's state to the shared WorldBlackboard so future
            // additional arms can coordinate (hand-offs, do-not-collide, collaborative tasks).
            var agentRobot = armGo.AddComponent<RobotAgent>();
            agentRobot.Bind("arm1", arm);

            // IK target gizmo
            var tgt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tgt.name = "IKTarget";
            tgt.transform.localScale = Vector3.one * 0.03f;
            Destroy(tgt.GetComponent<Collider>());
            tgt.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.2f, 0.9f, 0.3f, 0.6f));
            ikTarget = tgt.transform;

            controller = armGo.AddComponent<ArmController>();

            recorder = armGo.AddComponent<BehaviourRecorder>();
            recorder.controller = controller;
            recorder.arm = arm;

            workspaceMap.Bind(controller, arm);   // now that the controller exists

            // ── In-world PATH VISUALIZATION (S7d) ──────────────────────────────────────
            // Draw trajectories in 3D: live IK preview, multimodal "diffusion" candidate paths, a
            // denoising animation, and the executed tip trail. The visualizer is source-agnostic
            // (ITrajectoryProvider), so the real diffusion/MPD planner output will plug straight in.
            pathViz = armGo.AddComponent<ArmSmith.Visualization.PathVisualizer>();

            var ikProv = armGo.AddComponent<ArmSmith.Visualization.IKPathProvider>();
            ikProv.controller = controller; ikProv.arm = arm;
            pathViz.Register(ikProv);

            // Multimodal candidate-path demo (stand-in for the diffusion planner until DF5 is wired).
            var diffGo = new GameObject("DiffusionPathDemo");
            diffGo.transform.SetParent(armGo.transform, false);
            diffDemo = diffGo.AddComponent<ArmSmith.Visualization.DiffusionPathDemo>();
            diffDemo.vizEnabled = false;          // off by default; toggle with the 'P' key (see Update)
            pathViz.Register(diffDemo);

            // Denoising explainer animation (off by default; toggle with '7').
            var denGo = new GameObject("DenoisePathDemo");
            denGo.transform.SetParent(armGo.transform, false);
            denoiseDemo = denGo.AddComponent<ArmSmith.Visualization.DenoisePathDemo>();
            denoiseDemo.vizEnabled = false;
            pathViz.Register(denoiseDemo);

            // REAL MPD-style diffusion motion planner (collision-free multimodal paths from scene obstacles).
            // Off by default; toggle with '6'. This is the actual planner (vs DiffusionPathDemo's stand-in).
            var mpdGo = new GameObject("DiffusionMotionPlanner");
            mpdGo.transform.SetParent(armGo.transform, false);
            mpdPlanner = mpdGo.AddComponent<ArmSmith.Visualization.DiffusionMotionPlanner>();
            mpdPlanner.vizEnabled = false;
            pathViz.Register(mpdPlanner);

            // PLAN -> MOTION: follows the planner's chosen collision-free path with the arm (key 5).
            pathFollower = armGo.AddComponent<ArmSmith.Visualization.PlannedPathFollower>();
            pathFollower.controller = controller; pathFollower.arm = arm; pathFollower.planner = mpdPlanner;

            // DEPLOY a trained Diffusion Policy (DF4): connects to the Python inference server and drives
            // the arm receding-horizon. Toggle with key 4 (start the server first:
            // scripts/diffusion/serve_diffusion_policy.py ckpt.pt). Off until toggled.
            policyClient = armGo.AddComponent<ArmSmith.DiffusionPolicyClient>();
            policyClient.controller = controller; policyClient.arm = arm;

            // Executed-tip trail accumulator.
            executedPath = new ArmSmith.Visualization.TrajectorySample { label = "executed" };
            pathViz.SetExecuted(executedPath);
        }

        // Tray-to-tray scenario (see research/manipulation_repos/TEST_ENVIRONMENTS.md).
        void BuildCameras()
        {
            var rigGo = new GameObject("CameraRig");
            rig = rigGo.AddComponent<CameraRig>();

            rig.mainCam = MakeCam("MainCam", true);
            rig.wristCam = MakeCam("WristCam", false);
            rig.envCam = MakeCam("EnvCam", false);

            controller.Bind(arm, ikTarget, rig.mainCam);

            // When using the real URDF arm, override the home pose set by Bind() (which was
            // designed for the procedural arm geometry). For the URDF arm, theta=0 for all
            // joints is already a good "arm-up, reaching-forward" home pose (EE at ~z=0.32).
            // Also move the IK target to where the EE actually is at theta=0 so the first
            // IK frame is stable (not immediately driving the arm to an extreme config).
            if (useRealStlMeshes && arm.baseBody != null)
            {
                var homeDeg = new float[arm.jointBodies.Count]; // all zeros
                arm.SeedServoState(homeDeg);
                arm.SetJointTargets(homeDeg);
                controller.SetTargets(homeDeg);
                // Place IK target at the natural home EE position so IK starts stable.
                if (ikTarget != null && arm.endEffector != null)
                    ikTarget.position = arm.endEffector.position;
            }
            // rig.Setup is called once after the HUD panels exist (see BuildHud).
        }

        static readonly Vector3 EnvCamPos = new Vector3(0f, 0.55f, -0.45f);
        static readonly Vector3 EnvCamLook = new Vector3(0f, 0.05f, 0.34f);

        Camera MakeCam(string name, bool main)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Camera>();
            if (main) { c.tag = "MainCamera"; go.AddComponent<AudioListener>(); }
            c.clearFlags = CameraClearFlags.Skybox;
            c.backgroundColor = new Color(0.1f, 0.12f, 0.16f);
            return c;
        }

        void BuildHud()
        {
            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // UI SCALING: previously the CanvasScaler was added but never configured -> it defaulted to
            // ConstantPixelSize (scaleFactor 1). On a 2560x1440 display that left every panel/font at the
            // raw pixel size tuned for ~1280-1920, so text looked tiny and absolute offsets didn't track the
            // larger screen. Scale With Screen Size at a 1920x1080 reference makes panels + fonts scale up
            // proportionally on high-res displays (and down on small ones). Match-width-or-height 0.5 keeps a
            // balanced scale for both axes.
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // wrist + env panels (top-right corner)
            rig.wristPanel = MakePanel(canvasGo.transform, "WristPanel", new Vector2(-10, -10), new Vector2(200, 200), new Vector2(1, 1));
            rig.envPanel   = MakePanel(canvasGo.transform, "EnvPanel",   new Vector2(-10, -220), new Vector2(200, 150), new Vector2(1, 1));
            // Now that panels exist, set up the cameras + RenderTextures and bind them to the panels.
            // Pass the JAW transforms + the gripper body so the wrist cam frames BOTH jaws and looks OUT
            // past them (the grasp basis is derived from the jaw geometry, not the twisted EE local frame).
            Transform gripperBody = arm.endEffector != null ? arm.endEffector.parent : null;
            Transform jawA = arm.leftJaw != null ? arm.leftJaw.transform : null;
            Transform jawB = arm.rightJaw != null ? arm.rightJaw.transform
                            : (arm.fixedJawTf != null ? arm.fixedJawTf : null);   // fixed jaw is a plain collider now
            rig.Setup(arm.endEffector, jawA, jawB, gripperBody, EnvCamPos, EnvCamLook);

            // info text (top-left) — legible: larger font, dark backing panel, outline.
            var infoBgGo = new GameObject("InfoBg");
            infoBgGo.transform.SetParent(canvasGo.transform, false);
            var infoBg = infoBgGo.AddComponent<Image>();
            infoBg.color = new Color(0.04f, 0.05f, 0.07f, 0.78f);
            var bgrt = infoBg.rectTransform;
            bgrt.anchorMin = new Vector2(0, 1); bgrt.anchorMax = new Vector2(0, 1); bgrt.pivot = new Vector2(0, 1);
            bgrt.anchoredPosition = new Vector2(8, -8);
            bgrt.sizeDelta = new Vector2(720, 250);

            var txtGo = new GameObject("Info");
            txtGo.transform.SetParent(infoBgGo.transform, false);
            infoText = txtGo.AddComponent<Text>();
            infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            infoText.fontSize = 17;
            infoText.fontStyle = FontStyle.Bold;
            infoText.color = Color.white;
            infoText.lineSpacing = 1.05f;
            var outline = txtGo.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.9f);
            outline.effectDistance = new Vector2(1.2f, -1.2f);
            var rt = infoText.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10, 8); rt.offsetMax = new Vector2(-10, -8);

            // Servo motor values panel (bottom-left): per-joint angle -> tick, target, bar.
            servoPanel = canvasGo.AddComponent<ServoPanel>();
            servoPanel.Build(canvasGo.transform, arm, controller);

            // Sensor-module usage panel (right): live outputs + USED-IN-TRAINING indicator.
            modulePanel = canvasGo.AddComponent<ModuleUsagePanel>();
            modulePanel.Build(canvasGo.transform, sensorHub, trainer);

            // Placement-verification panel (right): checks base fastened, links connected, no penetration.
            verificationPanel = canvasGo.AddComponent<ArmSmith.Verification.VerificationPanel>();
            verificationPanel.Build(canvasGo.transform, arm, GameObject.Find("Worktop") != null ? GameObject.Find("Worktop").transform : null);

            // Grip-detection feedback (only revealed when the EFleshTactile module is enabled): highlights
            // a graspable object in range + shows GRIP READY, so objects are easier to pick up.
            gripDetector = canvasGo.AddComponent<GripDetector>();
            gripDetector.Bind(arm, sensorHub, canvasGo.transform);

            // Clickable servo callouts: click a joint's yellow hotspot -> leader line + command panel.
            servoCallouts = canvasGo.AddComponent<ServoCallouts>();
            servoCallouts.Build(arm, controller, rig.mainCam, canvas);

            // Scenario selection menu (top-center): clickable list of scenarios + active objective.
            scenarioMenu = canvasGo.AddComponent<ScenarioMenu>();
            scenarioMenu.Build(canvasGo.transform, scenarios);

            // Robot-arm BUILDER panel (left dock): arm stats + module toggles + training/generations view.
            builderPanel = canvasGo.AddComponent<BuilderPanel>();
            builderPanel.Build(canvasGo.transform, arm, sensorHub, trainer);

            // TRAINING panel (F3): backend selector + live curves + start/stop/step.
            // CONDITIONS panel (F4): difficulty/randomization/reward weights/sensor toggles/GA params.
            var trainingPanel = canvasGo.AddComponent<TrainingPanel>();
            trainingPanel.Bind(trainer);
            var conditionsPanel = canvasGo.AddComponent<ConditionsPanel>();
            conditionsPanel.Bind(trainer, scenarios);

            // GENERATIONS & CREATIONS panel (F7): browse evolving generations + best creations, replay a
            // saved creation in-scene, save/load training checkpoints, lock survivors (interactive evolution).
            var generationsPanel = canvasGo.AddComponent<GenerationsPanel>();
            generationsPanel.Bind(trainer);

            // MULTI-GENERATION viz (key 3): overlay the last few generations' best paths (newest bright).
            if (pathViz != null && arm != null)
            {
                multiGenViz = arm.gameObject.AddComponent<ArmSmith.Visualization.MultiGenViz>();
                multiGenViz.trainer = trainer;
                pathViz.Register(multiGenViz);
            }

            // Clickable CONTROL BAR (bottom-center): view + control toggle buttons (mouse-operable).
            controlBar = canvasGo.AddComponent<ControlBar>();
            controlBar.Build(canvasGo.transform, controller, arm, sensorViz, gizmos, rig, servoCallouts, trainer, scenarios);

            // Live TEXT COMMAND console: type robot commands (AgentCommands grammar) -> execute.
            commandConsole = canvasGo.AddComponent<CommandConsole>();
            commandConsole.Build(canvasGo.transform, agent);

            // ── UNIFIED UI TOOLKIT INTERFACE (F1) ─────────────────────────────────────────────────────
            // The incorporated, designed interface (robotics-console look from design/ui_html/): a single
            // overlay with top nav + live status bar + switchable views (Menu/Dashboard/Training/Options/
            // Help). Runtime UIDocument (PanelSettings built in code by UiTheme — no asset authoring).
            // Additive: legacy panels above keep working; press F1 to toggle this overlay.
            var uiGo = new GameObject("ArmSmithInterface");
            var uiDoc = uiGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            uiDoc.panelSettings = ArmSmith.UI.UiTheme.GetPanelSettings();
            uiDoc.sortingOrder = 20;   // above the uGUI canvas
            uiManager = uiGo.AddComponent<ArmSmith.UI.UiManager>();
            uiManager.Bind(arm, controller, scenarios, trainer, sensorHub, recorder, agent, moduleMount, saveSystem, attachmentSystem, mountNodeViz, rig);
            uiManager.legacyHud = canvasGo;   // hidden while the new interface overlay is up (F1 to swap)
            uiManager.visible = false;   // start hidden; legacy HUD is the default, F1 reveals the new UI

            // ── SP1: SENSOR-ONLY TELEOP overlay (Shift+S) — operate from sensor data only ───────────────
            var soGo = new GameObject("SensorOnlyMode");
            var soDoc = soGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            soDoc.panelSettings = ArmSmith.UI.UiTheme.GetPanelSettings();
            soDoc.sortingOrder = 30;   // above everything when active
            var sensorOnly = soGo.AddComponent<ArmSmith.UI.SensorOnlyMode>();
            sensorOnly.Bind(sensorHub, controller, arm);
        }

        RawImage MakePanel(Transform parent, string name, Vector2 pos, Vector2 size, Vector2 anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ri = go.AddComponent<RawImage>();
            var rt = ri.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return ri;
        }

        void Update()
        {
            // ── Path-visualization controls (S7d) ──────────────────────────────────────
            // 8 = toggle all path viz, 9 = toggle diffusion multimodal demo, 0 = toggle denoise demo.
            if (Input.GetKeyDown(KeyCode.Alpha8) && pathViz != null) pathViz.show = !pathViz.show;
            if (Input.GetKeyDown(KeyCode.Alpha9) && diffDemo != null) diffDemo.vizEnabled = !diffDemo.vizEnabled;
            if (Input.GetKeyDown(KeyCode.Alpha7) && denoiseDemo != null) denoiseDemo.vizEnabled = !denoiseDemo.vizEnabled;
            if (Input.GetKeyDown(KeyCode.Alpha6) && mpdPlanner != null)
            {
                mpdPlanner.vizEnabled = !mpdPlanner.vizEnabled; mpdPlanner.ReplanNow();
                // show/hide the obstacle rings the planner is routing around (PV4)
                if (pathViz != null) pathViz.SetObstacleField(mpdPlanner.vizEnabled ? mpdPlanner.Field : null);
            }
            if (Input.GetKeyDown(KeyCode.Alpha5) && pathFollower != null) { if (mpdPlanner != null) mpdPlanner.vizEnabled = true; pathFollower.Begin(); }
            if (Input.GetKeyDown(KeyCode.Alpha4) && policyClient != null) { if (policyClient.Running) policyClient.Stop(); else policyClient.Begin(); }
            if (Input.GetKeyDown(KeyCode.Alpha3) && multiGenViz != null) multiGenViz.vizEnabled = !multiGenViz.vizEnabled;
            // Accumulate the executed tip trail (every ~30 ms, capped length).
            if (executedPath != null && arm != null && arm.endEffector != null)
            {
                execTrailTimer += Time.deltaTime;
                if (execTrailTimer >= 0.03f)
                {
                    execTrailTimer = 0f;
                    var p = arm.endEffector.position;
                    if (executedPath.points.Count == 0 ||
                        Vector3.Distance(executedPath.points[executedPath.points.Count - 1], p) > 0.004f)
                    {
                        executedPath.points.Add(p);
                        if (executedPath.points.Count > 240) executedPath.points.RemoveAt(0);
                    }
                }
            }

            if (infoText == null) return;
            string mode = controller.mode.ToString();
            string grip = arm.gripper != null ? (arm.gripper.closeAmount > 0.5f ? "CLOSED" : "OPEN") : "-";
            string rec = recorder.IsRecording ? " [REC]" : recorder.IsPlaying ? " [PLAY]" : "";
            infoText.text =
                $"<b>ARMSMITH</b> — {config.armName}  ({arm.jointBodies.Count} DOF){rec}\n" +
                $"Mode: {mode} (Tab) | Claw: {grip}  (, open  . close  Space toggle | N/B rotate claw)\n" +
                $"<color=#fd6>FLY the green target:</color> WASD move, Q/E up-down (drives the claw via IK)\n" +
                $"<color=#9f9>Z/Home</color> calibrate to zero | <color=#9f9>Enter</color> {(controller.paused ? "<color=#f99>PAUSED (Enter=resume->move)</color>" : "pause")} | speed x{controller.speedScale:F1} (Shift+,/.)\n" +
                $"Mouse follow: {(controller.mouseFollow ? "<color=#6f6>ON</color>" : "<color=#f66>OFF</color>")} (M toggle) | depth scroll | dbl-click grab/place\n" +
                $"<color=#fc6>Servos</color> (direct keys): {ServoControlLine()}\n" +
                $"Camera: RMB orbit, MMB pan, Ctrl+scroll zoom | V HUD, B bounds, X axes | \\ servo callouts (click a joint)\n" +
                $"Record waypoints G | Playback P | Reset Esc | STL F9 / waypoints F10 | DEMO {(demoRec.IsRecording ? "REC " + demoRec.StepCount : "Backspace")} | <color=#9f9>Ctrl+S save / Ctrl+L load</color>\n" +
                $"Scenario: <b>{scenarios.current}</b>  (Tab... no — keys 1-7 pick) | Evolve: T train, N +1 gen, F11 export best(+GA demo)\n" +
                $"<color=#9cf>Path viz:</color> 8 toggle | 6 MPD planner | 5 FOLLOW plan | 4 diffusion-policy | 3 generations{(policyClient != null && policyClient.Running ? " <color=#6f6>LIVE</color>" : "")} | 9 routes | 7 denoise\n" +
                $"<color=#cdf>Sequence:</color> K capture pt ({sequence.Count}), J play{(sequence.Playing ? " <color=#6f6>[PLAYING " + (sequence.PlayIndex + 1) + "]</color>" : "")}, Shift+Backspace del, F6... export\n" +
                $"<color=#fd8>OBJECTIVE:</color> {scenarios.Objective()}\n" +
                $"<color=#8c8>{scenarios.RewardSpec()}</color>\n" +
                $"Reward: {scenarios.LastReward:F2}   Time: {scenarios.Elapsed:F1}s   " +
                $"{(scenarios.Succeeded ? "<color=#4f4>SUCCESS</color>" : "")}\n" +
                $"<color=#fa6>Servo bus (ticks):</color> {arm.ServoBusString()}\n" +
                $"<color=#9cf>Sensors</color> (F2-F7 toggle): {sensorHub.Summary()}\n" +
                $"<color=#6cf>Sensor views</color> (independent): L lidar, ' depth, Shift+L range  [{sensorViz.Status()}]\n" +
                $"<color=#fc6>SIM SPEED: {Time.timeScale:F1}x real time</color> (+/- adjust, 0 pause) | " +
                $"servo max ~300\u00b0/s \u2014 <color=#999>motors are NOT instant; at {Time.timeScale:F1}x a real {(1f / Mathf.Max(0.1f, Time.timeScale)):F2}s move looks 1s here</color>\n" +
                $"<color=#7cf>Evolution</color> [{(trainer.policyMode ? "POLICY/sensors" : "motion")}] (F8 toggle) gen {trainer.generation}  " +
                $"best {(trainer.policyMode ? (trainer.bestPolicy != null ? trainer.bestPolicy.fitness.ToString("F2") : "-") : (trainer.best != null ? trainer.best.fitness.ToString("F2") : "-"))}  [{trainer.status}]";

            // Sim speed control (the sim can run faster than real time; real motors are slower).
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                Time.timeScale = Mathf.Min(8f, Time.timeScale + 0.5f);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                Time.timeScale = Mathf.Max(0f, Time.timeScale - 0.5f);
            if (Input.GetKeyDown(KeyCode.Alpha0)) Time.timeScale = (Time.timeScale == 0f) ? 1f : 0f;

            // Sensor module toggles (ablation) moved to SHIFT+F2..F7 so the bare F-keys are free for the UI
            // panels. The plain F3/F4 used to fire BOTH a sensor toggle and a panel (Training/Conditions) at
            // once — a real keybinding clash. Ablation is an advanced action, so it lives behind Shift now.
            //   Shift+F2 IMU · Shift+F3 RangeFinder · Shift+F4 Lidar2D · Shift+F5 DepthCamera · Shift+F6 eFlesh · Shift+F7 MotorEncoders
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld && Input.GetKeyDown(KeyCode.F2)) Toggle("IMU");
            if (shiftHeld && Input.GetKeyDown(KeyCode.F3)) Toggle("RangeFinder");
            if (shiftHeld && Input.GetKeyDown(KeyCode.F4)) Toggle("Lidar2D");
            if (shiftHeld && Input.GetKeyDown(KeyCode.F5)) Toggle("DepthCamera");
            if (shiftHeld && Input.GetKeyDown(KeyCode.F6)) Toggle("EFleshTactile");
            if (shiftHeld && Input.GetKeyDown(KeyCode.F7)) Toggle("MotorEncoders");
            if (Input.GetKeyDown(KeyCode.F8)) { trainer.policyMode = !trainer.policyMode; if (trainer.policyMode) trainer.SeedPolicyPopulation(); }
            // Backspace: start/stop recording a DEMONSTRATION (obs+action pairs for imitation seeding).
            if (Input.GetKeyDown(KeyCode.Backspace)) { if (demoRec.IsRecording) demoRec.StopRecording(); else demoRec.StartRecording(); }

            if (Input.GetKeyDown(KeyCode.F9)) ExportStl();
            if (Input.GetKeyDown(KeyCode.F10)) recorder.StopRecording();
            if (Input.GetKeyDown(KeyCode.F11)) ExportBestTrajectory();
            // F1: run the built-in agent demo script (slow-but-correct solution).
            // F1: agent solves the CURRENT scenario autonomously. SortIntoTray -> AutoSort; else tray demo.
            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (scenarios.current == ScenarioType.SortIntoTray) agent.AutoSort();
                else agent.Run(AgentCommands.DemoTrayToTray);
            }
        }

        // "J0 ShoulderPan[T/G]=12° J1 ShoulderLift[Y/H]=-30° ..." — labeled per-servo readout.
        string ServoControlLine()
        {
            var sb = new System.Text.StringBuilder();
            float[] ang = arm.GetJointAngles();
            int n = arm.jointBodies.Count;
            for (int i = 0; i < n; i++)
            {
                string nm = arm.jointSpecs[i].name;
                sb.Append($"<color=#9cf>{nm}</color>[{ArmController.JointKeyLabel(i)}]={ang[i]:F0}\u00b0  ");
            }
            sb.Append("| claw , . ");
            return sb.ToString();
        }

        void Toggle(string sensorName)
        {
            var s = sensorHub.Get(sensorName);
            if (s != null) s.Enabled = !s.Enabled;
        }

        void ExportStl()
        {
            string dir = System.IO.Path.Combine(Application.persistentDataPath, "Exports");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"{config.armName}.stl");
            StlExporter.ExportHierarchy(arm.transform, path);
        }

        void ExportBestTrajectory()
        {
            var traj = trainer.BestToTrajectory();
            if (traj == null) { Debug.Log("[Export] no evolved best yet — press N/T to train"); return; }
            recorder.SetTrajectory(traj);
            string path = recorder.StopRecording(); // writes the injected trajectory to JSON
            Debug.Log($"[Export] best evolved trajectory -> {path}");
            // DF2: also save it into the Demos folder so waypoints_to_lerobot.py can build a training set
            // (GA = diffusion demo factory). Every F11 export grows the demonstration corpus.
            string demoPath = trainer.SaveBestAsDemo();
            if (demoPath != null) Debug.Log($"[Export] + GA demo for diffusion -> {demoPath}");
        }

        // Arm-vs-environment collision policy. The arm is bolted to the table at y=0, so its BASE +
        // shoulder links overlap the worktop top by design — colliding them depenetrates violently on frame
        // 1 and crashes PhysX. So we ignore the table/environment for the PROXIMAL links only (base +
        // shoulder), but let the DISTAL links (elbow → wrist → gripper) COLLIDE with the worktop so the claw
        // rests ON the table instead of clipping THROUGH it. Manipulable props (cube/trays, non-kinematic
        // Rigidbodies) are never ignored.
        void IgnoreArmVsEnvironment(ProceduralArm a)
        {
            if (a == null) return;
            // proximal colliders to ignore vs environment: base + the first 2 joint links (shoulder pan/lift).
            // NOTE: the joints are a NESTED chain, so GetComponentsInChildren returns the whole sub-tree —
            // we must keep only the colliders whose OWNING ArticulationBody is the proximal body itself,
            // otherwise we'd (wrongly) ignore the entire arm including the distal links + gripper.
            var proximal = new System.Collections.Generic.List<Collider>();
            void AddOwn(ArticulationBody body)
            {
                if (body == null) return;
                foreach (var c in body.GetComponentsInChildren<Collider>())
                    if (c.GetComponentInParent<ArticulationBody>() == body) proximal.Add(c);
            }
            AddOwn(a.baseBody);
            for (int i = 0; i < a.jointBodies.Count && i < 2; i++) AddOwn(a.jointBodies[i]);

            foreach (var ec in FindObjectsOfType<Collider>())
            {
                if (ec == null) continue;
                if (ec.GetComponentInParent<ProceduralArm>() != null) continue;   // skip the arm itself
                var rb = ec.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;                       // skip manipulable props
                // ignore only the proximal (base/shoulder) colliders vs the static environment; distal links
                // (elbow/wrist/gripper) keep colliding so the claw can't pass through the worktop.
                foreach (var ac in proximal)
                    if (ac != null) Physics.IgnoreCollision(ac, ec, true);
            }
        }

        static Material Mat(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            return m;
        }
    }
}
