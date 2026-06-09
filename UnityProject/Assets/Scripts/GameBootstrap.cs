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
        SensorViz sensorViz;
        MouseInteraction mouse;
        ServoPanel servoPanel;
        ModuleUsagePanel modulePanel;
        ArmSmith.Verification.VerificationPanel verificationPanel;
        ServoCallouts servoCallouts;
        ScenarioMenu scenarioMenu;
        BuilderPanel builderPanel;
        ControlBar controlBar;
        CommandConsole commandConsole;
        DemoRecorder demoRec;
        SequenceEditor sequence;
        SaveSystem saveSystem;
        Transform ikTarget;

        // HUD
        Text infoText;

        void Start()
        {
            Physics.defaultSolverIterations = 24;          // crisp joint physics
            Physics.defaultSolverVelocityIterations = 8;
            Time.fixedDeltaTime = 1f / 120f;               // 120 Hz physics for stable articulation

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
        }

        void BuildSensors()
        {
            var go = new GameObject("SensorHub");
            sensorHub = go.AddComponent<SensorHub>();
            sensorHub.Init(arm, rig);

            sensorViz = go.AddComponent<SensorViz>();
            sensorViz.Bind(sensorHub);
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
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // wrist + env panels (top-right corner)
            rig.wristPanel = MakePanel(canvasGo.transform, "WristPanel", new Vector2(-10, -10), new Vector2(200, 200), new Vector2(1, 1));
            rig.envPanel   = MakePanel(canvasGo.transform, "EnvPanel",   new Vector2(-10, -220), new Vector2(200, 150), new Vector2(1, 1));
            // Now that panels exist, set up the cameras + RenderTextures and bind them to the panels.
            rig.Setup(arm.endEffector, EnvCamPos, EnvCamLook);

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

            // Clickable servo callouts: click a joint's yellow hotspot -> leader line + command panel.
            servoCallouts = canvasGo.AddComponent<ServoCallouts>();
            servoCallouts.Build(arm, controller, rig.mainCam, canvas);

            // Scenario selection menu (top-center): clickable list of scenarios + active objective.
            scenarioMenu = canvasGo.AddComponent<ScenarioMenu>();
            scenarioMenu.Build(canvasGo.transform, scenarios);

            // Robot-arm BUILDER panel (left dock): arm stats + module toggles + training/generations view.
            builderPanel = canvasGo.AddComponent<BuilderPanel>();
            builderPanel.Build(canvasGo.transform, arm, sensorHub, trainer);

            // Clickable CONTROL BAR (bottom-center): view + control toggle buttons (mouse-operable).
            controlBar = canvasGo.AddComponent<ControlBar>();
            controlBar.Build(canvasGo.transform, controller, arm, sensorViz, gizmos, rig, servoCallouts, trainer, scenarios);

            // Live TEXT COMMAND console: type robot commands (AgentCommands grammar) -> execute.
            commandConsole = canvasGo.AddComponent<CommandConsole>();
            commandConsole.Build(canvasGo.transform, agent);
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
                $"Scenario: <b>{scenarios.current}</b>  (Tab... no — keys 1-7 pick) | Evolve: T train, N +1 gen, F11 export best\n" +
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

            // Sensor module toggles (ablation): F2 IMU, F3 RangeFinder, F4 Lidar2D, F5 DepthCamera, F6 eFlesh, F7 MotorEncoders
            if (Input.GetKeyDown(KeyCode.F2)) Toggle("IMU");
            if (Input.GetKeyDown(KeyCode.F3)) Toggle("RangeFinder");
            if (Input.GetKeyDown(KeyCode.F4)) Toggle("Lidar2D");
            if (Input.GetKeyDown(KeyCode.F5)) Toggle("DepthCamera");
            if (Input.GetKeyDown(KeyCode.F6)) Toggle("EFleshTactile");
            if (Input.GetKeyDown(KeyCode.F7)) Toggle("MotorEncoders");
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
        }

        static Material Mat(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            return m;
        }
    }
}
