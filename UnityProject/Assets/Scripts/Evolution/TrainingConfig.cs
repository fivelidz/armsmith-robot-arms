using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// The single shared configuration for the training regimen (design/specs/TRAINING_REGIMEN.md). The
    /// trainer reads it; the Training + Conditions UI panels write it. Captures: which MODEL/backend to
    /// train, the curriculum difficulty + randomization strength, the reward-shaping weights, which sensor
    /// MODULES feed the observation (model inclusion/exclusion of information), and the GA hyperparameters.
    /// </summary>
    [System.Serializable]
    public class TrainingConfig
    {
        // ---- backend (the "model" to train) ----
        public enum Backend { MotionGA, SensorPolicy, Diffusion }
        public Backend backend = Backend.MotionGA;

        // ---- curriculum ----
        [Range(0f, 1f)] public float difficulty = 0.3f;          // 0 reach .. 1 scrambled-world pick-place
        [Range(0f, 1f)] public float randomization = 0.2f;       // object/scene randomization STRENGTH
        public bool autoCurriculum = true;                       // bump difficulty when success-rate high
        [Range(0.1f, 1f)] public float advanceSuccessRate = 0.6f;

        // ---- reward shaping weights ----
        public float wReach   = 1.0f;     // -dist(tip, target)
        public float wGrasp   = 2.0f;     // + when holding
        public float wPlace   = 1.0f;     // -dist(object, goal) once grasped
        public float wSuccess = 5.0f;     // + on scenario success
        public float wEnergy  = 0.002f;   // - sum|joint deltas| (smooth/efficient)
        public float wSelfPen = 1.0f;     // - self-penetration
        public float wOob     = 5.0f;     // - if object leaves the table

        // ---- sensor module mask (which info the policy/diffusion observes) ----
        // names match the SensorHub modules; empty/true = enabled.
        public bool useMotorEncoders = true;
        public bool useTaskState     = true;
        public bool useImu           = true;
        public bool useRangeFinder   = true;
        public bool useLidar         = true;
        public bool useDepthCamera   = false;   // heavy; off by default for fast GA/policy training
        public bool useTactile       = true;

        // ---- GA / policy hyperparameters ----
        public int populationSize = 16;
        public int elite = 3;
        [Range(0f, 1f)] public float mutationRate = 0.3f;
        public float mutationSigma = 25f;
        public int keysPerGenome = 4;        // Motion-GA keyframes
        public int policyHidden = 24;        // Sensor-Policy MLP hidden units
        public int evalResets = 3;           // randomized resets per genome (fitness = mean)
        public float rolloutSpeedup = 4f;    // headless eval time-scale

        public TrainingConfig Clone() => (TrainingConfig)MemberwiseClone();

        /// <summary>Apply the sensor mask to a SensorHub (model inclusion/exclusion of information).</summary>
        public void ApplySensorMask(SensorHub hub)
        {
            if (hub == null) return;
            hub.SetEnabled("MotorEncoders", useMotorEncoders);
            hub.SetEnabled("TaskState", useTaskState);
            hub.SetEnabled("IMU", useImu);
            hub.SetEnabled("RangeFinder", useRangeFinder);
            hub.SetEnabled("Lidar2D", useLidar);
            hub.SetEnabled("DepthCamera", useDepthCamera);
            hub.SetEnabled("EFleshTactile", useTactile);
        }

        /// <summary>Curriculum level name from difficulty (for the UI).</summary>
        public string LevelName()
        {
            if (difficulty < 0.2f) return "L0 Reach";
            if (difficulty < 0.4f) return "L1 Reach+Grasp";
            if (difficulty < 0.6f) return "L2 PickPlace fixed";
            if (difficulty < 0.8f) return "L3 PickPlace random";
            return "L4 Scrambled world";
        }
    }
}
