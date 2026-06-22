using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// A pluggable add-on sensor module. Each sensor produces an observation vector and names its
    /// channels, so players can attach/detach modules (IMU, lidar, rangefinder, depth, eFlesh tactile,
    /// motor encoders, ...) and compare which information helps which task. See design ROADMAP "Sensors".
    /// </summary>
    public interface ISensor
    {
        string Name { get; }
        bool Enabled { get; set; }
        /// <summary>Human-readable channel names (length == Observe().Length).</summary>
        string[] Channels { get; }
        /// <summary>Current observation values (fixed length).</summary>
        float[] Observe();
    }

    /// <summary>
    /// F-r2 — global sensor REALISM settings (noise + latency). Real sensors are noisy and lag; toggling
    /// this on makes the observation the policy trains/acts on imperfect, so learned behaviour is robust to
    /// real-hardware imperfection (sim-to-real). One static config drives every module via SensorBase.
    /// </summary>
    public static class SensorRealism
    {
        public static bool enabled = false;
        /// <summary>Gaussian noise std-dev as a FRACTION of each channel's value magnitude (relative) plus
        /// a small absolute floor — keeps near-zero channels from being noise-free.</summary>
        public static float noiseRelative = 0.01f;     // 1% of value
        public static float noiseAbsolute = 0.003f;    // small absolute floor
        /// <summary>Latency in observation FRAMES (the policy sees a slightly stale reading).</summary>
        public static int latencyFrames = 1;

        static System.Random _rng = new System.Random(1234);
        public static float Gauss()
        {
            // Box-Muller
            double u1 = 1.0 - _rng.NextDouble(), u2 = 1.0 - _rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }
    }

    /// <summary>Base MonoBehaviour sensor with common plumbing.</summary>
    public abstract class SensorBase : MonoBehaviour, ISensor
    {
        [SerializeField] bool enabledModule = true;
        public bool Enabled { get => enabledModule; set => enabledModule = value; }
        public abstract string Name { get; }
        public abstract string[] Channels { get; }
        public abstract float[] Observe();

        protected ProceduralArm arm;
        public virtual void Bind(ProceduralArm a) { arm = a; }

        // F-r2: latency ring buffer of recent raw observations (per module).
        readonly System.Collections.Generic.Queue<float[]> _history = new System.Collections.Generic.Queue<float[]>();

        /// <summary>The observation WITH realism applied (noise + latency) when SensorRealism.enabled.
        /// Falls back to the clean Observe() otherwise. The SensorHub uses this for the training/act vector.</summary>
        public float[] ObserveNoisy()
        {
            float[] raw = Observe();
            if (!SensorRealism.enabled || raw == null || raw.Length == 0) return raw;

            // latency: push the latest, pop a stale frame to return
            _history.Enqueue((float[])raw.Clone());
            int want = Mathf.Max(0, SensorRealism.latencyFrames) + 1;
            while (_history.Count > want) _history.Dequeue();
            float[] delayed = _history.Count >= want ? _history.Peek() : raw;

            // noise: add Gaussian (relative + absolute) to a copy so we never mutate the cached frame
            var outv = new float[delayed.Length];
            for (int i = 0; i < delayed.Length; i++)
            {
                float sigma = Mathf.Abs(delayed[i]) * SensorRealism.noiseRelative + SensorRealism.noiseAbsolute;
                outv[i] = delayed[i] + SensorRealism.Gauss() * sigma;
            }
            return outv;
        }
    }
}
