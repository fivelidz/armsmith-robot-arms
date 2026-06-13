using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>Joint angles + servo ticks — the baseline proprioception (default module).</summary>
    public class MotorEncoderSensor : SensorBase
    {
        public override string Name => "MotorEncoders";
        string[] _ch;
        public override string[] Channels
        {
            get
            {
                if (_ch == null && arm != null)
                {
                    var l = new List<string>();
                    foreach (var js in arm.jointSpecs) l.Add(js.name + "_deg");
                    _ch = l.ToArray();
                }
                return _ch ?? new string[0];
            }
        }
        public override float[] Observe() => arm != null ? arm.GetJointAngles() : new float[0];
    }

    /// <summary>IMU at a link: orientation (euler), angular velocity, linear acceleration.
    /// Alternative to motor positioning — the arm "feels" its pose from inertial data.</summary>
    public class ImuSensor : SensorBase
    {
        public Transform mount;     // which link the IMU sits on (default: end-effector)
        Vector3 lastVel, lastPos;
        public override string Name => "IMU";
        public override string[] Channels => new[] {
            "imu_roll","imu_pitch","imu_yaw","gyro_x","gyro_y","gyro_z","acc_x","acc_y","acc_z" };

        public override void Bind(ProceduralArm a)
        {
            base.Bind(a);
            if (mount == null && a != null && a.endEffector != null) mount = a.endEffector;
            if (mount != null) lastPos = mount.position;
        }

        public override float[] Observe()
        {
            if (mount == null) return new float[9];
            Vector3 e = mount.rotation.eulerAngles;
            Vector3 vel = (mount.position - lastPos) / Mathf.Max(Time.fixedDeltaTime, 1e-4f);
            Vector3 acc = (vel - lastVel) / Mathf.Max(Time.fixedDeltaTime, 1e-4f);
            Vector3 gyro = mount.GetComponent<ArticulationBody>() != null
                ? mount.GetComponent<ArticulationBody>().angularVelocity : Vector3.zero;
            lastVel = vel; lastPos = mount.position;
            return new[] { Norm(e.x), Norm(e.y), Norm(e.z), gyro.x, gyro.y, gyro.z, acc.x, acc.y, acc.z + 9.81f };
        }
        static float Norm(float deg) => Mathf.DeltaAngle(0, deg) / 180f;
    }

    /// <summary>Single-point ToF rangefinder ("1-point lidar") from the gripper, pointing down/forward.</summary>
    public class RangeFinderSensor : SensorBase
    {
        public Transform origin;
        public Vector3 localDir = Vector3.up * -1f; // gripper -Y (downward toward objects)
        public float maxRange = 1.0f;
        public override string Name => "RangeFinder";
        public override string[] Channels => new[] { "range_m" };

        public override void Bind(ProceduralArm a)
        {
            base.Bind(a);
            if (origin == null && a != null && a.endEffector != null) origin = a.endEffector;
        }
        public override float[] Observe()
        {
            if (origin == null) return new[] { maxRange };
            Vector3 dir = origin.TransformDirection(localDir).normalized;
            if (Physics.Raycast(origin.position, dir, out RaycastHit hit, maxRange))
                return new[] { hit.distance };
            return new[] { maxRange };
        }

        void OnDrawGizmosSelected()
        {
            if (origin == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(origin.position, origin.TransformDirection(localDir).normalized * maxRange);
        }
    }

    /// <summary>Planar lidar: a fan of N raycasts in the horizontal plane around the gripper.</summary>
    public class Lidar2DSensor : SensorBase
    {
        public Transform origin;
        public int rays = 16;
        public float maxRange = 1.0f;
        public float fovDeg = 360f;
        string[] _ch;
        public override string Name => "Lidar2D";
        public override string[] Channels
        {
            get
            {
                if (_ch == null) { _ch = new string[Mathf.Max(1, rays)]; for (int i = 0; i < _ch.Length; i++) _ch[i] = $"lidar_{i}"; }
                return _ch;
            }
        }
        public override void Bind(ProceduralArm a)
        {
            base.Bind(a);
            if (origin == null && a != null && a.endEffector != null) origin = a.endEffector;
        }
        public override float[] Observe()
        {
            var r = new float[Mathf.Max(1, rays)];
            if (origin == null) { for (int i = 0; i < r.Length; i++) r[i] = maxRange; return r; }
            for (int i = 0; i < rays; i++)
            {
                float ang = -fovDeg * 0.5f + fovDeg * (i / (float)Mathf.Max(1, rays - 1));
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * origin.forward;
                r[i] = Physics.Raycast(origin.position, dir, out RaycastHit hit, maxRange) ? hit.distance : maxRange;
            }
            return r;
        }
    }

    /// <summary>Depth camera: downsampled NxN depth grid from the wrist camera view (RGB-D emulation).</summary>
    public class DepthCameraSensor : SensorBase
    {
        public Camera cam;          // wrist camera
        public int grid = 4;        // grid x grid depth samples
        public float maxRange = 1.2f;
        string[] _ch;
        public override string Name => "DepthCamera";
        public override string[] Channels
        {
            get
            {
                if (_ch == null) { _ch = new string[grid * grid]; for (int i = 0; i < _ch.Length; i++) _ch[i] = $"depth_{i}"; }
                return _ch;
            }
        }
        public override float[] Observe()
        {
            var d = new float[grid * grid];
            if (cam == null) { for (int i = 0; i < d.Length; i++) d[i] = maxRange; return d; }
            // Raycast through a grid of viewport points to emulate per-pixel depth.
            for (int y = 0; y < grid; y++)
                for (int x = 0; x < grid; x++)
                {
                    Vector3 vp = new Vector3((x + 0.5f) / grid, (y + 0.5f) / grid, 0f);
                    Ray ray = cam.ViewportPointToRay(vp);
                    d[y * grid + x] = Physics.Raycast(ray, out RaycastHit hit, maxRange) ? hit.distance : maxRange;
                }
            return d;
        }
    }

    /// <summary>eFlesh-style tactile: per-finger contact normal force + contact flag at the gripper jaws.
    /// Emulated from physics contacts on the jaw colliders (magnetic tactile gives shear+normal; here we
    /// expose normal force magnitude + contact presence per finger).</summary>
    public class EFleshTactileSensor : SensorBase
    {
        public override string Name => "EFleshTactile";
        public override string[] Channels => new[] { "left_contact", "left_force", "right_contact", "right_force" };

        readonly float[] vals = new float[4];
        readonly Dictionary<ArticulationBody, float> forces = new Dictionary<ArticulationBody, float>();

        public override float[] Observe()
        {
            if (arm == null) return vals;
            vals[0] = TouchForce(arm.leftJaw, out float lf) ? 1f : 0f; vals[1] = lf;
            vals[2] = TouchForce(arm.rightJaw, out float rf) ? 1f : 0f; vals[3] = rf;
            return vals;
        }

        bool TouchForce(ArticulationBody jaw, out float force)
        {
            force = 0f;
            if (jaw == null) return false;
            var jawCol = jaw.GetComponent<Collider>();
            if (jawCol == null) return false;
            // probe a small overlap box around the jaw for objects (the cube etc.)
            Collider[] hits = Physics.OverlapBox(jawCol.bounds.center, jawCol.bounds.extents * 1.2f);
            foreach (var h in hits)
            {
                if (h == jawCol) continue;
                var rb = h.attachedRigidbody;
                if (rb != null) { force = Mathf.Clamp01(arm.gripper != null ? arm.gripper.closeAmount : 0f) * 5f; return true; }
            }
            return false;
        }
    }

    /// <summary>
    /// TASK-STATE sensor — the high-value, manipulation-specific observations a policy needs to actually
    /// solve pick-and-place (and what a real teleop operator sees): end-effector world pose, gripper open
    /// amount + holding flag, joint velocities (rad/s), and — crucially — the VECTOR from the gripper tip
    /// to the active task object (the cube), in WORLD axes + its distance. With this, a diffusion/RL policy
    /// can learn "close the gap to the object, then grasp" directly. Target resolved by name (S_Cube) so it
    /// works across scenarios; if absent, those channels are zero.
    /// Channels (16): eeX,eeY,eeZ, gripperOpen, holding, jv0..jv5, toTargetX,toTargetY,toTargetZ,
    ///                targetDist, targetPresent.
    /// </summary>
    public class TaskStateSensor : SensorBase
    {
        public Transform targetOverride;   // optional explicit target; else find S_Cube
        Transform target;

        public override string Name => "TaskState";
        public override string[] Channels => new[]{
            "eeX","eeY","eeZ","gripperOpen","holding",
            "jv0","jv1","jv2","jv3","jv4","jv5",
            "toTargetX","toTargetY","toTargetZ","targetDist","targetPresent"
        };

        Transform ResolveTarget()
        {
            if (targetOverride != null) return targetOverride;
            if (target == null) { var g = GameObject.Find("S_Cube"); if (g != null) target = g.transform; }
            return target;
        }

        public override float[] Observe()
        {
            var v = new float[16];
            if (arm == null) return v;
            Vector3 ee = arm.gripper != null ? arm.gripper.TipPosition
                        : (arm.endEffector != null ? arm.endEffector.position : Vector3.zero);
            v[0] = ee.x; v[1] = ee.y; v[2] = ee.z;
            v[3] = arm.gripper != null ? 1f - Mathf.Clamp01(arm.gripper.closeAmount) : 1f;   // 1=open
            v[4] = arm.gripper != null && arm.gripper.IsHolding ? 1f : 0f;
            for (int i = 0; i < 6 && i < arm.jointBodies.Count; i++)
            {
                var ab = arm.jointBodies[i];
                v[5 + i] = (ab != null && ab.jointVelocity.dofCount > 0) ? ab.jointVelocity[0] : 0f;
            }
            var tgt = ResolveTarget();
            if (tgt != null)
            {
                Vector3 d = tgt.position - ee;
                v[11] = d.x; v[12] = d.y; v[13] = d.z; v[14] = d.magnitude; v[15] = 1f;
            }
            return v;
        }
    }
}
