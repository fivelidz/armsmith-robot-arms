using System;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// A small feed-forward neural-network policy: observation (from SensorHub — encoders, IMU, lidar,
    /// rangefinder, depth, eFlesh) -> joint target deltas. This is the CLOSED-LOOP genome that actually
    /// USES the sensor information during training (vs the open-loop MotionGenome). Evolving its weights
    /// (neuroevolution / ES) lets the population learn to react to sensors, and lets us compare which
    /// sensor modules help which task (enable/disable modules -> different obs size -> compare fitness).
    /// </summary>
    [Serializable]
    public class PolicyGenome
    {
        public int inputSize;
        public int hidden;
        public int outputSize;     // = joint count (+ gripper)
        public float[] w1;         // inputSize x hidden
        public float[] b1;         // hidden
        public float[] w2;         // hidden x outputSize
        public float[] b2;         // outputSize
        public float fitness = float.NegativeInfinity;
        public int generation = 0;

        public static PolicyGenome Random(int inSize, int hid, int outSize, System.Random rng)
        {
            var g = new PolicyGenome { inputSize = inSize, hidden = hid, outputSize = outSize };
            g.w1 = Rand(inSize * hid, rng); g.b1 = new float[hid];
            g.w2 = Rand(hid * outSize, rng); g.b2 = new float[outSize];
            return g;
        }

        static float[] Rand(int n, System.Random rng)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = (float)(rng.NextDouble() * 2 - 1) * 0.5f;
            return a;
        }

        /// <summary>Forward pass: obs -> output in [-1,1] (scaled to joint speed by caller).</summary>
        public float[] Forward(float[] obs)
        {
            var h = new float[hidden];
            for (int j = 0; j < hidden; j++)
            {
                float s = b1[j];
                int baseI = j; // w1 laid out [in*hidden]; index = i*hidden + j
                for (int i = 0; i < inputSize && i < obs.Length; i++) s += obs[i] * w1[i * hidden + j];
                h[j] = (float)Math.Tanh(s);
            }
            var o = new float[outputSize];
            for (int k = 0; k < outputSize; k++)
            {
                float s = b2[k];
                for (int j = 0; j < hidden; j++) s += h[j] * w2[j * outputSize + k];
                o[k] = (float)Math.Tanh(s);
            }
            return o;
        }

        public PolicyGenome Clone()
        {
            return new PolicyGenome
            {
                inputSize = inputSize, hidden = hidden, outputSize = outputSize, generation = generation,
                w1 = (float[])w1.Clone(), b1 = (float[])b1.Clone(),
                w2 = (float[])w2.Clone(), b2 = (float[])b2.Clone()
            };
        }

        public void Mutate(float rate, float sigma, System.Random rng)
        {
            Mut(w1, rate, sigma, rng); Mut(b1, rate, sigma, rng);
            Mut(w2, rate, sigma, rng); Mut(b2, rate, sigma, rng);
        }
        static void Mut(float[] a, float rate, float sigma, System.Random rng)
        {
            for (int i = 0; i < a.Length; i++)
                if (rng.NextDouble() < rate) a[i] += (float)Gauss(rng) * sigma;
        }

        public static PolicyGenome Crossover(PolicyGenome a, PolicyGenome b, System.Random rng)
        {
            var c = a.Clone();
            Cross(c.w1, b.w1, rng); Cross(c.b1, b.b1, rng); Cross(c.w2, b.w2, rng); Cross(c.b2, b.b2, rng);
            return c;
        }
        static void Cross(float[] dst, float[] other, System.Random rng)
        {
            for (int i = 0; i < dst.Length && i < other.Length; i++) if (rng.NextDouble() < 0.5) dst[i] = other[i];
        }

        static double Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = 1.0 - rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
