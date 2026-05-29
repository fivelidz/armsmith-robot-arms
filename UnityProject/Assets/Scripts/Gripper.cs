using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Two-finger parallel-jaw gripper driven by prismatic ArticulationBody jaws.
    /// open value 0 = fully open, 1 = fully closed. Grasping is emergent from friction +
    /// the jaw drive force squeezing the object (physically realistic, no "magnet" cheat).
    /// </summary>
    public class Gripper : MonoBehaviour
    {
        ProceduralArm arm;
        ArticulationBody left, right;
        float maxWidth;
        float halfOpen;
        [Range(0, 1)] public float closeAmount = 0f; // 0 open, 1 closed

        public void Init(ProceduralArm a, ArticulationBody l, ArticulationBody r, float width, float half)
        {
            arm = a; left = l; right = r; maxWidth = width; halfOpen = half;
            SetClose(0f);
        }

        /// <summary>0 = open, 1 = closed.</summary>
        public void SetClose(float t)
        {
            closeAmount = Mathf.Clamp01(t);
            // Drive target = DISPLACEMENT from each jaw's rest position (matchAnchors=true).
            // closeAmount 0 => 0 displacement (jaws rest at ±halfOpen, fully open).
            // closeAmount 1 => each jaw moves inward by (halfOpen - 1mm) so they nearly meet at centre.
            // Physics + high friction make them clamp on any object between them (they stop at its surface).
            float inward = Mathf.Lerp(0f, halfOpen - 0.001f, closeAmount);
            ApplyTarget(left,  +inward);   // left jaw (rest -halfOpen) moves +X toward centre
            ApplyTarget(right, -inward);   // right jaw (rest +halfOpen) moves -X toward centre
        }

        public void Toggle() => SetClose(closeAmount > 0.5f ? 0f : 1f);

        void ApplyTarget(ArticulationBody ab, float target)
        {
            if (ab == null) return;
            var d = ab.xDrive;
            d.target = target;
            ab.xDrive = d;
        }

        /// <summary>Gripper tip world position (for IK target & grasp checks).</summary>
        public Vector3 TipPosition => arm != null && arm.endEffector != null
            ? arm.endEffector.position : transform.position;

        /// <summary>Approximate current opening in metres (for telemetry/export).</summary>
        public float CurrentWidth => Mathf.Lerp(maxWidth, 0.008f, closeAmount);

        /// <summary>Gripper angle proxy in degrees for waypoint export (0 open .. 90 closed).</summary>
        public float GripperDegrees => closeAmount * 90f;
    }
}
