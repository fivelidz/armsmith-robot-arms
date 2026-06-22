using System;
using System.Collections.Generic;
using System.Text;

namespace ArmSmith
{
    /// <summary>
    /// S10 — the MODULE ADVISOR: comparative analytics over sensor SETS. As the player trains with different
    /// sensor masks (ablation runs), this records (task, sensor-set) -> best success rate / fitness, then
    /// ranks them so it can recommend the best-performing sensor set for each task ("you only needed encoders
    /// + tactile for TrayToTray"). Pure C#, no MonoBehaviour, so it's headless-testable and persists in-session.
    /// </summary>
    public static class ModuleAdvisor
    {
        public class Record
        {
            public string task;
            public string sensorSet;     // canonical sorted list of enabled module names
            public int channels;
            public float bestSuccess;    // best success-rate observed for this (task, set)
            public float bestFitness;
            public int samples;
        }

        static readonly List<Record> _records = new List<Record>();

        public static IReadOnlyList<Record> Records => _records;

        public static void Clear() => _records.Clear();

        /// <summary>Canonical key for a sensor mask (sorted module names joined).</summary>
        public static string SetKey(TrainingConfig cfg)
        {
            var on = new List<string>();
            if (cfg.useMotorEncoders) on.Add("Enc");
            if (cfg.useTaskState) on.Add("Task");
            if (cfg.useImu) on.Add("IMU");
            if (cfg.useRangeFinder) on.Add("Range");
            if (cfg.useLidar) on.Add("Lidar");
            if (cfg.useDepthCamera) on.Add("Depth");
            if (cfg.useTactile) on.Add("Tactile");
            on.Sort();
            return on.Count == 0 ? "(none)" : string.Join("+", on);
        }

        /// <summary>Record a training result for the current task + sensor set. Keeps the BEST per (task,set).</summary>
        public static void RecordResult(string task, TrainingConfig cfg, float successRate, float fitness, int channels)
        {
            string set = SetKey(cfg);
            var r = _records.Find(x => x.task == task && x.sensorSet == set);
            if (r == null)
            {
                r = new Record { task = task, sensorSet = set, channels = channels, bestSuccess = successRate, bestFitness = fitness, samples = 1 };
                _records.Add(r);
            }
            else
            {
                r.samples++;
                r.channels = channels;
                if (successRate > r.bestSuccess) r.bestSuccess = successRate;
                if (fitness > r.bestFitness) r.bestFitness = fitness;
            }
        }

        /// <summary>Best sensor set for a task (highest success, tie-break: fewer channels = simpler/cheaper).</summary>
        public static Record Recommend(string task)
        {
            Record best = null;
            foreach (var r in _records)
            {
                if (r.task != task) continue;
                if (best == null
                    || r.bestSuccess > best.bestSuccess + 1e-4f
                    || (Math.Abs(r.bestSuccess - best.bestSuccess) <= 1e-4f && r.channels < best.channels))
                    best = r;
            }
            return best;
        }

        /// <summary>All records for a task, ranked best-first (for the UI advisor panel).</summary>
        public static List<Record> Ranked(string task)
        {
            var list = _records.FindAll(r => r.task == task);
            list.Sort((a, b) =>
            {
                int c = b.bestSuccess.CompareTo(a.bestSuccess);
                if (c != 0) return c;
                c = a.channels.CompareTo(b.channels);       // fewer channels wins ties (simpler rig)
                if (c != 0) return c;
                return b.bestFitness.CompareTo(a.bestFitness);
            });
            return list;
        }

        public static string Summary(string task)
        {
            var ranked = Ranked(task);
            if (ranked.Count == 0) return $"{task}: no ablation runs yet — train with different sensor masks to compare.";
            var sb = new StringBuilder();
            sb.AppendLine($"MODULE ADVISOR — {task} (best sensor sets):");
            for (int i = 0; i < ranked.Count && i < 8; i++)
            {
                var r = ranked[i];
                sb.AppendLine($"  {i + 1}. {r.sensorSet,-28} success {r.bestSuccess * 100f,5:F0}%  ch {r.channels,2}  (n={r.samples})");
            }
            var rec = Recommend(task);
            if (rec != null) sb.AppendLine($"  -> recommended: {rec.sensorSet} ({rec.bestSuccess * 100f:F0}% success, {rec.channels} channels)");
            return sb.ToString();
        }
    }
}
