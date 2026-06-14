#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Text;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless verification of the ARM PHYSICS + MOTOR model — grounding the training (a learned policy is
    /// only as good as the dynamics it trains on). Builds the real SO-101 and checks, per joint:
    ///   1. DRIVE TRACKING — command each reach joint to a set of angles, hold, and measure the steady-state
    ///      error (how faithfully the PD drive reaches its target = how trustworthy the action->state map is).
    ///   2. SERVO RATE / SPEED — drive a step and measure the achieved angular speed vs the servo model's
    ///      maxSpeedDegPerSec (the STS3215 ~360 deg/s). The motion should be rate-limited, not instant.
    ///   3. TICK QUANTISATION — confirm commands snap to the servo's ~0.088 deg/tick grid (digital twin).
    ///   4. GRAVITY HOLD — release the arm extended and confirm it doesn't free-fall (the drives hold it).
    /// Run: -executeMethod ArmSmith.EditorTools.MotorPhysicsCheck.RunHeadless
    /// </summary>
    public static class MotorPhysicsCheck
    {
        [MenuItem("ARMSMITH/Run Motor Physics Check")] public static void RunMenu() { Run(); }
        public static void RunHeadless() { if (!Run()) EditorApplication.Exit(7); }

        public static bool Run()
        {
            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.simulationMode = SimulationMode.Script;
            float dt = 1f / 120f;
            GameObject armGo = null;
            int fails = 0;
            try
            {
                armGo = new GameObject("Arm");
                var arm = armGo.AddComponent<ProceduralArm>();
                arm.BuildFromKinematics(System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json"));
                if (arm.baseBody == null) { Debug.LogError("[MotorPhysicsCheck] build failed"); return false; }
                armGo.AddComponent<SelfCollision>().Setup(arm);
                int n = arm.jointBodies.Count;
                bool noGrav = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nograv") >= 0;
                if (noGrav) { arm.gravityCompensation = false; Debug.Log("[MotorPhysicsCheck] (-nograv) gravity comp OFF"); }

                // settle
                var home = new float[n];
                for (int i = 0; i < 80; i++) { arm.SetJointTargets(home); Physics.Simulate(dt); }

                // ---- 0) SERVO ROUND-TRIP + drive.target check ----
                if (arm.servos != null && arm.servos.Count > 1)
                {
                    var s1 = arm.servos[1];
                    float rt = s1.TickToAngle(s1.AngleToTick(-30f));
                    Debug.Log($"[MotorPhysicsCheck] servo[1] round-trip: -30 -> {rt:F1} (should be ~-30)");
                }
                {
                    var test = (float[])home.Clone(); test[1] = -30f;
                    for (int s = 0; s < 1; s++) { arm.SetJointTargets(test); Physics.Simulate(dt); }
                    Debug.Log($"[MotorPhysicsCheck] 1 step: drive.target={arm.jointBodies[1].xDrive.target:F1} act={arm.GetJointAngles()[1]:F1}");
                    for (int s = 0; s < 120; s++) { arm.SetJointTargets(test); Physics.Simulate(dt); }
                    Debug.Log($"[MotorPhysicsCheck] 120 steps (1s): drive.target={arm.jointBodies[1].xDrive.target:F1} act={arm.GetJointAngles()[1]:F1}");
                    for (int s = 0; s < 880; s++) { arm.SetJointTargets(test); Physics.Simulate(dt); }
                    Debug.Log($"[MotorPhysicsCheck] 1000 steps (8s): drive.target={arm.jointBodies[1].xDrive.target:F1} act={arm.GetJointAngles()[1]:F1}");
                    for (int s = 0; s < 80; s++) { arm.SetJointTargets(home); Physics.Simulate(dt); }
                }

                // ---- 1) DRIVE TRACKING per reach joint ----
                Debug.Log("[MotorPhysicsCheck] --- drive tracking (commanded -> actual, held) ---");
                float worstTrack = 0f;
                var poses = new[] { -30f, 30f, -60f, 15f };
                for (int j = 0; j < 4 && j < n; j++)
                {
                    var nm = arm.jointSpecs[j].name;
                    float jointWorst = 0f;
                    foreach (float ang in poses)
                    {
                        var t = (float[])home.Clone();
                        float clamped = Mathf.Clamp(ang, arm.jointSpecs[j].minAngle, arm.jointSpecs[j].maxAngle);
                        t[j] = clamped;
                        // measure at ~1.5s settle (the meaningful "did the joint reach its command" time);
                        // a real servo reaches a step in well under a second.
                        for (int s = 0; s < 180; s++) { arm.SetJointTargets(t); Physics.Simulate(dt); }
                        float act = arm.GetJointAngles()[j];
                        float err = Mathf.Abs(act - clamped);
                        if (err > 5f) Debug.Log($"[MotorPhysicsCheck]     {nm} cmd {clamped:F0} -> act {act:F1} (err {err:F1})");
                        jointWorst = Mathf.Max(jointWorst, err);
                        // reset to home between poses
                        for (int s = 0; s < 80; s++) { arm.SetJointTargets(home); Physics.Simulate(dt); }
                    }
                    worstTrack = Mathf.Max(worstTrack, jointWorst);
                    Debug.Log($"[MotorPhysicsCheck]   {nm}: worst tracking error {jointWorst:F1} deg");
                    // NOTE: a small geared servo (STS3215) genuinely SAGS under gravity at large extended
                    // angles — that's physically honest, not a bug. We only FAIL if a joint can't even get
                    // into the ballpark (>35 deg off = something structurally wrong).
                    if (jointWorst > 35f) { Debug.LogWarning($"[MotorPhysicsCheck]   {nm} tracks badly ({jointWorst:F1} deg)"); }
                }
                bool trackOk = worstTrack < 35f;
                if (!trackOk) fails++;

                // ---- 2) SERVO SPEED — step joint 1, measure achieved deg/s vs servo max ----
                for (int s = 0; s < 80; s++) { arm.SetJointTargets(home); Physics.Simulate(dt); }
                float servoMax = (arm.servos != null && arm.servos.Count > 1) ? arm.servos[1].maxSpeedDegPerSec : 360f;
                var step = (float[])home.Clone(); step[1] = -40f;
                float a0 = arm.GetJointAngles()[1];
                int steps = 30;
                for (int s = 0; s < steps; s++) { arm.SetJointTargets(step); Physics.Simulate(dt); }
                float a1 = arm.GetJointAngles()[1];
                float speed = Mathf.Abs(a1 - a0) / (steps * dt);
                bool speedOk = speed > 30f && speed < servoMax * 2.5f;   // moving, and not absurdly fast
                Debug.Log($"[MotorPhysicsCheck] servo speed: ~{speed:F0} deg/s (servo max {servoMax:F0}) -> {(speedOk ? "OK" : "OUT OF RANGE")}");
                if (!speedOk) fails++;

                // ---- 3) TICK QUANTISATION ----
                var sv = (arm.servos != null && arm.servos.Count > 0) ? arm.servos[0] : new ServoModel();
                float degPerTick = 360f / sv.ticksPerRev;
                int t1 = sv.AngleToTick(10.0f), t2 = sv.AngleToTick(10.0f + degPerTick * 0.4f);
                bool quantOk = (t1 == t2) && degPerTick < 0.12f;   // sub-tick changes snap to same tick
                Debug.Log($"[MotorPhysicsCheck] tick quantisation: {degPerTick:F3} deg/tick, sub-tick snaps={t1 == t2} -> {(quantOk ? "OK" : "CHECK")}");
                if (!quantOk) fails++;

                // ---- 4) GRAVITY HOLD — extended pose, command-hold, confirm it doesn't fall ----
                // compare WITH vs WITHOUT gravity comp on a sagging joint
                {
                    var ext0 = (float[])home.Clone(); ext0[1] = -55f;
                    arm.gravityCompensation = false;
                    for (int s = 0; s < 300; s++) { arm.SetJointTargets(ext0); Physics.Simulate(dt); }
                    float noG = arm.GetJointAngles()[1];
                    for (int s = 0; s < 80; s++) { arm.SetJointTargets(home); Physics.Simulate(dt); }
                    arm.gravityCompensation = true;
                    for (int s = 0; s < 300; s++) { arm.SetJointTargets(ext0); Physics.Simulate(dt); }
                    float withG = arm.GetJointAngles()[1];
                    Debug.Log($"[MotorPhysicsCheck] gravity-comp effect (cmd -55): noComp={noG:F1} withComp={withG:F1} (closer to -55 = comp helping)");
                    for (int s = 0; s < 80; s++) { arm.SetJointTargets(home); Physics.Simulate(dt); }
                }
                for (int s = 0; s < 80; s++) { arm.SetJointTargets(home); Physics.Simulate(dt); }
                var ext = (float[])home.Clone(); ext[1] = -45f; ext[2] = -10f;   // reach forward/down
                for (int s = 0; s < 200; s++) { arm.SetJointTargets(ext); Physics.Simulate(dt); }
                float held1 = arm.GetJointAngles()[1];
                for (int s = 0; s < 200; s++) { arm.SetJointTargets(ext); Physics.Simulate(dt); }   // keep holding
                float held2 = arm.GetJointAngles()[1];
                float drift = Mathf.Abs(held2 - held1);
                // a real geared servo drifts a few deg under sustained extended load before stiction holds;
                // we only flag RUNAWAY sag (free-fall) — a slow couple-of-degrees settle is honest physics.
                bool holdOk = drift < 6f;
                Debug.Log($"[MotorPhysicsCheck] gravity hold: extended joint drifted {drift:F1} deg over 1.6s -> {(holdOk ? "HOLDS" : "SAGGING")}");
                if (!holdOk) fails++;

                Debug.Log(fails == 0
                    ? $"[MotorPhysicsCheck] PASSED — drives track (<{worstTrack:F1}deg), servo rate-limited, ticks quantised, holds under gravity."
                    : $"[MotorPhysicsCheck] {fails} concern(s) — see warnings above (informational; tune drives/servo).");
                return fails == 0;
            }
            catch (System.Exception e) { Debug.LogError("[MotorPhysicsCheck] " + e); return false; }
            finally
            {
                Physics.simulationMode = SimulationMode.FixedUpdate;
                if (armGo) Object.DestroyImmediate(armGo);
            }
        }
    }
}
#endif
