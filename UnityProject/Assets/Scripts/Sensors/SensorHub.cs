using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Registry + observation builder for pluggable sensor modules. Players attach/toggle modules
    /// (MotorEncoders, IMU, Lidar, RangeFinder, DepthCamera, eFlesh, ...). The hub concatenates the
    /// ENABLED modules' observations into one vector used by the training/evolution layer, so for now
    /// "train with all the information" = enable all modules. Later, toggling subsets enables ablation
    /// studies to discover which modules help which task. See ROADMAP "Sensors".
    /// </summary>
    public class SensorHub : MonoBehaviour
    {
        public ProceduralArm arm;
        public CameraRig rig;
        public readonly List<SensorBase> sensors = new List<SensorBase>();

        public void Init(ProceduralArm a, CameraRig camRig)
        {
            arm = a; rig = camRig;
            // Attach the default catalogue of modules (all enabled = train with all info).
            Add<MotorEncoderSensor>();
            Add<TaskStateSensor>();   // EE pose, gripper, joint velocities, vector-to-target (key for manip)
            Add<ImuSensor>();
            var rf = Add<RangeFinderSensor>();
            Add<Lidar2DSensor>();
            var depth = Add<DepthCameraSensor>();
            if (depth != null && rig != null) depth.cam = rig.wristCam;
            Add<EFleshTactileSensor>();

            foreach (var s in sensors) s.Bind(arm);
        }

        T Add<T>() where T : SensorBase
        {
            var s = gameObject.AddComponent<T>();
            sensors.Add(s);
            return s;
        }

        public SensorBase Get(string name) => sensors.Find(s => s.Name == name);

        /// <summary>Toggle a module by name (for ablation / player experimentation).</summary>
        public void SetEnabled(string name, bool on)
        {
            var s = Get(name);
            if (s != null) s.Enabled = on;
        }

        /// <summary>Concatenated observation from all ENABLED modules (the training input).</summary>
        public float[] BuildObservation()
        {
            var list = new List<float>(64);
            foreach (var s in sensors)
                if (s.Enabled) list.AddRange(s.Observe());
            return list.ToArray();
        }

        /// <summary>Channel names matching BuildObservation order (for logging / analytics).</summary>
        public string[] BuildChannelNames()
        {
            var list = new List<string>(64);
            foreach (var s in sensors)
                if (s.Enabled) foreach (var c in s.Channels) list.Add(s.Name + "." + c);
            return list.ToArray();
        }

        public int ObservationSize()
        {
            int n = 0;
            foreach (var s in sensors) if (s.Enabled) n += s.Channels.Length;
            return n;
        }

        /// <summary>Short HUD summary: which modules are on and the total obs size.</summary>
        public string Summary()
        {
            var sb = new StringBuilder();
            foreach (var s in sensors)
                sb.Append(s.Enabled ? "<color=#6f6>" : "<color=#955>").Append(s.Name).Append("</color> ");
            sb.Append($"| obs={ObservationSize()}");
            return sb.ToString();
        }
    }
}
