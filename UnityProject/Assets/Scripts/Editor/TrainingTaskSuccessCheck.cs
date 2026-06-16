#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless proof that the system can actually PERFORM THE TASK — not just that the optimiser math
    /// converges (TrainingSmokeCheck) or that a single grasp is stable (RealisticGraspCheck), but that the
    /// pick-and-place MOTION the GA warm-starts from (and refines) moves the cube from its start to a target.
    ///
    /// It builds the real SO-101 arm + a cube + a target pad, runs the SAME pick-place waypoint sequence the
    /// trainer's BuildPickPlaceDemo() uses (above -> descend -> grasp -> lift -> over-target -> place ->
    /// release), stepping physics with Physics.Simulate, and asserts the cube ends up AT THE TARGET and OFF
    /// its start. This is the end-to-end task the Generations UI shows reaching 100% success.
    ///
    /// Run: -executeMethod ArmSmith.EditorTools.TrainingTaskSuccessCheck.RunHeadless
    /// </summary>
    public static class TrainingTaskSuccessCheck
    {
        [MenuItem("ARMSMITH/Run Training Task-Success Check")] public static void RunMenu() { Run(); }
        public static void RunHeadless() { if (!Run()) EditorApplication.Exit(9); }

        public static bool Run()
        {
            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.simulationMode = SimulationMode.Script;
            float dt = 1f / 120f;

            GameObject armGo = null, cubeGo = null, ground = null, padGo = null;
            try
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Worktop";
                ground.transform.position = new Vector3(0f, -0.05f, 0.25f);
                ground.transform.localScale = new Vector3(1.2f, 0.1f, 1.2f);
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                ground.AddComponent<BoxCollider>().size = Vector3.one;

                armGo = new GameObject("Arm");
                var arm = armGo.AddComponent<ProceduralArm>();
                string kinPath = System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                arm.BuildFromKinematics(kinPath);
                if (arm.baseBody == null) { Debug.LogError("[TrainingTaskSuccessCheck] arm build failed."); return false; }
                armGo.AddComponent<SelfCollision>().Setup(arm);
                var groundCol = ground.GetComponent<Collider>();
                foreach (var c in arm.baseBody.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, groundCol, true);
                foreach (var ab in arm.jointBodies) foreach (var c in ab.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, groundCol, true);

                var tgtGo = new GameObject("ikTarget");
                var ctrl = armGo.AddComponent<ArmController>();
                ctrl.Bind(arm, tgtGo.transform, null);
                ctrl.mouseFollow = false;
                ctrl.mode = ArmController.Mode.Manual;

                // cube at a start position, target pad at a different position (the place goal)
                Vector3 cubeStart = new Vector3(0.16f, 0.031f, 0.30f);
                Vector3 target = new Vector3(-0.14f, 0.02f, 0.30f);
                cubeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubeGo.name = "S_Cube"; cubeGo.transform.localScale = Vector3.one * 0.045f;
                cubeGo.transform.position = cubeStart;
                var crb = cubeGo.AddComponent<Rigidbody>();
                crb.mass = 0.05f; crb.maxDepenetrationVelocity = 1f; crb.maxLinearVelocity = 5f;
                cubeGo.GetComponent<BoxCollider>().material = new PhysicsMaterial("c") { dynamicFriction = 1.1f, staticFriction = 1.3f };

                padGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                padGo.name = "S_Pad"; padGo.transform.localScale = new Vector3(0.1f, 0.004f, 0.1f);
                padGo.transform.position = new Vector3(target.x, 0.002f, target.z);
                Object.DestroyImmediate(padGo.GetComponent<Collider>());

                var grip = arm.gripper;
                if (grip == null) { Debug.LogError("[TrainingTaskSuccessCheck] no gripper."); return false; }

                Step(arm, dt, 60, null);
                ctrl.CalibrateIK();

                // The pick-place waypoint plan (mirrors BuildPickPlaceDemo): above -> grasp -> lift ->
                // over-target -> place -> release. Drive each via analytic IK and Physics.Simulate.
                var plan = new (Vector3 pos, float grip, int steps)[]
                {
                    (new Vector3(cubeStart.x, 0.14f, cubeStart.z), 0f, 150),  // above object, open
                    (new Vector3(cubeStart.x, 0.05f, cubeStart.z), 0f, 150),  // descend
                    (new Vector3(cubeStart.x, 0.05f, cubeStart.z), 1f, 90),   // close (grab)
                    (new Vector3(cubeStart.x, 0.16f, cubeStart.z), 1f, 130),  // lift
                    (new Vector3(0f, 0.20f, 0.30f),                1f, 120),  // via centre
                    (new Vector3(target.x, 0.16f, target.z),       1f, 150),  // over target
                    (new Vector3(target.x, 0.075f, target.z),      1f, 130),  // descend to place
                    (new Vector3(target.x, 0.075f, target.z),      0f, 90),   // release
                    (new Vector3(target.x, 0.18f, target.z),       0f, 80),   // retreat
                };

                foreach (var w in plan)
                {
                    float[] ang = ctrl.IKAnglesFor(w.pos);
                    if (grip != null) grip.SetClose(w.grip);
                    Step(arm, dt, w.steps, ang);
                }
                Step(arm, dt, 60, null);   // settle

                Vector3 cubeEnd = cubeGo.transform.position;
                float toTarget = Vector3.Distance(new Vector3(cubeEnd.x, 0, cubeEnd.z), new Vector3(target.x, 0, target.z));
                float fromStart = Vector3.Distance(new Vector3(cubeEnd.x, 0, cubeEnd.z), new Vector3(cubeStart.x, 0, cubeStart.z));
                bool onTable = cubeEnd.y > -0.05f && cubeEnd.y < 0.2f;

                // NaN safety
                bool finite = true;
                foreach (var ab in arm.jointBodies)
                {
                    if (ab == null || ab.dofCount <= 0) continue;
                    if (float.IsNaN(ab.jointPosition[0]) || float.IsInfinity(ab.jointPosition[0])) { finite = false; break; }
                }

                // SUCCESS: cube delivered near the target (<10cm), clearly moved off its start (>10cm), stable.
                bool moved = fromStart > 0.10f;
                bool delivered = toTarget < 0.10f;
                bool pass = finite && onTable && moved && delivered;

                Debug.Log($"[TrainingTaskSuccessCheck] cubeEnd={cubeEnd:F3} toTarget={toTarget*100f:F1}cm " +
                          $"fromStart={fromStart*100f:F1}cm onTable={onTable} finite={finite}");
                Debug.Log(pass
                    ? $"[TrainingTaskSuccessCheck] PASSED — pick-place MOVED the cube to the target ({toTarget*100f:F1}cm), task performed."
                    : $"[TrainingTaskSuccessCheck] FAILED — moved={moved} delivered={delivered} onTable={onTable} finite={finite}");
                return pass;
            }
            catch (System.Exception e) { Debug.LogError("[TrainingTaskSuccessCheck] " + e); return false; }
            finally
            {
                Physics.simulationMode = SimulationMode.FixedUpdate;
                if (cubeGo != null) Object.DestroyImmediate(cubeGo);
                if (padGo != null) Object.DestroyImmediate(padGo);
                if (armGo != null) Object.DestroyImmediate(armGo);
                if (ground != null) Object.DestroyImmediate(ground);
            }
        }

        static void Step(ProceduralArm arm, float dt, int n, float[] targetsDeg)
        {
            for (int i = 0; i < n; i++)
            {
                if (targetsDeg != null) arm.SetJointTargets(targetsDeg);
                if (arm.gripper != null) arm.gripper.TickHeld();
                Physics.Simulate(dt);
            }
        }
    }
}
#endif
