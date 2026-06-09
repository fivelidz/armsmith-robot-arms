using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>What one robot publishes about itself for others to read (multi-robot coordination).</summary>
    public struct RobotState
    {
        public string id;
        public Vector3 tipPosition;     // gripper/grasp point in world
        public bool holding;            // is it currently holding an object?
        public string intent;           // free-form: "idle" | "reaching" | "carrying" | "handoff_ready" | ...
        public float updated;           // Time.time of last publish
    }

    /// <summary>
    /// Shared world blackboard for MULTI-ROBOT coordination (Pillar K foundation). Each arm publishes its
    /// RobotState (tip pose, holding, intent); any arm can read all others. Also a tiny key/value store +
    /// a claim system so two arms negotiate who does what (e.g. who picks vs who receives in a hand-off).
    /// Single static instance so all arms in a scene share it without wiring references.
    /// </summary>
    public class WorldBlackboard
    {
        static WorldBlackboard _instance;
        public static WorldBlackboard Instance => _instance ??= new WorldBlackboard();

        readonly Dictionary<string, RobotState> robots = new Dictionary<string, RobotState>();
        readonly Dictionary<string, string> claims = new Dictionary<string, string>();   // resource -> ownerId
        readonly Dictionary<string, float> kv = new Dictionary<string, float>();

        public void Publish(RobotState s) { s.updated = Time.time; robots[s.id] = s; }

        public IEnumerable<RobotState> AllRobots() => robots.Values;
        public IEnumerable<RobotState> Others(string selfId)
        {
            foreach (var r in robots.Values) if (r.id != selfId) yield return r;
        }
        public bool TryGet(string id, out RobotState s) => robots.TryGetValue(id, out s);

        // ── claims (so two arms don't grab the same object / collide on a hand-off) ──
        public bool Claim(string resource, string ownerId)
        {
            if (claims.TryGetValue(resource, out var cur) && cur != ownerId) return false;
            claims[resource] = ownerId; return true;
        }
        public void Release(string resource, string ownerId)
        {
            if (claims.TryGetValue(resource, out var cur) && cur == ownerId) claims.Remove(resource);
        }
        public string Owner(string resource) => claims.TryGetValue(resource, out var o) ? o : null;

        // ── shared scalars (e.g. hand-off rendezvous height, sync flags) ──
        public void Set(string key, float v) => kv[key] = v;
        public float Get(string key, float def = 0f) => kv.TryGetValue(key, out var v) ? v : def;

        public void Clear() { robots.Clear(); claims.Clear(); kv.Clear(); }
    }

    /// <summary>Attach to an arm: publishes its state to the WorldBlackboard each frame so other arms can
    /// coordinate with it (hand-offs, do-not-collide, collaborative tasks).</summary>
    public class RobotAgent : MonoBehaviour
    {
        public string robotId = "arm1";
        public ProceduralArm arm;
        public string intent = "idle";

        public void Bind(string id, ProceduralArm a) { robotId = id; arm = a; }

        void Update()
        {
            if (arm == null || arm.gripper == null) return;
            WorldBlackboard.Instance.Publish(new RobotState
            {
                id = robotId,
                tipPosition = arm.gripper.TipPosition,
                holding = arm.gripper.IsHolding,
                intent = intent,
            });
        }

        /// <summary>Distance to the nearest OTHER robot's tip (for do-not-collide / hand-off rendezvous).</summary>
        public float NearestOtherTip(out RobotState other)
        {
            float best = float.MaxValue; other = default;
            Vector3 me = arm != null && arm.gripper != null ? arm.gripper.TipPosition : transform.position;
            foreach (var r in WorldBlackboard.Instance.Others(robotId))
            {
                float d = Vector3.Distance(me, r.tipPosition);
                if (d < best) { best = d; other = r; }
            }
            return best;
        }
    }
}
