#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless verification of the REALISTIC (friction-limited) grasp path (Gripper.realisticGrasp).
    /// Research basis: research/manipulation_repos/GRASP_PHYSICS_STUDY.md — a force-limited dynamic
    /// follower with the analytic friction gate F_hold = 2*mu*F_grip; the object slips/drops when its
    /// load m*(g+a) exceeds capacity.
    ///
    /// This is a BEHAVIOURAL gate: it must demonstrate that the grasp is realistic, i.e.
    ///   (1) a STRONG grip (high grip force) HOLDS a light cube through a lift, AND
    ///   (2) a WEAK grip (grip force too low to beat gravity) FAILS to pick the cube up.
    /// If both hold, slip/drop is genuinely emergent from the force balance (not scripted), which is the
    /// whole point — it predicts real-arm grasp failures.
    ///
    /// Run:
    ///   Unity -batchmode -nographics -projectPath . \
    ///         -executeMethod ArmSmith.EditorTools.RealisticGraspCheck.RunHeadless -quit -logFile -
    /// </summary>
    public static class RealisticGraspCheck
    {
        [MenuItem("ARMSMITH/Run Realistic Grasp Check")]
        public static void RunMenu() { Run(); }

        public static void RunHeadless() { bool ok = Run(); if (!ok) EditorApplication.Exit(3); }

        public static bool Run()
        {
            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.simulationMode = SimulationMode.Script;
            float dt = 1f / 120f;

            bool strongHeld = false, strongLifted = false;
            bool weakLifted = true;       // we WANT this to end up false
            try
            {
                // --- case 1: STRONG grip should hold + lift ---
                {
                    var (held, lifted) = RunCase(dt, gripForceMax: 30f, frictionMu: 0.9f, cubeMass: 0.05f);
                    strongHeld = held; strongLifted = lifted;
                    Debug.Log($"[RealisticGraspCheck] STRONG grip: holding={held} lifted={lifted}");
                }
                // --- case 2: WEAK grip should fail to lift (drops / never forms) ---
                {
                    // grip force so low that 2*mu*F_grip < m*g -> cannot hold the cube's weight
                    var (held, lifted) = RunCase(dt, gripForceMax: 0.2f, frictionMu: 0.4f, cubeMass: 0.05f);
                    weakLifted = lifted;
                    Debug.Log($"[RealisticGraspCheck] WEAK grip: holding={held} lifted={lifted} (expect lifted=false)");
                }

                bool pass = strongHeld && strongLifted && !weakLifted;
                Debug.Log(pass
                    ? $"[RealisticGraspCheck] PASSED — strong grip holds+lifts, weak grip fails to lift (slip/drop is emergent)."
                    : $"[RealisticGraspCheck] FAILED — strongHeld={strongHeld} strongLifted={strongLifted} weakLifted={weakLifted}");
                return pass;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[RealisticGraspCheck] Exception: " + e);
                return false;
            }
            finally
            {
                Physics.simulationMode = SimulationMode.FixedUpdate;
            }
        }

        // Build arm + cube, configure the realistic grasp with the given params, run approach->grasp->lift,
        // return (was holding right after grasp, was the cube lifted at the end).
        static (bool held, bool lifted) RunCase(float dt, float gripForceMax, float frictionMu, float cubeMass)
        {
            GameObject armGo = null, cubeGo = null, ground = null;
            try
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Worktop";
                ground.transform.position = new Vector3(0f, -0.05f, 0.25f);
                ground.transform.localScale = new Vector3(1f, 0.1f, 1f);
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                ground.AddComponent<BoxCollider>().size = Vector3.one;

                armGo = new GameObject("Arm");
                var arm = armGo.AddComponent<ProceduralArm>();
                string kinPath = System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                arm.BuildFromKinematics(kinPath);
                if (arm.baseBody == null) { Debug.LogError("[RealisticGraspCheck] arm build failed."); return (false, false); }

                armGo.AddComponent<SelfCollision>().Setup(arm);
                var groundCol = ground.GetComponent<Collider>();
                foreach (var c in arm.baseBody.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, groundCol, true);
                foreach (var ab in arm.jointBodies) foreach (var c in ab.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, groundCol, true);

                var tgtGo = new GameObject("ikTarget");
                var ctrl = armGo.AddComponent<ArmController>();
                ctrl.Bind(arm, tgtGo.transform, null);
                ctrl.mouseFollow = false;
                ctrl.mode = ArmController.Mode.Manual;

                cubeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubeGo.name = "S_Cube";
                cubeGo.transform.localScale = Vector3.one * 0.045f;
                cubeGo.transform.position = new Vector3(0.16f, 0.031f, 0.30f);
                var crb = cubeGo.AddComponent<Rigidbody>();
                crb.mass = cubeMass;
                crb.maxDepenetrationVelocity = 1f;
                crb.maxLinearVelocity = 5f;
                cubeGo.GetComponent<BoxCollider>().material =
                    new PhysicsMaterial("c") { dynamicFriction = 1.1f, staticFriction = 1.3f };

                var grip = arm.gripper;
                if (grip == null) { Debug.LogError("[RealisticGraspCheck] no gripper."); return (false, false); }
                // ENABLE the realistic friction grasp for this run.
                grip.realisticGrasp = true;
                grip.gripForceMax = gripForceMax;
                grip.frictionMu = frictionMu;

                Step(arm, dt, 60, null);
                ctrl.CalibrateIK();

                System.Action<Vector3, int> goTo = (goal, n) =>
                {
                    float[] ang = ctrl.IKAnglesFor(goal);
                    Step(arm, dt, n, ang);   // ang may be null -> just settle
                };

                grip.SetClose(0f);
                goTo(new Vector3(0.16f, 0.12f, 0.30f), 180);   // approach above
                goTo(new Vector3(0.16f, 0.05f, 0.30f), 180);   // descend to grasp

                float[] descAng = ctrl.IKAnglesFor(new Vector3(0.16f, 0.05f, 0.30f));
                grip.SetClose(1f);
                Step(arm, dt, 60, descAng);                    // latch
                bool holding = grip.IsHolding;

                float[] liftAng = ctrl.IKAnglesFor(new Vector3(0.16f, 0.16f, 0.30f));
                Step(arm, dt, 220, liftAng);                   // lift

                float cubeY = cubeGo.transform.position.y;
                bool lifted = cubeY > 0.09f;
                Debug.Log($"[RealisticGraspCheck]   case(F={gripForceMax},mu={frictionMu},m={cubeMass}): cubeY={cubeY:F3} holding={grip.IsHolding} slipped={grip.LastGraspSlipped}");
                return (holding, lifted);
            }
            finally
            {
                if (cubeGo != null) Object.DestroyImmediate(cubeGo);
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
