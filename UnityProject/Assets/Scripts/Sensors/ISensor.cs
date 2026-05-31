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
    }
}
