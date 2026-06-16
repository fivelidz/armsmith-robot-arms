using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Persistence for the evolutionary trainer: saves/loads "CREATIONS" (the best genome of each
    /// generation, with metadata) and full CHECKPOINTS (resumable population + history). This is what lets
    /// the Generations UI browse past generations, replay a saved best in-scene, and resume training across
    /// sessions — none of which existed before (only baked waypoint exports did).
    ///
    /// Files live under Application.persistentDataPath/Evolution/:
    ///   creations.json      — rolling list of best-per-generation creations (CreationLibrary)
    ///   checkpoint.json     — latest resumable training checkpoint (EvoCheckpoint)
    /// Both use JsonUtility (MotionGenome/PolicyGenome are [Serializable] with flat arrays).
    /// </summary>

    [Serializable]
    public class Creation
    {
        public int generation;
        public float fitness;
        public float successRate;
        public string backend;        // "motion" | "policy"
        public string scenario;       // scenario name at capture time
        public string timestamp;      // yyyyMMdd_HHmmss
        public string label;          // user/auto label
        // exactly one of these is populated depending on backend:
        public MotionGenome motion;   // for backend == "motion"
        public PolicyGenome policy;   // for backend == "policy"
    }

    [Serializable]
    public class CreationLibrary
    {
        public List<Creation> creations = new List<Creation>();
        public int maxKeep = 200;     // cap so the file can't grow unbounded
    }

    [Serializable]
    public class EvoCheckpoint
    {
        public int generation;
        public string backend;        // "motion" | "policy"
        public string scenario;
        public string timestamp;
        public TrainingConfig config;
        public List<MotionGenome> population = new List<MotionGenome>();
        public List<PolicyGenome> policyPop = new List<PolicyGenome>();
        public List<float> bestHistory = new List<float>();
        public List<float> meanHistory = new List<float>();
        public List<float> successHistory = new List<float>();
    }

    public static class EvolutionStore
    {
        public static string Dir => Path.Combine(Application.persistentDataPath, "Evolution");
        public static string CreationsPath => Path.Combine(Dir, "creations.json");
        public static string CheckpointPath => Path.Combine(Dir, "checkpoint.json");

        static void EnsureDir() => Directory.CreateDirectory(Dir);

        // ── Creations ──────────────────────────────────────────────────────────────────────────────
        public static CreationLibrary LoadLibrary()
        {
            try
            {
                if (!File.Exists(CreationsPath)) return new CreationLibrary();
                var lib = JsonUtility.FromJson<CreationLibrary>(File.ReadAllText(CreationsPath));
                return lib ?? new CreationLibrary();
            }
            catch (Exception e) { Debug.LogWarning("[EvolutionStore] load creations failed: " + e.Message); return new CreationLibrary(); }
        }

        public static void SaveLibrary(CreationLibrary lib)
        {
            try
            {
                EnsureDir();
                while (lib.creations.Count > lib.maxKeep) lib.creations.RemoveAt(0);
                File.WriteAllText(CreationsPath, JsonUtility.ToJson(lib, true));
            }
            catch (Exception e) { Debug.LogWarning("[EvolutionStore] save creations failed: " + e.Message); }
        }

        /// <summary>Append a creation and persist. Returns the saved creation.</summary>
        public static Creation AddCreation(Creation c)
        {
            var lib = LoadLibrary();
            lib.creations.Add(c);
            SaveLibrary(lib);
            return c;
        }

        // ── Checkpoint (resume training) ─────────────────────────────────────────────────────────────
        public static void SaveCheckpoint(EvoCheckpoint cp)
        {
            try { EnsureDir(); File.WriteAllText(CheckpointPath, JsonUtility.ToJson(cp, true)); }
            catch (Exception e) { Debug.LogWarning("[EvolutionStore] save checkpoint failed: " + e.Message); }
        }

        public static EvoCheckpoint LoadCheckpoint()
        {
            try
            {
                if (!File.Exists(CheckpointPath)) return null;
                return JsonUtility.FromJson<EvoCheckpoint>(File.ReadAllText(CheckpointPath));
            }
            catch (Exception e) { Debug.LogWarning("[EvolutionStore] load checkpoint failed: " + e.Message); return null; }
        }

        public static bool HasCheckpoint() => File.Exists(CheckpointPath);

        public static string Stamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss");
    }
}
