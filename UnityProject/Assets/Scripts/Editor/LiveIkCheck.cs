#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless test of the LIVE IK control loop — the exact path the PLAYER uses (ArmController in IK
    /// mode, moving ikTarget, SolveIK each FixedUpdate). It drives ikTarget around the workspace and calls
    /// ArmController.TickControl() per Physics.Simulate step (Unity FixedUpdate doesn't fire under script
    /// sim), then measures the physical tip error. This verifies the FK fix makes the player's mouse-follow
    /// / IK-target control actually track low + spread targets — not just the analytic IKAnglesFor path.
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

                // optionally disable the servo rate-limiter to isolate it as the oscillation source
                bool noServo = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noservo") >= 0;
                if (noServo)
                {
                    var sf = typeof(ProceduralArm).GetField("servoFidelity", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (sf != null) { sf.SetValue(arm, false); Debug.Log("[LiveIkCheck] (-noservo) servoFidelity OFF"); }
                }

                // settle + calibrate (mirror the live settle path)
                for (int i = 0; i < 60; i++) Physics.Simulate(dt);
                ctrl.CalibrateIK();

                // DIAGNOSTIC: disable ALL arm colliders to test if collision blocks the wrist descent.
                if (System.Environment.GetCommandLineArgs() != null) { }
                bool noColliders = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nocol") >= 0;
                if (noColliders)
                {
                    foreach (var ab in arm.jointBodies) foreach (var c in ab.GetComponentsInChildren<Collider>()) c.enabled = false;
                    if (arm.baseBody != null) foreach (var c in arm.baseBody.GetComponentsInChildren<Collider>()) c.enabled = false;
                    Debug.Log("[LiveIkCheck] (-nocol) ALL arm colliders disabled");
                }
                bool noWorktop = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noworktop") >= 0;
                if (noWorktop) { wc.enabled = false; Debug.Log("[LiveIkCheck] (-noworktop) worktop collider disabled"); }
                else
                {
                    // Which jaw<->arm pairs are NOT ignored? (suspected wrist-blocking collision)
                    var sc = armGo.GetComponent<SelfCollision>();
                    if (arm.gripper != null)
                    {
                        var jawCols = arm.gripper.GetComponentsInChildren<Collider>();
                        var sb = new System.Text.StringBuilder("[LiveIkCheck] jaw colliders=" + jawCols.Length + " NOT-ignored vs: ");
                        foreach (var jc in jawCols)
                            foreach (var ab in arm.jointBodies)
                                foreach (var lc in ab.GetComponentsInChildren<Collider>())
                                {
                                    if (jc == lc) continue;
                                    if (jc.GetComponentInParent<ArticulationBody>() == ab) continue; // same body
                                    if (!Physics.GetIgnoreCollision(jc, lc)) { sb.Append(jc.name + "<->" + ab.name + " "); }
                                }
                        Debug.Log(sb.ToString());
                    }
                    // ALSO: any arm-link<->arm-link pair NOT ignored?
                    var all = new System.Collections.Generic.List<Collider>();
                    if (arm.baseBody != null) foreach (var c in arm.baseBody.GetComponentsInChildren<Collider>()) all.Add(c);
                    foreach (var ab in arm.jointBodies) foreach (var c in ab.GetComponentsInChildren<Collider>()) all.Add(c);
                    var sb2 = new System.Text.StringBuilder("[LiveIkCheck] arm-internal NOT-ignored: ");
                    int cnt = 0;
                    for (int x = 0; x < all.Count; x++) for (int y = x + 1; y < all.Count; y++)
                        if (!Physics.GetIgnoreCollision(all[x], all[y])) { sb2.Append(all[x].name + "/" + all[y].name + " "); cnt++; }
                    sb2.Append("(total " + cnt + ")");
                    Debug.Log(sb2.ToString());
                }

                // ISOLATION: command ONLY wrist_flex to -40 (home pose otherwise) and see if it reaches.
                {
                    ctrl.HardHome(null);
                    for (int i = 0; i < 40; i++) Physics.Simulate(dt);
                    var home = arm.GetJointAngles();
                    var wf = (float[])home.Clone();
                    wf[3] = -40f;   // wrist_flex target
                    for (int i = 0; i < 800; i++) { arm.SetJointTargets(wf); Physics.Simulate(dt); }   // long converge
                    var got = arm.GetJointAngles();
                    Debug.Log($"[LiveIkCheck] wrist_flex isolation: commanded -40, got {got[3]:F1} (others ~home)");
                    // what does the wrist body's collider overlap right now?
                    var wfBody = arm.jointBodies[3];
                    var wfCol = wfBody.GetComponentInChildren<Collider>();
                    if (wfCol != null)
                    {
                        var hits = Physics.OverlapBox(wfCol.bounds.center, wfCol.bounds.extents * 1.1f);
                        var sb = new System.Text.StringBuilder("[LiveIkCheck]   wrist overlaps: ");
                        foreach (var h in hits) if (h != wfCol) sb.Append(h.name + "[L" + h.gameObject.layer + (Physics.GetIgnoreCollision(wfCol,h)?"/ign":"/COL") + "] ");
                        Debug.Log(sb.ToString());
                        Debug.Log($"[LiveIkCheck]   wristCol layer={wfCol.gameObject.layer} armLayer-self-ignored={Physics.GetIgnoreLayerCollision(8,8)}");
                    }
                }

                var goals = new[] {
                    new Vector3(0.10f, 0.12f, 0.28f),
                    new Vector3(0.16f, 0.06f, 0.30f),   // low reach
                    new Vector3(0.0f,  0.16f, 0.30f),
                    new Vector3(-0.12f,0.07f, 0.28f),   // low + left
                    new Vector3(0.14f, 0.05f, 0.30f),   // low + right
                };
                float worst = 0f, sum = 0f; int n = 0;
                foreach (var g in goals)
                {
                    tgt.position = g;
                    // what does the analytic multi-seed solver return for this goal, and where does its FK land?
                    float[] ik = ctrl.IKAnglesFor(g);
                    float fke = ctrl.TestReachWith(ik, g, out Vector3 fkt);
                    Debug.Log($"[LiveIkCheck]   IKAnglesFor -> [{ik[0]:F0},{ik[1]:F0},{ik[2]:F0},{ik[3]:F0}] FKtip {fkt:F3} fkErr {fke*100f:F1}cm");
                    for (int i = 0; i < 240; i++) { ctrl.TickControl(); Physics.Simulate(dt); }
                    var act = arm.GetJointAngles();
                    Debug.Log($"[LiveIkCheck]   ACTUAL joints [{act[0]:F0},{act[1]:F0},{act[2]:F0},{act[3]:F0}] (IK wanted [{ik[0]:F0},{ik[1]:F0},{ik[2]:F0},{ik[3]:F0}])");
                    Vector3 tip = arm.gripper != null ? arm.gripper.TipPosition : arm.endEffector.position;
                    Vector3 ee = arm.endEffector != null ? arm.endEffector.position : tip;
                    float e = (tip - g).magnitude;
                    float eeErr = (ee - g).magnitude;
                    Debug.Log($"[LiveIkCheck] goal {g:F2} -> tip {tip:F3} err {e*100f:F1}cm | EE {ee:F3} eeErr {eeErr*100f:F1}cm tipVsEE {(tip-ee).magnitude*100f:F1}cm");
                    worst = Mathf.Max(worst, e); sum += e; n++;
                }
                float mean = sum / n;
                bool pass = mean < 0.05f && worst < 0.08f;   // live loop should track to a few cm
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
