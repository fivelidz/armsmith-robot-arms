using System;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// A motion-primitive genome: a small set of keyframes, each = target joint angles (deg) + gripper +
    /// hold time. The arm interpolates between keyframes during a rollout. This is the unit the
    /// evolutionary trainer mutates/crosses over (research/learning_evolution: CMA-ES/GA over motion params).
    /// Compact + interpretable, and converts directly to an exportable waypoint trajectory for the real arm.
    /// </summary>
    [Serializable]
    public class MotionKey
    {
        public float[] angles;   // per-joint target (deg)
        public float gripper;    // 0 open .. 1 closed
        public float hold;       // seconds to reach/hold this key
    }

    [Serializable]
    public class MotionGenome
    {
        public MotionKey[] keys;
        public float fitness = float.NegativeInfinity;
        public int generation = 0;
        public bool succeeded = false;   // did this genome complete the task in its eval rollout?

        public static MotionGenome Random(int jointCount, int keyCount, JointSpec[] specs, System.Random rng)
        {
            var g = new MotionGenome { keys = new MotionKey[keyCount] };
            for (int k = 0; k < keyCount; k++)
            {
                var key = new MotionKey { angles = new float[jointCount], gripper = (float)rng.NextDouble(), hold = 0.5f + (float)rng.NextDouble() * 1.0f };
                for (int j = 0; j < jointCount; j++)
                {
                    float lo = specs[j].minAngle, hi = specs[j].maxAngle;
                    key.angles[j] = lo + (float)rng.NextDouble() * (hi - lo);
                }
                g.keys[k] = key;
            }
            return g;
        }

        public MotionGenome Clone()
        {
            var g = new MotionGenome { keys = new MotionKey[keys.Length], generation = generation, succeeded = succeeded };
            for (int k = 0; k < keys.Length; k++)
                g.keys[k] = new MotionKey { angles = (float[])keys[k].angles.Clone(), gripper = keys[k].gripper, hold = keys[k].hold };
            return g;
        }

        public void Mutate(float rate, float sigma, JointSpec[] specs, System.Random rng)
        {
            foreach (var key in keys)
            {
                for (int j = 0; j < key.angles.Length; j++)
                    if (rng.NextDouble() < rate)
                    {
                        key.angles[j] += (float)Gauss(rng) * sigma;
                        key.angles[j] = Mathf.Clamp(key.angles[j], specs[j].minAngle, specs[j].maxAngle);
                    }
                if (rng.NextDouble() < rate) key.gripper = Mathf.Clamp01(key.gripper + (float)Gauss(rng) * 0.3f);
                if (rng.NextDouble() < rate) key.hold = Mathf.Clamp(key.hold + (float)Gauss(rng) * 0.3f, 0.2f, 2.5f);
            }
        }

        public static MotionGenome Crossover(MotionGenome a, MotionGenome b, System.Random rng)
        {
            var c = a.Clone();
            for (int k = 0; k < c.keys.Length; k++)
                for (int j = 0; j < c.keys[k].angles.Length; j++)
                    if (rng.NextDouble() < 0.5) c.keys[k].angles[j] = b.keys[k].angles[j];
            return c;
        }

        static double Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = 1.0 - rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
