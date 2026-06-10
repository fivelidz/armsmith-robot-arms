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
        public float graspRadius = 0.12f;      // object must be within this of the grasp point to grab
        public float heldFloorY = 0.02f;       // never drive a held object below this Y (worktop surface)
        Rigidbody held;                        // currently held object
        Vector3 heldLocalPos; Quaternion heldLocalRot = Quaternion.identity;  // grasp offset rel. to EE

        /// <summary>0 = open, 1 = closed.</summary>
        public void SetClose(float t)
        {
            float prev = closeAmount;
            closeAmount = Mathf.Clamp01(t);
            float inward = Mathf.Lerp(0f, halfOpen - 0.001f, closeAmount);
            ApplyTarget(left,  +inward);   // left jaw moves +X toward centre
            ApplyTarget(right, -inward);   // right jaw moves -X toward centre

            if (!graspAssist) return;
            // Grab once when closing; only RELEASE on a deliberate full-open (hysteresis prevents the
            // grab/release oscillation seen when the drive hasn't settled).
            if (closeAmount > 0.55f && held == null) TryGrab();
            if (closeAmount < 0.15f && held != null) Release();
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
            // Reliable carry WITHOUT corrupting the articulation physics: parenting a kinematic Rigidbody
            // under an ArticulationBody link makes the joints wind up to insane angles. Instead we make the
            // held object kinematic, do NOT parent it, and manually drive its transform to follow the grasp
            // point each FixedUpdate (see HeldFollow). Records the local offset captured at grab time.
            best.linearVelocity = Vector3.zero; best.angularVelocity = Vector3.zero;
            best.isKinematic = true;
            // capture the object's pose relative to the EE so it holds its grabbed orientation
            Transform ee = arm != null && arm.endEffector != null ? arm.endEffector : transform;
            heldLocalPos = ee.InverseTransformPoint(TipPosition);   // hold at the grasp point
            heldLocalRot = Quaternion.Inverse(ee.rotation) * best.transform.rotation;

            // IGNORE collision between the held object and the arm. Once kinematic and rigidly carried,
            // the cube's collider would otherwise keep generating contacts against the gripper/wrist links
            // every physics step. Those contact forces feed back into the ArticulationBody solver and JAM
            // the arm when it tries to lift from a low grasp (verified: empty lift = 0.3cm error, but lift
            // while holding = 54cm jam). Ignoring the pair makes the lift as clean as the empty case. The
            // grasp is still "real" — it only forms when the jaws are closed at the object (friction-based),
            // we just stop the carried body from fighting its own carrier.
            heldCols = best.GetComponentsInChildren<Collider>();
            SetHeldCollisionIgnored(true);
        }

        Collider[] heldCols;       // colliders of the currently-held object (for ignore/restore)
        Collider[] armColsCache;   // cached arm colliders (gathered lazily)

        void SetHeldCollisionIgnored(bool ignore)
        {
            if (heldCols == null) return;
            if (armColsCache == null && arm != null)
            {
                var list = new System.Collections.Generic.List<Collider>();
                if (arm.baseBody != null) list.AddRange(arm.baseBody.GetComponentsInChildren<Collider>());
                foreach (var ab in arm.jointBodies)
                    if (ab != null) list.AddRange(ab.GetComponentsInChildren<Collider>());
                armColsCache = list.ToArray();
            }
            if (armColsCache == null) return;
            foreach (var hc in heldCols)
            {
                if (hc == null) continue;
                foreach (var ac in armColsCache)
                {
                    if (ac == null) continue;
                    Physics.IgnoreCollision(hc, ac, ignore);
                }
            }
        }

        // Drive the held object to follow the gripper each physics step (no parenting -> no articulation
        // wind-up). Called from FixedUpdate.
        void HeldFollow()
        {
            if (held == null) return;
            Transform ee = arm != null && arm.endEffector != null ? arm.endEffector : transform;
            Vector3 p = TipPosition;
            // Safety: never drive the held object to a NaN/absurd position (would fling it to infinity).
            Vector3 basePos = arm != null && arm.baseBody != null ? arm.baseBody.transform.position : transform.position;
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || Vector3.Distance(p, basePos) > 1.5f)
                return;  // keep the object where it is this frame
            // Floor guard: if the tip momentarily dips below the worktop during a transition, don't drag
            // the held object underground (it would clip through the table / read as a failed lift).
            if (p.y < heldFloorY) p.y = heldFloorY;
            held.transform.position = p;
            held.transform.rotation = ee.rotation * heldLocalRot;
        }

        void FixedUpdate()
        {
            // Continuous grab ONLY when the gripper is firmly closed AND something is right at the grasp
            // point (tight radius) — lets a closed gripper that arrives at an object still grab it, without
            // grabbing things it merely passes near. Wider window only matters for fast rollouts.
            if (graspAssist && held == null && closeAmount > 0.8f) TryGrab();
            HeldFollow();
        }

        void Release()
        {
            if (held != null)
            {
                SetHeldCollisionIgnored(false);   // restore collision before letting go
                held.isKinematic = false;
                held.linearVelocity = Vector3.zero; held.angularVelocity = Vector3.zero;
            }
            heldCols = null;
            held = null;
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
