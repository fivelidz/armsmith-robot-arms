using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Pick-and-place task (T1). A cube must be moved onto a target pad.
    /// Provides dense + sparse reward used by both the player score HUD and the training/evolution layer.
    /// Reward shaping per design/GAME_DESIGN.md section 3 and research/manipulation_repos (FetchPickAndPlace).
    /// </summary>
    public class TaskManager : MonoBehaviour
    {
        public ProceduralArm arm;
        public Transform cube;
        public Transform target;       // target pad center
        public float targetRadius = 0.05f;
        public float tableY = 0.0f;

        public Vector3 cubeSpawn = new Vector3(0.15f, 0.025f, 0.30f);
        public Vector3 targetSpawn = new Vector3(-0.15f, 0.001f, 0.30f);

        Rigidbody cubeRb;
        float elapsed;
        public float timeLimit = 30f;
        bool active = true;

        public float LastReward { get; private set; }
        public bool Succeeded { get; private set; }
        public float Elapsed => elapsed;

        public void Bind(ProceduralArm a, Transform cubeT, Transform targetT)
        {
            arm = a; cube = cubeT; target = targetT;
            cubeRb = cube.GetComponent<Rigidbody>();
            ResetTask();
        }

        public void ResetTask()
        {
            elapsed = 0f; active = true; Succeeded = false;
            if (cube != null)
            {
                cube.position = cubeSpawn;
                cube.rotation = Quaternion.identity;
                if (cubeRb != null) { cubeRb.linearVelocity = Vector3.zero; cubeRb.angularVelocity = Vector3.zero; }
            }
            if (target != null) target.position = targetSpawn;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Return)) active = true;
            if (Input.GetKeyDown(KeyCode.Escape)) ResetTask();
            if (active) elapsed += Time.deltaTime;
            LastReward = ComputeReward(out bool success);
            Succeeded = success;
            if (success || elapsed > timeLimit) active = false;
        }

        /// <summary>Dense + sparse reward. Public so the trainer can query per-step fitness.</summary>
        public float ComputeReward(out bool success)
        {
            success = false;
            if (cube == null || target == null || arm == null) return 0f;

            Vector3 ee = arm.gripper != null ? arm.gripper.TipPosition : arm.endEffector.position;
            float gripToCube = Vector3.Distance(ee, cube.position);
            Vector3 flatCube = cube.position; flatCube.y = target.position.y;
            float cubeToTarget = Vector3.Distance(flatCube, target.position);

            bool grasped = gripToCube < 0.05f && arm.gripper != null && arm.gripper.closeAmount > 0.5f;

            // Dense shaping: approach the cube, then carry it to target.
            float dense = -gripToCube * 0.5f;
            if (grasped) dense += 0.5f - cubeToTarget;

            float reward = dense;

            // Success: cube center within target radius, low, and at rest.
            bool onTarget = cubeToTarget < targetRadius && cube.position.y < tableY + 0.06f;
            bool atRest = cubeRb == null || cubeRb.linearVelocity.magnitude < 0.03f;
            if (onTarget && atRest)
            {
                success = true;
                reward += 10f;                       // sparse success bonus
                reward += Mathf.Max(0f, (timeLimit - elapsed)) * 0.1f; // speed bonus
            }

            // Penalty: cube knocked off table.
            if (cube.position.y < tableY - 0.1f) reward -= 5f;

            return reward;
        }

        /// <summary>Full fitness for evolution (adds energy penalty). Call at episode end.</summary>
        public float EpisodeFitness(float energyUsed)
        {
            ComputeReward(out bool success);
            float f = LastReward - energyUsed * 0.01f;
            if (success) f += 5f;
            return f;
        }
    }
}
