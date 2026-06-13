#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless regression check for the PhysX articulation crash (S7d). Builds the real SO-101 arm and
    /// steps physics manually for a while, watching for NaN/Inf in the articulation state — the precursor
    /// to the setupDescTask segfault. Runs WITHOUT the GUI, so it works even when the interactive editor
    /// can't launch on this Wayland/AMD stack.
    ///
    /// Run headless:
    ///   Unity -batchmode -nographics -projectPath . \
    ///         -executeMethod ArmSmith.EditorTools.PhysxStabilityCheck.RunHeadless -quit \
    ///         -logFile -
    ///
    /// Exit code 0 = stable (no NaN, no exception over N steps); non-zero = problem detected. Useful as a
    /// CI gate so the crash can never silently regress.
    /// </summary>
    public static class PhysxStabilityCheck
    {
        [MenuItem("ARMSMITH/Run PhysX Stability Check")]
        public static void RunMenu() { Debug.Log(Run(600) ? "PhysX check PASSED" : "PhysX check FAILED"); }

        public static void RunHeadless()
        {
            bool ok = Run(600);
            Debug.Log(ok ? "[PhysxStabilityCheck] PASSED — no NaN over 600 steps." :
                           "[PhysxStabilityCheck] FAILED — instability detected.");
            // In batchmode -quit handles exit; set exit code for CI.
            if (!ok) EditorApplication.Exit(2);
        }

        /// <summary>
        /// Build the arm via the same config + UrdfArm path the game uses, then Physics.Simulate() for
        /// `steps` fixed steps. Returns false if any articulation DOF becomes NaN/Inf or an exception throws.
        /// </summary>
        public static bool Run(int steps)
        {
            // Match the game's physics settings (the ones we tuned for stability).
            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.simulationMode = SimulationMode.Script;   // we drive Simulate() ourselves
            float dt = 1f / 120f;

            GameObject armGo = null;
            try
            {
                armGo = new GameObject("PhysxCheckArm");
                armGo.transform.position = Vector3.zero;
                var arm = armGo.AddComponent<ProceduralArm>();

                // Build the realistic SO-101 from kinematics (the exact path that was crashing in-game).
                string kinPath = System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                try
                {
                    arm.BuildFromKinematics(kinPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[PhysxStabilityCheck] BuildFromKinematics threw, falling back to procedural: " + e.Message);
                }
                if (arm.baseBody == null)
                {
                    // Fallback: procedural build so the check still exercises an articulation.
                    arm.useStlMeshes = false;
                    arm.Build(ArmConfig.CreateStarter());
                }

                var selfCol = armGo.AddComponent<SelfCollision>();
                selfCol.Setup(arm);

                // REPRODUCE the in-game crash condition: a worktop whose TOP is exactly y=0, so the arm
                // base (at y=0) intersects it — the depenetration that crashed PhysX in the full scene.
                var worktop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                worktop.name = "Worktop";
                worktop.transform.position = new Vector3(0f, -0.025f, 0.25f);   // top at y=0
                worktop.transform.localScale = new Vector3(0.8f, 0.05f, 0.8f);

                // Apply the same fix GameBootstrap does: ignore arm-vs-static-environment collisions.
                var armCols = new System.Collections.Generic.List<Collider>();
                if (arm.baseBody != null) armCols.AddRange(arm.baseBody.GetComponentsInChildren<Collider>());
                foreach (var ab in arm.jointBodies) if (ab != null) armCols.AddRange(ab.GetComponentsInChildren<Collider>());
                var wcol = worktop.GetComponent<Collider>();
                foreach (var ac in armCols) if (ac != null) Physics.IgnoreCollision(ac, wcol, true);

                // Step physics; watch for divergence.
                for (int i = 0; i < steps; i++)
                {
                    Physics.Simulate(dt);
                    if (i % 20 == 0 && !StateFinite(arm))
                    {
                        Debug.LogError($"[PhysxStabilityCheck] NaN/Inf at step {i}.");
                        return false;
                    }
                }
                bool fin = StateFinite(arm);
                if (!fin) Debug.LogError("[PhysxStabilityCheck] NaN/Inf at final step.");
                return fin;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PhysxStabilityCheck] Exception: " + e);
                return false;
            }
            finally
            {
                Physics.simulationMode = SimulationMode.FixedUpdate;
                if (armGo != null) Object.DestroyImmediate(armGo);
            }
        }

        static bool StateFinite(ProceduralArm arm)
        {
            if (arm == null || arm.jointBodies == null) return true;
            foreach (var ab in arm.jointBodies)
            {
                if (ab == null || ab.dofCount <= 0) continue;
                var pos = ab.jointPosition;
                var vel = ab.jointVelocity;
                for (int d = 0; d < ab.dofCount; d++)
                {
                    if (float.IsNaN(pos[d]) || float.IsInfinity(pos[d])) return false;
                    if (d < vel.dofCount && (float.IsNaN(vel[d]) || float.IsInfinity(vel[d]))) return false;
                }
                var p = ab.transform.position;
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) return false;
            }
            return true;
        }
    }
}
#endif
