using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Pillar K1 — spawns and owns N robot arms in one scene, each with its own ArticulationBody chain,
    /// ArmController and RobotAgent, all sharing the single WorldBlackboard (K2). Arms are placed at offset
    /// bases facing a common workspace so they can hand objects between each other (K3).
    ///
    /// This is the scene-side composition root for multi-robot scenarios. It deliberately reuses the exact
    /// build path GameBootstrap uses for arm #1 (ProceduralArm.BuildFromKinematics + SelfCollision +
    /// ArmController) so every arm is a faithful SO-101 twin.
    /// </summary>
    public class MultiRobotManager : MonoBehaviour
    {
        public struct Spawned
        {
            public string id;
            public ProceduralArm arm;
            public ArmController controller;
            public RobotAgent agent;
            public Transform ikTarget;
        }

        public readonly List<Spawned> robots = new System.Collections.Generic.List<Spawned>();

        /// <summary>Build `count` arms. Bases are spread along X and rotated to face the shared centre so
        /// their workspaces overlap (a hand-off zone). Returns the spawned set.</summary>
        public List<Spawned> Spawn(int count, string kinematicsPath, Vector3 center, float spacing = 0.5f)
        {
            for (int i = 0; i < count; i++)
            {
                string id = $"arm{i + 1}";
                var armGo = new GameObject($"Arm_{id}");
                // place bases on a line either side of centre, facing inward
                float x = center.x + (i - (count - 1) * 0.5f) * spacing;
                armGo.transform.position = new Vector3(x, 0f, center.z - 0.30f);
                // face the shared centre
                Vector3 toCenter = (new Vector3(center.x, 0f, center.z) - armGo.transform.position);
                if (toCenter.sqrMagnitude > 1e-4f) armGo.transform.rotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);

                var arm = armGo.AddComponent<ProceduralArm>();
                arm.BuildFromKinematics(kinematicsPath);
                if (arm.baseBody == null) { Debug.LogError($"[MultiRobotManager] {id} build failed"); Object.Destroy(armGo); continue; }

                var selfCol = armGo.AddComponent<SelfCollision>();
                selfCol.Setup(arm);

                var tgt = new GameObject($"IKTarget_{id}").transform;
                var ctrl = armGo.AddComponent<ArmController>();
                ctrl.Bind(arm, tgt, null);
                ctrl.mouseFollow = false;
                ctrl.mode = ArmController.Mode.Manual;

                var agent = armGo.AddComponent<RobotAgent>();
                agent.Bind(id, arm);

                robots.Add(new Spawned { id = id, arm = arm, controller = ctrl, agent = agent, ikTarget = tgt });
            }
            Debug.Log($"[MultiRobotManager] spawned {robots.Count} arms sharing the WorldBlackboard.");
            return robots;
        }

        /// <summary>Ignore inter-arm collisions between arm bodies (so two arms occupying overlapping
        /// reach don't explode the solver when they pass near each other). Coordination (yield/handoff)
        /// handles the high-level avoidance; this keeps the physics stable.</summary>
        public void IgnoreInterArmCollisions()
        {
            for (int a = 0; a < robots.Count; a++)
                for (int b = a + 1; b < robots.Count; b++)
                {
                    var ca = robots[a].arm.baseBody.GetComponentsInChildren<Collider>();
                    var cb = robots[b].arm.baseBody.GetComponentsInChildren<Collider>();
                    foreach (var x in ca) foreach (var y in cb) if (x && y) Physics.IgnoreCollision(x, y, true);
                }
        }
    }
}
