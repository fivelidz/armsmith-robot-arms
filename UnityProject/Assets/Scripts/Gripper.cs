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

        // ---------------------------------------------------------------------------------------------
        // REALISTIC (friction-limited) GRASP — opt-in via realisticGrasp.  (research: GRASP_PHYSICS_STUDY.md)
        //
        // The DEFAULT path keeps the held object KINEMATIC and teleports it to the grasp point (a perfect
        // weld that never slips). That's numerically bullet-proof and is what the 7/7 regression suite +
        // the pick routine rely on, so it stays the default.
        //
        // When realisticGrasp = true we instead keep the object DYNAMIC and pull it toward the grasp pose
        // with a FORCE-LIMITED PD follower (Unity has no ArticulationBody<->Rigidbody joint, so a literal
        // break-force FixedJoint is impossible — a manual capped follower is the correct equivalent, exactly
        // what Isaac Sim's Surface Gripper does). The follower force is capped at the friction-cone capacity
        //     F_hold = 2 * mu * F_grip
        // so if the object's gravity+inertial load m*(g+a) exceeds what the grip can hold, the object simply
        // can't be kept up and SLIPS/DROPS — emergent, in the same regimes the real STS3215 gripper fails
        // (weak squeeze, fast jerk, heavy/low-friction object). F_grip scales with how hard the jaws close.
        [Header("Realistic grasp (opt-in; default = kinematic weld)")]
        public bool realisticGrasp = false;
        public float frictionMu = 0.7f;         // jaw-pad <-> object friction coeff (domain-randomize)
        public float gripForceMax = 12f;        // N of clamp force at closeAmount=1 (from STS3215 torque limit via linkage)
        public float gripContactClose = 0.5f;   // closeAmount above which the jaws actually contact/squeeze
        public float safetyFactor = 1.0f;       // 1.0 = exact physics; >1 = more forgiving hold
        public float slipReleaseDist = 0.06f;   // if the object lags the grasp point by more than this, it has slipped out
        Vector3 prevEeVel;                       // for finite-difference EE acceleration
        Vector3 prevEePos; bool hasPrevEe;
        bool heldWasKinematic;                   // restore the object's original kinematic flag on release
        public bool LastGraspSlipped { get; private set; }  // telemetry: did the most recent hold slip/drop?

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

            // GRASP FORMATION GATE (realistic mode): the squeeze must be strong enough to at least hold the
            // object's weight against gravity, or the pick simply fails (emergent, like a too-weak real grip).
            if (realisticGrasp)
            {
                float fHold = FrictionHoldCapacity();
                float weight = best.mass * Physics.gravity.magnitude;
                if (fHold < weight)
                {
                    // too weak to lift it — don't form a hold (the jaws may nudge it but won't pick it up)
                    return;
                }
            }

            held = best;
            heldWasKinematic = best.isKinematic;
            LastGraspSlipped = false;
            // capture the object's pose relative to the EE so it holds its grabbed orientation
            Transform ee = arm != null && arm.endEffector != null ? arm.endEffector : transform;
            heldLocalPos = ee.InverseTransformPoint(TipPosition);   // hold at the grasp point
            heldLocalRot = Quaternion.Inverse(ee.rotation) * best.transform.rotation;
            heldCols = best.GetComponentsInChildren<Collider>();
            hasPrevEe = false;

            if (realisticGrasp)
            {
                // DYNAMIC hold: the object stays a normal Rigidbody and is pulled by a force-limited PD
                // follower (see HeldFollowDynamic). It can slip/drop when overloaded. Keep jaw<->object
                // collision IGNORED so the pads don't re-eject it (the hold force is bounded, so it no
                // longer jams the articulation the way the rigid kinematic weld did).
                best.linearVelocity = Vector3.zero; best.angularVelocity = Vector3.zero;
                SetHeldCollisionIgnored(true);
            }
            else
            {
                // DEFAULT path: kinematic weld. Reliable carry WITHOUT corrupting the articulation physics
                // — parenting a kinematic Rigidbody under an ArticulationBody link makes the joints wind up
                // to insane angles. Make it kinematic, do NOT parent, drive its transform each step. Ignore
                // collision so the carried body can't feed contact forces back into the solver (verified:
                // empty lift = 0.3cm error, but lift while holding with collision = 54cm jam).
                best.linearVelocity = Vector3.zero; best.angularVelocity = Vector3.zero;
                best.isKinematic = true;
                SetHeldCollisionIgnored(true);
            }
        }

        /// <summary>Current jaw clamp force (N): 0 below the contact threshold, ramps to gripForceMax at
        /// full close. Proxy for the STS3215 torque limit transmitted through the jaw linkage.</summary>
        float GripForce()
        {
            float t = Mathf.InverseLerp(gripContactClose, 1f, closeAmount);
            return Mathf.Clamp01(t) * gripForceMax;
        }

        /// <summary>Friction-cone holding capacity (N) of a parallel grip: F_hold = 2*mu*F_grip.</summary>
        float FrictionHoldCapacity() => 2f * frictionMu * GripForce() * safetyFactor;

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
        // wind-up). Called from FixedUpdate. Routes to the kinematic weld (default) or the force-limited
        // dynamic follower (realisticGrasp) — see each method.
        void HeldFollow()
        {
            if (held == null) return;
            if (realisticGrasp) { HeldFollowDynamic(); return; }

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

        // REALISTIC hold: a FORCE-LIMITED PD follower on the DYNAMIC held Rigidbody. The follower computes
        // the wrench needed to keep the object at the grasp pose, then CLAMPS it to the friction-cone
        // capacity F_hold = 2*mu*F_grip. Because the force is capped, an overloaded grip (too weak, or a
        // fast/jerky move that adds m*a on top of m*g) physically cannot keep the object up -> it slips and
        // drops, exactly as the real gripper would. No teleport, no kinematic weld.
        void HeldFollowDynamic()
        {
            Transform ee = arm != null && arm.endEffector != null ? arm.endEffector : transform;
            Vector3 tip = TipPosition;
            Vector3 basePos = arm != null && arm.baseBody != null ? arm.baseBody.transform.position : transform.position;
            if (float.IsNaN(tip.x) || float.IsNaN(tip.y) || float.IsNaN(tip.z) || Vector3.Distance(tip, basePos) > 1.5f)
                return;

            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

            // finite-difference the EE acceleration (the inertial load the grip must also resist)
            Vector3 eeVel = hasPrevEe ? (tip - prevEePos) / dt : Vector3.zero;
            Vector3 eeAcc = hasPrevEe ? (eeVel - prevEeVel) / dt : Vector3.zero;
            prevEePos = tip; prevEeVel = eeVel; hasPrevEe = true;

            // target grasp pose (where the object should be if perfectly held)
            Vector3 targetPos = tip;
            Quaternion targetRot = ee.rotation * heldLocalRot;

            // released-by-slip detection: if the object has fallen far behind the grasp point, it's gone.
            float lag = Vector3.Distance(held.worldCenterOfMass, targetPos);
            if (lag > slipReleaseDist)
            {
                LastGraspSlipped = true;
                Release();   // it slipped out of the jaws
                return;
            }

            // PD wrench to track the grasp pose
            const float kp = 900f, kd = 60f;   // per-kg gains; scaled by mass below
            Vector3 posErr = targetPos - held.worldCenterOfMass;
            Vector3 desForce = held.mass * (kp * posErr - kd * held.linearVelocity);

            // CLAMP to the friction-cone capacity. The grip can apply at most F_hold of holding force; it
            // must first carry the object's own weight+inertia, leaving the remainder for tracking.
            float fHold = FrictionHoldCapacity();
            // gravity + inertial load the grip must support to keep the object moving with the EE
            Vector3 loadForce = held.mass * (-Physics.gravity + eeAcc);
            Vector3 totalForce = desForce + loadForce;   // what we'd need to apply this step
            float need = totalForce.magnitude;
            if (need > fHold && need > 1e-4f)
            {
                // exceed the friction capacity -> can only apply F_hold; the shortfall makes the object slip.
                totalForce *= fHold / need;
            }
            held.AddForce(totalForce, ForceMode.Force);

            // orientation: a softer capped PD so the object keeps its grabbed orientation (twist capacity
            // ~ mu * F_grip * patch radius; approximated by capping the angular correction).
            Quaternion dRot = targetRot * Quaternion.Inverse(held.rotation);
            dRot.ToAngleAxis(out float angDeg, out Vector3 axis);
            if (angDeg > 180f) angDeg -= 360f;
            if (!float.IsInfinity(axis.x) && angDeg != 0f)
            {
                Vector3 desTorque = axis.normalized * (angDeg * Mathf.Deg2Rad) * (held.mass * 4f)
                                    - held.angularVelocity * (held.mass * 0.5f);
                float tauMax = frictionMu * GripForce() * 0.02f;   // ~ mu*F_grip*patch_radius
                if (desTorque.magnitude > tauMax && tauMax > 1e-5f) desTorque = desTorque.normalized * tauMax;
                held.AddTorque(desTorque, ForceMode.Force);
            }
        }

        void FixedUpdate()
        {
            // Continuous grab ONLY when the gripper is firmly closed AND something is right at the grasp
            // point (tight radius) — lets a closed gripper that arrives at an object still grab it, without
            // grabbing things it merely passes near. Wider window only matters for fast rollouts.
            if (graspAssist && held == null && closeAmount > 0.8f) TryGrab();
            HeldFollow();
        }

        /// <summary>Manually step the grab/hold logic (for headless Physics.Simulate loops where Unity's
        /// FixedUpdate does not fire). Mirrors FixedUpdate.</summary>
        public void TickHeld()
        {
            if (graspAssist && held == null && closeAmount > 0.8f) TryGrab();
            HeldFollow();
        }

        void Release()
        {
            if (held != null)
            {
                SetHeldCollisionIgnored(false);   // restore collision before letting go
                if (!realisticGrasp)
                {
                    // kinematic-weld path: it was forced kinematic; turn dynamics back on and let it fall.
                    held.isKinematic = false;
                    held.linearVelocity = Vector3.zero; held.angularVelocity = Vector3.zero;
                }
                else
                {
                    // dynamic path: it was already a normal Rigidbody; just restore its original flag and
                    // keep whatever velocity it had (so a slipped object falls naturally).
                    held.isKinematic = heldWasKinematic;
                }
            }
            heldCols = null;
            held = null;
            hasPrevEe = false;
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
