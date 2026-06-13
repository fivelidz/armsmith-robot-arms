using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Enables SELF-COLLISION on the arm so links physically cannot pass through each other (which
    /// produced non-physical poses + erratic IK). ArticulationBodies in one articulation don't self-
    /// collide by default; we force-enable collision between NON-ADJACENT link colliders and explicitly
    /// IGNORE adjacent pairs (i, i+1) + base (they share a joint and would jam). Also reports the closest
    /// approach so training can penalise near-self-collision.
    /// </summary>
    public class SelfCollision : MonoBehaviour
    {
        ProceduralArm arm;
        readonly List<Collider> linkCols = new List<Collider>();

        public void Setup(ProceduralArm a)
        {
            arm = a;
            GatherAndApply();
            // CRITICAL: Unity re-evaluates collision pairs after MeshColliders finish cooking and after
            // the first physics ticks, which silently DROPS the IgnoreCollision pairs we set here. If that
            // happens, the tightly-packed adjacent wrist links start generating contact forces that JAM the
            // arm whenever it folds down to reach low (the pick-and-place "can't reach the cube / floors 6cm
            // above target" bug). We re-assert the ignore pairs for the first second to make them stick.
            StartCoroutine(ReassertForAWhile());
        }

        void GatherAndApply()
        {
            linkCols.Clear();

            // gather ONE representative collider per rigid link (base + each joint body), excluding jaws.
            AddLinkCollider(arm.baseBody != null ? arm.baseBody.GetComponent<Collider>() : null);
            for (int i = 0; i < arm.jointBodies.Count; i++)
            {
                var c = arm.jointBodies[i].GetComponent<Collider>();
                if (c == null) c = arm.jointBodies[i].GetComponentInChildren<Collider>();
                AddLinkCollider(c);
            }
            // S7d: start with ALL self-collision IGNORED. The SO-101 STL links overlap by design at build;
            // if any pair collides on the first physics step, PhysX tries to depenetrate them violently and
            // the articulation state spikes to NaN -> setupDescTask segfault. Settle first, THEN enable the
            // gap>=3 collisions (see ReassertForAWhile).
            ApplyIgnores(ignoreAll: true);
        }

        void ApplyIgnores(bool ignoreAll = false)
        {
            // Force-ENABLE collision only between FAR-APART link pairs (gap >= 3 in the chain) — e.g.
            // base vs forearm/wrist. NEAR pairs (gap 1 or 2) are the tightly-packed wrist/gripper cluster
            // whose meshes overlap by design; colliding them JAMS the joints at their limits, so we IGNORE
            // those. This keeps "can't fold back through itself" without wedging the wrist.
            // When ignoreAll is true, EVERY pair is ignored (used during the initial settle).
            for (int i = 0; i < linkCols.Count; i++)
                for (int j = i + 1; j < linkCols.Count; j++)
                {
                    if (linkCols[i] == null || linkCols[j] == null) continue;
                    bool near = ignoreAll || (j - i) <= 2; // settle: ignore all; else adjacent+once-removed
                    Physics.IgnoreCollision(linkCols[i], linkCols[j], near);
                }
        }

        System.Collections.IEnumerator ReassertForAWhile()
        {
            // PHASE 1 — SETTLE with ALL self-collision OFF (~40 frames) so the overlapping STL links never
            // depenetrate violently at build (the first-step PhysX NaN crash). Re-assert ignore-all each few
            // frames in case Unity rebuilds the pair table during cooking.
            for (int frame = 0; frame < 40; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (frame % 5 == 0) ApplyIgnores(ignoreAll: true);
            }
            // PHASE 2 — enable the gap>=3 self-collisions now the arm has settled into its rest pose.
            ApplyIgnores(ignoreAll: false);
            // PHASE 3 — steady-state low-rate re-assert (0.5 Hz) to survive broadphase rebuilds that would
            // re-drop our ignores and re-jam the wrist on the next pick. Coroutine = between steps, safe.
            var wait = new WaitForSeconds(2.0f);
            while (true)
            {
                ApplyIgnores(ignoreAll: false);
                yield return wait;
            }
        }

        void AddLinkCollider(Collider c) { linkCols.Add(c); } // keep index alignment even if null

        /// <summary>Smallest penetration depth between any non-adjacent link pair (0 = no self-collision).
        /// Useful as a training penalty signal.</summary>
        public float MaxSelfPenetration()
        {
            float worst = 0f;
            for (int i = 0; i < linkCols.Count; i++)
                for (int j = i + 2; j < linkCols.Count; j++)
                {
                    var a = linkCols[i]; var b = linkCols[j];
                    if (a == null || b == null) continue;
                    if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                                                   b, b.transform.position, b.transform.rotation,
                                                   out _, out float dist))
                        if (dist > worst) worst = dist;
                }
            return worst;
        }
    }
}
