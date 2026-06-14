using UnityEngine;

namespace ArmSmith.Visualization
{
    /// <summary>
    /// MULTI-GENERATION visualization (TRAINING_REGIMEN MG1 / TR8): draws the last few generations' best
    /// EE-space trajectories at once, newest BRIGHT and older FADED, so the player sees the SPREAD of
    /// evolving behaviour improving over generations — not just the current best. Reads the trainer's
    /// genTrajectories ring buffer and feeds the shared PathVisualizer. Toggle with key 3.
    /// </summary>
    public class MultiGenViz : MonoBehaviour, ITrajectoryProvider
    {
        public EvolutionTrainer trainer;
        public bool vizEnabled = false;

        public string ProviderName => "Generations";
        public bool VizEnabled => vizEnabled && trainer != null;

        public TrajectorySet GetTrajectories()
        {
            if (trainer == null || trainer.genTrajectories.Count == 0) return null;
            var set = new TrajectorySet { source = "generations" };
            int n = trainer.genTrajectories.Count;
            for (int i = 0; i < n; i++)
            {
                var src = trainer.genTrajectories[i];
                float age = (n == 1) ? 1f : i / (float)(n - 1);   // 0 oldest .. 1 newest
                var s = new TrajectorySample(src.points)
                {
                    label = src.label,
                    chosen = (i == n - 1),
                    // newest = bright cyan-green, oldest = dim grey-blue
                    colorOverride = Color.Lerp(new Color(0.3f, 0.4f, 0.55f, 0.25f),
                                               new Color(0.3f, 1f, 0.6f, 0.95f), age)
                };
                set.Add(s);
            }
            return set;
        }
    }
}
