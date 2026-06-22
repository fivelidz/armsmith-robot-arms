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

        // ---- per-term reward ENABLE flags (Isaac Lab RewardsCfg pattern: named, individually toggleable) ----
        public bool eReach = true, eGrasp = true, ePlace = true, eSuccess = true, eEnergy = true, eSelfPen = true, eOob = true;

        // ---- domain-randomization RANGES (Isaac Lab EventsCfg: named per-axis min/max, scaled by the
        // global `randomization` master). Each has an enable flag. Conservative sim-to-real defaults. ----
        public bool drSpawnPos = true;   public float drSpawnPosM = 0.06f;        // ± metres
        public bool drMass     = true;   public float drMassLo = 0.85f, drMassHi = 1.15f;   // × factor
        public bool drFriction = true;   public float drFrictionLo = 0.7f, drFrictionHi = 1.3f;
        public bool drYaw      = true;   public float drYawDeg = 45f;             // ± degrees
        public bool drSensorNoise = false;                                         // ties to SensorRealism

        // ---- termination / success (Isaac Lab TerminationsCfg: termination ≠ success) ----
        public float timeoutSec = 20f;          // episode time limit
        public bool termOnOob = true;           // end episode if object leaves the table
        public float successHoldSec = 0.3f;     // object must rest in goal this long to count

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

        // ── presets (Isaac Lab task registry / RoboSuite controller configs pattern) ──────────────────
        public enum Preset { QuickGA, Robust, SimToReal, ReachDebug }

        /// <summary>Overwrite this config with a named preset (solves the blank-page problem).</summary>
        public void ApplyPreset(Preset p)
        {
            switch (p)
            {
                case Preset.QuickGA:   // fast iteration
                    backend = Backend.MotionGA; difficulty = 0.3f; randomization = 0.1f; autoCurriculum = true;
                    populationSize = 16; elite = 3; mutationRate = 0.3f; evalResets = 2; rolloutSpeedup = 6f;
                    drSensorNoise = false; break;
                case Preset.Robust:    // heavy randomization for generalisation
                    backend = Backend.SensorPolicy; difficulty = 0.6f; randomization = 0.8f; autoCurriculum = true;
                    populationSize = 32; elite = 5; mutationRate = 0.3f; evalResets = 5; rolloutSpeedup = 4f;
                    drSpawnPos = drMass = drFriction = drYaw = true; drSensorNoise = true; break;
                case Preset.SimToReal: // conservative ranges matching the real rig
                    backend = Backend.SensorPolicy; difficulty = 0.5f; randomization = 0.4f; autoCurriculum = true;
                    populationSize = 24; elite = 4; evalResets = 4; rolloutSpeedup = 4f;
                    drSpawnPosM = 0.05f; drMassLo = 0.9f; drMassHi = 1.1f; drFrictionLo = 0.8f; drFrictionHi = 1.2f;
                    drSensorNoise = true; break;
                case Preset.ReachDebug:  // simplest task, no randomization, fast
                    backend = Backend.MotionGA; difficulty = 0.0f; randomization = 0f; autoCurriculum = false;
                    populationSize = 12; elite = 3; evalResets = 1; rolloutSpeedup = 8f;
                    drSpawnPos = drMass = drFriction = drYaw = drSensorNoise = false; break;
            }
        }

        public static string PresetName(Preset p)
        {
            switch (p)
            {
                case Preset.QuickGA: return "Quick GA (fast)";
                case Preset.Robust: return "Robust (heavy DR)";
                case Preset.SimToReal: return "Sim-to-real (conservative)";
                default: return "Reach-only debug";
            }
        }

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
