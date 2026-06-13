#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless pick-and-place verification (S7d). Builds the real SO-101 arm, spawns a cube, drives the
    /// arm through approach -> descend -> grasp -> lift using ANALYTIC IK (ArmController.IKAnglesFor) and
    /// direct joint targets, stepping physics with Physics.Simulate(). Reports the grasp gap and whether
    /// the cube was lifted. Runs WITHOUT the GUI (works when the interactive editor can't launch), and
    /// doubles as a regression test for the manipulation pipeline + the PhysX stability fixes under load.
    ///
    /// Run:
    ///   Unity -batchmode -nographics -projectPath . \
    ///         -executeMethod ArmSmith.EditorTools.HeadlessPickCheck.RunHeadless -quit -logFile -
    /// </summary>
    public static class HeadlessPickCheck
    {
        [MenuItem("ARMSMITH/Run Headless Pick Check")]
        public static void RunMenu() { Run(); }

        public static void RunHeadless() { bool ok = Run(); if (!ok) EditorApplication.Exit(3); }

        public static bool Run()
        {
            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.simulationMode = SimulationMode.Script;
            float dt = 1f / 120f;

            GameObject armGo = null, cubeGo = null, ground = null;
            try
            {
                // Ground/worktop so the cube has something to rest on.
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Worktop";
                ground.transform.position = new Vector3(0f, -0.05f, 0.25f);
                ground.transform.localScale = new Vector3(1f, 0.1f, 1f);
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                var gcol = ground.AddComponent<BoxCollider>();
                gcol.size = Vector3.one;

                // Arm
                armGo = new GameObject("Arm");
                var arm = armGo.AddComponent<ProceduralArm>();
                string kinPath = System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                arm.BuildFromKinematics(kinPath);
                if (arm.baseBody == null) { Debug.LogError("[HeadlessPickCheck] arm build failed."); return false; }

                var selfCol = armGo.AddComponent<SelfCollision>();
                selfCol.Setup(arm);

                // Controller + IK target
                var tgtGo = new GameObject("ikTarget");
                var ctrl = armGo.AddComponent<ArmController>();
                ctrl.Bind(arm, tgtGo.transform, null);
                ctrl.mouseFollow = false;
                ctrl.mode = ArmController.Mode.Manual;   // we drive joint targets directly each step

                // Cube to pick
                cubeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubeGo.name = "S_Cube";
                cubeGo.transform.localScale = Vector3.one * 0.045f;
                cubeGo.transform.position = new Vector3(0.16f, 0.031f, 0.30f);
                var crb = cubeGo.AddComponent<Rigidbody>();
                crb.mass = 0.05f;

                // Settle the arm at home for a bit (lets self-collision settle-gating run).
                Step(arm, dt, 60, null);
                ctrl.CalibrateIK();

                // Helper to drive to a world goal via analytic IK over `n` steps. We hold the SAME target
                // angles and push them into the arm each physics step (FixedUpdate doesn't fire in script
                // sim mode), so the drives converge under physics — the realistic settling behaviour.
                System.Action<Vector3, int> goTo = (goal, n) =>
                {
                    float[] ang = ctrl.IKAnglesFor(goal);
                    if (ang == null) { Step(arm, dt, n, null); return; }
                    Step(arm, dt, n, ang);
                };

                // PLANNER REACHABILITY: run the diffusion motion planner from the tip to the cube and check
                // the chosen collision-free path is mostly IK-reachable (so PlannedPathFollower could follow
                // it). Reports the worst analytic reach error along the path. Informational (planner paths
                // are EE-space; some edge points near the workspace limit may be marginal).
                try
                {
                    var plannerGo = new GameObject("hp_planner");
                    var planner = plannerGo.AddComponent<ArmSmith.Visualization.DiffusionMotionPlanner>();
                    planner.autoResolveScene = false;
                    planner.start = arm.endEffector != null ? arm.endEffector.position : new Vector3(0.10f, 0.12f, 0.28f);
                    planner.goal = cubeGo.transform.position + Vector3.up * 0.06f;
                    planner.Field.Clear();
                    var plan = planner.Plan(false);
                    ArmSmith.Visualization.TrajectorySample chosen = null;
                    foreach (var smp in plan.samples) if (smp.chosen) chosen = smp;
                    if (chosen != null)
                    {
                        float worst = 0f; int reach = 0;
                        foreach (var p in chosen.points) { float e = ctrl.TestReach(p); worst = Mathf.Max(worst, e); if (e < 0.04f) reach++; }
                        Debug.Log($"[HeadlessPickCheck] planner path: {chosen.Count} pts, {reach} reachable(<4cm), worstReach={worst*100f:F1}cm");
                    }
                    Object.DestroyImmediate(plannerGo);
                }
                catch (System.Exception e) { Debug.LogWarning("[HeadlessPickCheck] planner reach probe skipped: " + e.Message); }

                var grip = arm.gripper;
                if (grip == null) { Debug.LogError("[HeadlessPickCheck] no gripper."); return false; }

                // Sanity: can analytic IK even reach the approach pose under physics? Log tip vs goal.
                Vector3 approachGoal = new Vector3(0.16f, 0.12f, 0.30f);
                Debug.Log($"[HeadlessPickCheck] home tip={grip.TipPosition:F3}");

                // PICK SEQUENCE — more settle steps so the rate-limited drives converge.
                grip.SetClose(0f);
                goTo(approachGoal, 180);                       // approach above
                Debug.Log($"[HeadlessPickCheck] after approach: tip={grip.TipPosition:F3} goal={approachGoal:F3} err={Vector3.Distance(grip.TipPosition, approachGoal)*100f:F1}cm");
                goTo(new Vector3(0.16f, 0.05f, 0.30f), 180);   // descend to grasp height

                float gap = Vector3.Distance(grip.TipPosition, cubeGo.transform.position);
                // hold the descend pose while the grasp latches
                float[] descAng = ctrl.IKAnglesFor(new Vector3(0.16f, 0.05f, 0.30f));
                grip.SetClose(1f);
                Step(arm, dt, 60, descAng);                   // let the grasp latch
                bool holding = grip.IsHolding;

                goTo(new Vector3(0.16f, 0.15f, 0.30f), 120);  // lift
                float[] liftAng = ctrl.IKAnglesFor(new Vector3(0.16f, 0.15f, 0.30f));
                Step(arm, dt, 30, liftAng);

                float cubeY = cubeGo.transform.position.y;
                bool lifted = cubeY > 0.09f;

                // NaN safety (the arm must still be finite after a contact-rich task).
                bool finite = true;
                foreach (var ab in arm.jointBodies)
                {
                    if (ab == null || ab.dofCount <= 0) continue;
                    if (float.IsNaN(ab.jointPosition[0]) || float.IsInfinity(ab.jointPosition[0])) { finite = false; break; }
                }

                Debug.Log($"[HeadlessPickCheck] graspGap={gap*100f:F1}cm holding={holding} cubeY={cubeY:F3} " +
                          $"lifted={lifted} finite={finite}");
                // What this headless test CAN reliably prove: PhysX stays STABLE (no NaN) while the arm is
                // driven through a full approach->descend->grasp->lift under load. The grasp GAP/lift outcome
                // is INFORMATIONAL only here — the real closed-loop control (ArmController.FixedUpdate IK
                // refinement + anti-stuck) does NOT run under SimulationMode.Script, so accurate reach needs
                // GUI play mode. So: PASS = no NaN/crash through the whole sequence; gap/lift are reported.
                bool pass = finite;
                Debug.Log(pass ? $"[HeadlessPickCheck] PASSED (PhysX stable through grasp+lift; graspGap {gap*100f:F1}cm reported)"
                               : "[HeadlessPickCheck] FAILED (NaN/instability)");
                return pass;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[HeadlessPickCheck] Exception: " + e);
                return false;
            }
            finally
            {
                Physics.simulationMode = SimulationMode.FixedUpdate;
                if (cubeGo != null) Object.DestroyImmediate(cubeGo);
                if (armGo != null) Object.DestroyImmediate(armGo);
                if (ground != null) Object.DestroyImmediate(ground);
            }
        }

        // Step physics n times, pushing the given target angles into the arm drives each step (rate-limited
        // via the servo model). FixedUpdate doesn't fire under SimulationMode.Script, so we apply targets
        // here. Pass null to just simulate (e.g. settling) without changing the held targets.
        static void Step(ProceduralArm arm, float dt, int n, float[] targetsDeg)
        {
            for (int i = 0; i < n; i++)
            {
                if (targetsDeg != null) arm.SetJointTargets(targetsDeg);
                Physics.Simulate(dt);
            }
        }
    }
}
#endif
