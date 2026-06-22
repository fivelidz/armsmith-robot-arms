#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Pillar K gate — proves the multi-robot coordination layer:
    ///   (1) WorldBlackboard STATE: two robots publish; each can read the other; nearest/collide/yield work.
    ///   (2) EVENT BUS: a hand-off offer is delivered to a subscriber and an accept round-trips.
    ///   (3) CLAIMS: two arms can't both own the same object.
    ///   (4) SPAWN: MultiRobotManager builds 2 real SO-101 arms sharing the blackboard (physics-light).
    ///
    /// Parts 1-3 are pure C# (no scene). Part 4 builds arms; it's gated on a successful kinematics load.
    /// Run: -executeMethod ArmSmith.EditorTools.MultiRobotCheck.RunHeadless
    /// </summary>
    public static class MultiRobotCheck
    {
        [MenuItem("ARMSMITH/Run Multi-Robot Check")] public static void RunMenu() { Run(); }
        public static void RunHeadless() { if (!Run()) EditorApplication.Exit(13); }

        public static bool Run()
        {
            int pass = 0, fail = 0;
            void Check(string label, bool cond) { if (cond) pass++; else { fail++; Debug.LogError($"[MultiRobotCheck] FAIL: {label}"); } }

            var bb = WorldBlackboard.Instance;
            bb.Clear();

            // (1) STATE pub/sub
            bb.Publish(new RobotState { id = "arm1", tipPosition = new Vector3(0.2f, 0.1f, 0.3f), holding = true, intent = "carrying" });
            bb.Publish(new RobotState { id = "arm2", tipPosition = new Vector3(-0.2f, 0.1f, 0.3f), holding = false, intent = "idle" });
            Check("arm1 readable", bb.TryGet("arm1", out var s1) && s1.holding);
            int others = 0; foreach (var _ in bb.Others("arm1")) others++;
            Check("arm1 sees exactly 1 other", others == 1);

            // nearest other to a rendezvous point near arm2
            Check("nearest other = arm2", bb.NearestOther("arm1", new Vector3(-0.18f, 0.1f, 0.3f), out var near, out _) && near.id == "arm2");

            // collision / yield: arm2 plans into arm1's tip zone; lower id ("arm1") has priority -> arm2 yields
            Vector3 contested = new Vector3(0.2f, 0.1f, 0.3f);
            Check("WouldCollide detects overlap", bb.WouldCollide("arm2", contested, 0.06f));
            Check("arm2 must yield to arm1", bb.MustYield("arm2", contested, 0.06f));
            Check("arm1 need NOT yield to arm2", !bb.MustYield("arm1", contested, 0.06f));

            // (2) EVENT BUS hand-off round-trip
            RobotEvent? received = null;
            System.Action<RobotEvent> handler = e => { if (e.kind == "handoff_offer") received = e; };
            bb.Subscribe(handler);
            bb.PublishEvent(new RobotEvent { fromId = "arm1", toId = "arm2", kind = "handoff_offer", resource = "S_Cube", point = Vector3.zero });
            Check("offer delivered to subscriber", received.HasValue && received.Value.resource == "S_Cube");
            Check("event log records offer", bb.Events.Count == 1);
            bb.Unsubscribe(handler);

            // (3) CLAIMS exclusivity
            Check("arm1 claims cube", bb.Claim("S_Cube", "arm1"));
            Check("arm2 cannot steal claim", !bb.Claim("S_Cube", "arm2"));
            bb.Release("S_Cube", "arm1");
            Check("after release arm2 can claim", bb.Claim("S_Cube", "arm2"));

            bb.Clear();

            // (4) SPAWN two real arms sharing the blackboard
            GameObject mgrGo = null;
            var spawnedGos = new List<GameObject>();
            try
            {
                string kinPath = System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                if (System.IO.File.Exists(kinPath))
                {
                    mgrGo = new GameObject("MultiRobotManager");
                    var mgr = mgrGo.AddComponent<MultiRobotManager>();
                    var spawned = mgr.Spawn(2, kinPath, new Vector3(0f, 0f, 0.3f), 0.45f);
                    foreach (var sp in spawned) { spawnedGos.Add(sp.arm.gameObject); spawnedGos.Add(sp.ikTarget.gameObject); }
                    Check("spawned 2 arms", spawned.Count == 2);
                    Check("arm1 built", spawned.Count > 0 && spawned[0].arm.baseBody != null);
                    Check("arm2 built", spawned.Count > 1 && spawned[1].arm.baseBody != null);
                    Check("distinct ids", spawned.Count == 2 && spawned[0].id != spawned[1].id);
                    // both publish to the shared blackboard on Update; force one manual publish each
                    foreach (var sp in spawned)
                        bb.Publish(new RobotState { id = sp.id, tipPosition = sp.arm.gripper != null ? sp.arm.gripper.TipPosition : Vector3.zero });
                    int n = 0; foreach (var _ in bb.AllRobots()) n++;
                    Check("both arms on shared bus", n == 2);
                }
                else Debug.LogWarning("[MultiRobotCheck] kinematics.json missing — skipping spawn part (bus logic still gated).");
            }
            finally
            {
                foreach (var go in spawnedGos) if (go != null) Object.DestroyImmediate(go);
                if (mgrGo != null) Object.DestroyImmediate(mgrGo);
                bb.Clear();
            }

            bool ok = fail == 0;
            Debug.Log(ok
                ? $"[MultiRobotCheck] PASSED — {pass} multi-robot assertions hold (bus + events + claims + spawn)."
                : $"[MultiRobotCheck] FAILED — {fail} of {pass + fail} assertions failed.");
            return ok;
        }
    }
}
#endif
