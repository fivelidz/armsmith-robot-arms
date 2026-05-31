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
        // STL skinning needs the real SO-ARM100 joint frames (URDF) to align; until that's wired,
        // Procedural arm is the stable shipping default (clean look + approved mouse control work well).
        // Real SO-101 STL arm (URDF-accurate frames, now aligned). true=real model, false=procedural.
        public bool useRealStlMeshes = false;
        ProceduralArm arm;
        ArmController controller;
        CameraRig rig;
        ScenarioManager scenarios;
        ArmGizmos gizmos;
        BehaviourRecorder recorder;
        EvolutionTrainer trainer;
        AgentCommands agent;
        SensorHub sensorHub;
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
        }

        void BuildTrainer()
        {
            var trGo = new GameObject("EvolutionTrainer");
            trainer = trGo.AddComponent<EvolutionTrainer>();
            trainer.Init(arm, controller, scenarios);

            var agGo = new GameObject("AgentCommands");
            agent = agGo.AddComponent<AgentCommands>();
            agent.Bind(controller, arm, scenarios, trainer, ikTarget);
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
                // Build from real SO-101 URDF kinematics + STL meshes.
                // Populates baseBody, jointBodies (6), jointSpecs (6), servos (6),
                // endEffector, leftJaw, rightJaw, gripper — same public contract as Build(config).
                string kinPath = System.IO.Path.Combine(
                    Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                arm.BuildFromKinematics(kinPath);
                // If kinematics build failed (missing file), fall back to procedural.
                if (arm.baseBody == null)
                {
                    Debug.LogWarning("[GameBootstrap] BuildFromKinematics failed; falling back to Build(config).");
                    arm.useStlMeshes = false;
                    arm.Build(config);
                }
                else if (arm.config == null)
                    arm.config = config;  // ensure TotalReach() / AxisVector() have a valid config
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

            // info text (top-left)
            var txtGo = new GameObject("Info");
            txtGo.transform.SetParent(canvasGo.transform, false);
            infoText = txtGo.AddComponent<Text>();
            infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            infoText.fontSize = 15;
            infoText.color = Color.white;
            var rt = infoText.rectTransform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(10, -10);
            rt.sizeDelta = new Vector2(640, 320);
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
                $"Mode: {mode} (Tab) | Gripper: {grip}  (, open  . close  Space toggle)\n" +
                $"IK: arm FOLLOWS MOUSE on work-plane (M toggle); scroll=pick height; WASD/RF nudge\n" +
                $"Manual: 1-{arm.jointBodies.Count} select joint, Q/E rotate | F1 run agent demo\n" +
                $"Camera: RMB orbit, MMB pan, Ctrl+scroll zoom, V HUD | B bounds, X axes\n" +
                $"Record G | Playback P | Reset Esc | Export F9 STL / F10 waypoints\n" +
                $"Scenario: <b>{scenarios.current}</b>  ([ ] change) | Evolve: T train, N +1 gen, F11 export best\n" +
                $"<color=#fd8>OBJECTIVE:</color> {scenarios.Objective()}\n" +
                $"<color=#8c8>{scenarios.RewardSpec()}</color>\n" +
                $"Reward: {scenarios.LastReward:F2}   Time: {scenarios.Elapsed:F1}s   " +
                $"{(scenarios.Succeeded ? "<color=#4f4>SUCCESS</color>" : "")}\n" +
                $"<color=#fa6>Servo bus (ticks):</color> {arm.ServoBusString()}\n" +
                $"<color=#9cf>Sensors</color> (F2-F7 toggle): {sensorHub.Summary()}\n" +
                $"<color=#fc6>Sim speed: {Time.timeScale:F1}x</color>  (+/- adjust, 0 to pause)  " +
                $"<color=#999>real servos move slower; speed-up is sim-only</color>\n" +
                $"<color=#7cf>Evolution</color> gen {trainer.generation}  " +
                $"best {(trainer.best != null ? trainer.best.fitness.ToString("F2") : "-")}  [{trainer.status}]";

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

            if (Input.GetKeyDown(KeyCode.F9)) ExportStl();
            if (Input.GetKeyDown(KeyCode.F10)) recorder.StopRecording();
            if (Input.GetKeyDown(KeyCode.F11)) ExportBestTrajectory();
            // F1: run the built-in agent demo script (slow-but-correct solution).
            if (Input.GetKeyDown(KeyCode.F1)) agent.Run(AgentCommands.DemoTrayToTray);
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
