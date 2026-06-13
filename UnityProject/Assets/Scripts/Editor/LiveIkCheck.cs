#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless test of the LIVE IK control loop — the path the PLAYER uses (ArmController in IK mode,
    /// moving ikTarget, SolveIK each FixedUpdate). Drives ikTarget to several goals and calls
    /// ArmController.TickControl() per Physics.Simulate step (Unity FixedUpdate doesn't fire under script
    /// sim), then measures the physical tip error. Verifies the player's mouse-follow/IK-target control
    /// tracks low + spread targets.
    /// Run: -executeMethod ArmSmith.EditorTools.LiveIkCheck.RunHeadless
    /// </summary>
    public static class LiveIkCheck
    {
        [MenuItem("ARMSMITH/Run Live IK Check")] public static void RunMenu() { Run(); }
        public static void RunHeadless() { if (!Run()) EditorApplication.Exit(6); }

        public static bool Run()
        {
            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.simulationMode = SimulationMode.Script;
            float dt = 1f / 120f;
            GameObject armGo = null, worktop = null;
            try
            {
                worktop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                worktop.name = "Worktop";
                worktop.transform.position = new Vector3(0f, -0.025f, 0.25f);
                worktop.transform.localScale = new Vector3(0.8f, 0.05f, 0.8f);

                armGo = new GameObject("Arm");
                var arm = armGo.AddComponent<ProceduralArm>();
                arm.BuildFromKinematics(System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json"));
                if (arm.baseBody == null) { Debug.LogError("[LiveIkCheck] build failed"); return false; }
                armGo.AddComponent<SelfCollision>().Setup(arm);
                var wc = worktop.GetComponent<Collider>();
                if (arm.baseBody != null) foreach (var c in arm.baseBody.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, wc, true);
                foreach (var ab in arm.jointBodies) foreach (var c in ab.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, wc, true);

                var ctrl = armGo.AddComponent<ArmController>();
                var tgt = new GameObject("ikt").transform;
                ctrl.Bind(arm, tgt, null);
                ctrl.mouseFollow = false;
                ctrl.mode = ArmController.Mode.IK;

                for (int i = 0; i < 60; i++) Physics.Simulate(dt);
                ctrl.CalibrateIK();

                var goals = new[] {
                    new Vector3(0.10f, 0.12f, 0.28f),
                    new Vector3(0.16f, 0.06f, 0.30f),
                    new Vector3(0.00f, 0.16f, 0.30f),
                    new Vector3(-0.12f, 0.07f, 0.28f),
                    new Vector3(0.14f, 0.05f, 0.30f),
                };
                float worst = 0f, sum = 0f; int n = 0;
                foreach (var g in goals)
                {
                    tgt.position = g;
                    for (int i = 0; i < 240; i++) { ctrl.TickControl(); Physics.Simulate(dt); }
                    Vector3 tip = arm.gripper != null ? arm.gripper.TipPosition : arm.endEffector.position;
                    float e = (tip - g).magnitude;
                    Debug.Log($"[LiveIkCheck] goal {g:F2} -> tip {tip:F3} err {e*100f:F1}cm");
                    worst = Mathf.Max(worst, e); sum += e; n++;
                }
                float mean = sum / n;
                bool pass = mean < 0.05f && worst < 0.08f;
                Debug.Log($"[LiveIkCheck] {(pass ? "PASSED" : "FAILED")} — mean {mean*100f:F1}cm, worst {worst*100f:F1}cm (player IK-target path)");
                return pass;
            }
            catch (System.Exception e) { Debug.LogError("[LiveIkCheck] " + e); return false; }
            finally
            {
                Physics.simulationMode = SimulationMode.FixedUpdate;
                if (armGo) Object.DestroyImmediate(armGo);
                if (worktop) Object.DestroyImmediate(worktop);
            }
        }
    }
}
#endif
