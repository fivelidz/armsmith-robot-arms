#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless diagnostic for the "arm won't descend to low targets" jam. Builds the real arm + worktop
    /// (top at y=0, like the game), applies the same arm-vs-environment + self-collision ignores the game
    /// does, then drives the arm to a low target via analytic IK and reports the achieved tip height under
    /// THREE collider configurations:
    ///   A) full collisions (game config)         — what the player sees
    ///   B) cube removed                            — isolates cube-as-obstacle
    ///   C) all arm colliders disabled              — pure kinematic ceiling
    /// This pinpoints what actually blocks the descent, WITHOUT the flaky live bridge.
    /// Run: -executeMethod ArmSmith.EditorTools.DescentCheck.RunHeadless
    /// </summary>
    public static class DescentCheck
    {
        [MenuItem("ARMSMITH/Run Descent Check")]
        public static void RunMenu() { Run(); }
        public static void RunHeadless() { Run(); }

        public static bool Run()
        {
            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.simulationMode = SimulationMode.Script;
            float dt = 1f / 120f;
            GameObject armGo = null, worktop = null, cube = null;
            try
            {
                worktop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                worktop.name = "Worktop";
                worktop.transform.position = new Vector3(0f, -0.025f, 0.25f);   // top at y=0
                worktop.transform.localScale = new Vector3(0.8f, 0.05f, 0.8f);

                armGo = new GameObject("Arm");
                var arm = armGo.AddComponent<ProceduralArm>();
                arm.BuildFromKinematics(System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json"));
                if (arm.baseBody == null) { Debug.LogError("[DescentCheck] build failed"); return false; }

                var sc = armGo.AddComponent<SelfCollision>();
                sc.Setup(arm);
                var ctrl = armGo.AddComponent<ArmController>();
                var tgt = new GameObject("ikt").transform;
                ctrl.Bind(arm, tgt, null);

                // arm-vs-worktop ignore (same as GameBootstrap.IgnoreArmVsEnvironment)
                var wc = worktop.GetComponent<Collider>();
                IgnoreArmVs(arm, wc);

                cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "S_Cube"; cube.transform.localScale = Vector3.one * 0.045f;
                cube.transform.position = new Vector3(0.16f, 0.031f, 0.30f);
                cube.AddComponent<Rigidbody>().mass = 0.05f;

                Vector3 goal = new Vector3(0.16f, 0.05f, 0.30f);

            // turn OFF servo rate-limiting so SetJointTargets commands the drive directly (isolate FK/drive)
            var sf = typeof(ProceduralArm).GetField("servoFidelity", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (sf != null) { sf.SetValue(arm, false); Debug.Log("[DescentCheck] servoFidelity OFF"); }

            Settle(arm, dt, 80);
            ctrl.CalibrateIK();

            // FK vs physical at the CURRENT (home) pose — isolates whether the base/chain calibration matches.
            {
                var cur = arm.GetJointAngles();
                float e0 = ctrl.TestReachWith(cur, arm.gripper.TipPosition, out Vector3 fk0);
                Debug.Log($"[DescentCheck] HOME pose: FK={fk0:F3} physical={arm.gripper.TipPosition:F3} angles=[{cur[0]:F0},{cur[1]:F0},{cur[2]:F0},{cur[3]:F0}] disagreement={e0*100f:F1}cm");
            }

            // Does the IK's FK endpoint match the physical gripper tip for the SOLVED angles?
            float fkErr = ctrl.TestReach(goal);
            Vector3 ee = arm.endEffector != null ? arm.endEffector.position : Vector3.zero;
            Vector3 tip = arm.gripper != null ? arm.gripper.TipPosition : ee;
            Debug.Log($"[DescentCheck] analytic TestReach(endEffector) err={fkErr*100f:F1}cm | " +
                      $"endEffector={ee:F3} gripperTip={tip:F3} offset={(tip-ee).magnitude*100f:F1}cm");

            float a = DriveAndMeasure(ctrl, arm, goal, dt);
                Debug.Log($"[DescentCheck] A) full collisions: tipY={a:F3} (goalY=0.05)");

                // B) remove cube
                Object.DestroyImmediate(cube); cube = null;
                ResetPose(ctrl, arm, dt);
                float b = DriveAndMeasure(ctrl, arm, goal, dt);
                Debug.Log($"[DescentCheck] B) cube removed: tipY={b:F3}");

                // C) disable all arm colliders
                foreach (var ab in arm.jointBodies) foreach (var c in ab.GetComponentsInChildren<Collider>()) c.enabled = false;
                foreach (var c in arm.baseBody.GetComponentsInChildren<Collider>()) c.enabled = false;
                ResetPose(ctrl, arm, dt);
                float cc = DriveAndMeasure(ctrl, arm, goal, dt);
                Debug.Log($"[DescentCheck] C) arm colliders off: tipY={cc:F3}");

                bool descends = a < 0.10f;   // with the FK fix the arm should reach near the 0.05 goal
                Debug.Log($"[DescentCheck] VERDICT: full={a:F3} noCube={b:F3} noArmCol={cc:F3} (goal 0.05) => " +
                          (descends ? "ARM DESCENDS CORRECTLY (FK ok)" : "STILL FLOORING (FK/drive issue)"));
                return descends;
            }
            catch (System.Exception e) { Debug.LogError("[DescentCheck] " + e); return false; }
            finally
            {
                Physics.simulationMode = SimulationMode.FixedUpdate;
                if (cube) Object.DestroyImmediate(cube);
                if (armGo) Object.DestroyImmediate(armGo);
                if (worktop) Object.DestroyImmediate(worktop);
            }
        }

        static void IgnoreArmVs(ProceduralArm arm, Collider other)
        {
            if (arm.baseBody != null) foreach (var c in arm.baseBody.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, other, true);
            foreach (var ab in arm.jointBodies) foreach (var c in ab.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, other, true);
        }

        static void ResetPose(ArmController ctrl, ProceduralArm arm, float dt)
        {
            ctrl.HardHome(null);
            Settle(arm, dt, 40);
        }

        static void Settle(ProceduralArm arm, float dt, int n)
        {
            for (int i = 0; i < n; i++) Physics.Simulate(dt);
        }

        static float DriveAndMeasure(ArmController ctrl, ProceduralArm arm, Vector3 goal, float dt)
        {
            float[] ang = ctrl.IKAnglesFor(goal);
            // FK-predicted tip for the SOLVED angles (what the IK thinks it achieves)
            float fkPredErr = ctrl.TestReachWith(ang, goal, out Vector3 fkTip);
            for (int i = 0; i < 200; i++) { if (ang != null) arm.SetJointTargets(ang); Physics.Simulate(dt); }
            var act = arm.GetJointAngles();
            var sb = new System.Text.StringBuilder("    SOLVED ang=[");
            for (int i = 0; i < 6 && ang != null; i++) sb.Append(ang[i].ToString("F0") + (i < 5 ? "," : ""));
            sb.Append("] FKtip=" + fkTip.ToString("F3") + " | ACTUAL ang=[");
            for (int i = 0; i < 6; i++) sb.Append(act[i].ToString("F0") + (i < 5 ? "," : ""));
            sb.Append("]");
            Debug.Log(sb.ToString());
            // FK of the ACTUAL angles vs the PHYSICAL tip — if these disagree, the FK model != real arm.
            float fkActErr = ctrl.TestReachWith(act, goal, out Vector3 fkActTip);
            Vector3 physTip = arm.gripper != null ? arm.gripper.TipPosition : arm.endEffector.position;
            Debug.Log($"    FK(actual)={fkActTip:F3} vs PHYSICAL tip={physTip:F3} (disagreement={(fkActTip-physTip).magnitude*100f:F1}cm => model mismatch if large)");
            return physTip.y;
        }
    }
}
#endif
