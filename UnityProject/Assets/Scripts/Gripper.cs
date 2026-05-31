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

        [Header("Grasp assist")]
        public bool graspAssist = true;       // attach the held object so contact-rich grasps are reliable
        public float graspRadius = 0.06f;      // object must be within this of the grasp point to grab
        Rigidbody held;                        // currently held object
        FixedJoint heldJoint;

        /// <summary>0 = open, 1 = closed.</summary>
        public void SetClose(float t)
        {
            float prev = closeAmount;
            closeAmount = Mathf.Clamp01(t);
            float inward = Mathf.Lerp(0f, halfOpen - 0.001f, closeAmount);
            ApplyTarget(left,  +inward);   // left jaw moves +X toward centre
            ApplyTarget(right, -inward);   // right jaw moves -X toward centre

            if (!graspAssist) return;
            // Closing past half -> try to grab the nearest graspable object at the grasp point.
            if (closeAmount > 0.6f && held == null) TryGrab();
            // Opening -> release.
            if (closeAmount < 0.4f && held != null) Release();
        }

        void TryGrab()
        {
            Vector3 p = TipPosition;
            Rigidbody best = null; float bestD = graspRadius;
            foreach (var col in Physics.OverlapSphere(p, graspRadius))
            {
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                // skip the arm's own bodies
                if (col.GetComponentInParent<ProceduralArm>() != null) continue;
                float d = Vector3.Distance(p, rb.worldCenterOfMass);
                if (d < bestD) { bestD = d; best = rb; }
            }
            if (best == null) return;
            held = best;
            // Attach via a real physics FixedJoint to the gripper body (not a teleport).
            heldJoint = gameObject.AddComponent<FixedJoint>();
            heldJoint.connectedBody = best;
            heldJoint.breakForce = 200f; heldJoint.breakTorque = 200f;
        }

        void Release()
        {
            if (heldJoint != null) Destroy(heldJoint);
            heldJoint = null; held = null;
        }

        public bool IsHolding => held != null;

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
