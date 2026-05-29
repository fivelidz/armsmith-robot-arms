using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Serializable description of a robot arm's morphology.
    /// This is the "genome" the evolution layer mutates and the unit exported to JSON
    /// alongside the STL for sim-to-real. All lengths in METRES (1:1 with real hardware).
    /// Reference dimensions modelled on the Seeed reBot-DevArm / SO-ARM100 (see research/).
    /// </summary>
    [Serializable]
    public class JointSpec
    {
        public string name = "joint";
        public JointAxis axis = JointAxis.Pitch;   // rotation axis relative to parent
        public float linkLength = 0.20f;           // length of the link AFTER this joint (m)
        public float linkRadius = 0.025f;          // visual radius of the link (m)
        public float minAngle = -150f;             // joint limit (deg)
        public float maxAngle = 150f;
        public float maxTorque = 30f;              // N·m budget -> energy cost
        public float stiffness = 8000f;            // articulation drive stiffness
        public float damping = 400f;
    }

    public enum JointAxis { Yaw, Pitch, Roll }

    public enum GripperType { ParallelJaw, SoftFinger }

    [Serializable]
    public class ArmConfig
    {
        public string armName = "Starter-3DOF";
        public float baseHeight = 0.10f;           // pedestal height (m)
        public float baseRadius = 0.06f;
        public List<JointSpec> joints = new List<JointSpec>();
        public GripperType gripper = GripperType.ParallelJaw;
        public float gripperWidth = 0.08f;         // max jaw opening (m)
        public float gripperLength = 0.06f;        // finger length (m)

        /// <summary>Default simple starter arm: base-yaw + shoulder-pitch + elbow-pitch + gripper.</summary>
        public static ArmConfig CreateStarter()
        {
            var c = new ArmConfig { armName = "Starter-3DOF" };
            c.joints.Add(new JointSpec { name = "BaseYaw",   axis = JointAxis.Yaw,   linkLength = 0.02f, linkRadius = 0.04f, minAngle = -180, maxAngle = 180 });
            c.joints.Add(new JointSpec { name = "Shoulder",  axis = JointAxis.Pitch, linkLength = 0.25f, linkRadius = 0.03f, minAngle = -100, maxAngle = 100 });
            c.joints.Add(new JointSpec { name = "Elbow",     axis = JointAxis.Pitch, linkLength = 0.22f, linkRadius = 0.025f, minAngle = -135, maxAngle = 135 });
            c.joints.Add(new JointSpec { name = "Wrist",     axis = JointAxis.Pitch, linkLength = 0.08f, linkRadius = 0.022f, minAngle = -110, maxAngle = 110 });
            return c;
        }

        /// <summary>Full 6-DOF reBot-DevArm-style layout (later tier).</summary>
        public static ArmConfig CreateReBot6DOF()
        {
            var c = new ArmConfig { armName = "reBot-6DOF" };
            c.joints.Add(new JointSpec { name = "BaseYaw",      axis = JointAxis.Yaw,   linkLength = 0.05f, linkRadius = 0.045f, minAngle = -180, maxAngle = 180 });
            c.joints.Add(new JointSpec { name = "Shoulder",     axis = JointAxis.Pitch, linkLength = 0.28f, linkRadius = 0.035f, minAngle = -120, maxAngle = 120 });
            c.joints.Add(new JointSpec { name = "Elbow",        axis = JointAxis.Pitch, linkLength = 0.25f, linkRadius = 0.030f, minAngle = -140, maxAngle = 140 });
            c.joints.Add(new JointSpec { name = "ForearmRoll",  axis = JointAxis.Roll,  linkLength = 0.06f, linkRadius = 0.025f, minAngle = -180, maxAngle = 180 });
            c.joints.Add(new JointSpec { name = "WristPitch",   axis = JointAxis.Pitch, linkLength = 0.06f, linkRadius = 0.024f, minAngle = -110, maxAngle = 110 });
            c.joints.Add(new JointSpec { name = "WristRoll",    axis = JointAxis.Roll,  linkLength = 0.04f, linkRadius = 0.022f, minAngle = -180, maxAngle = 180 });
            return c;
        }

        public float TotalReach()
        {
            float r = baseHeight;
            foreach (var j in joints) r += j.linkLength;
            return r + gripperLength;
        }

        public Vector3 AxisVector(JointAxis a)
        {
            switch (a)
            {
                case JointAxis.Yaw:  return Vector3.up;      // rotate about local Y
                case JointAxis.Roll: return Vector3.forward; // rotate about local Z (link direction)
                default:             return Vector3.right;   // Pitch about local X
            }
        }

        public string ToJson() => JsonUtility.ToJson(this, true);
        public static ArmConfig FromJson(string s) => JsonUtility.FromJson<ArmConfig>(s);
    }
}
