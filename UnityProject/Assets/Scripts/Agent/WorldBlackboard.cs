using System;
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

    /// <summary>A transient coordination MESSAGE on the bus (e.g. a hand-off offer/accept). Unlike
    /// RobotState (last-value-wins), events are delivered to subscribers in publish order — the K2
    /// request/response channel that hand-offs and turn-taking are built on.</summary>
    public struct RobotEvent
    {
        public string fromId;
        public string toId;            // null/"" = broadcast
        public string kind;            // "handoff_offer" | "handoff_accept" | custom
        public string resource;        // object the message concerns (optional)
        public Vector3 point;          // rendezvous / claim point
        public float stamp;
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

        // ── transient EVENT bus (K2 request/response: hand-off offers, accepts, custom messages) ──
        readonly List<RobotEvent> events = new List<RobotEvent>();
        readonly List<Action<RobotEvent>> subs = new List<Action<RobotEvent>>();

        public void Subscribe(Action<RobotEvent> h) { if (h != null && !subs.Contains(h)) subs.Add(h); }
        public void Unsubscribe(Action<RobotEvent> h) { subs.Remove(h); }

        public void PublishEvent(RobotEvent e)
        {
            e.stamp = Time.time; events.Add(e);
            for (int i = 0; i < subs.Count; i++) subs[i]?.Invoke(e);
        }
        public IReadOnlyList<RobotEvent> Events => events;

        // ── coordination helpers (built on the primitives above) ──

        /// <summary>Nearest OTHER robot's tip to a point — "who should receive this object?".</summary>
        public bool NearestOther(string selfId, Vector3 point, out RobotState nearest, out float dist)
        {
            nearest = default; dist = float.PositiveInfinity; bool found = false;
            foreach (var r in Others(selfId))
            {
                float d = Vector3.Distance(r.tipPosition, point);
                if (d < dist) { dist = d; nearest = r; found = true; }
            }
            return found;
        }

        /// <summary>Would a planned tip target come within `clearance` of any OTHER arm's current tip?</summary>
        public bool WouldCollide(string selfId, Vector3 plannedTip, float clearance)
        {
            foreach (var r in Others(selfId))
                if (Vector3.Distance(r.tipPosition, plannedTip) < clearance) return true;
            return false;
        }

        /// <summary>Deterministic right-of-way by id string compare: the "lower" id has priority, so the
        /// caller yields if a higher-priority arm contests the same space. Prevents two arms deadlocking.</summary>
        public bool MustYield(string selfId, Vector3 plannedTip, float clearance)
        {
            foreach (var r in Others(selfId))
                if (string.CompareOrdinal(r.id, selfId) < 0 && Vector3.Distance(r.tipPosition, plannedTip) < clearance)
                    return true;
            return false;
        }

        public void Clear() { robots.Clear(); claims.Clear(); kv.Clear(); events.Clear(); subs.Clear(); }
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

        // ── K3 hand-off protocol (built on the event bus + claims) ────────────────────────────────────
        // Giver: claims the object, broadcasts a "handoff_offer" at a rendezvous point. Receiver: listens,
        // replies "handoff_accept", moves to take it. Both use WorldBlackboard so no direct references.

        /// <summary>This arm offers the object it's holding to another arm at a rendezvous point.</summary>
        public void OfferHandoff(string resource, Vector3 rendezvous, string toId = null)
        {
            intent = "handoff_ready";
            WorldBlackboard.Instance.Claim(resource, robotId);
            WorldBlackboard.Instance.PublishEvent(new RobotEvent
            { fromId = robotId, toId = toId, kind = "handoff_offer", resource = resource, point = rendezvous });
        }

        /// <summary>This arm accepts a pending offer (call from an event handler).</summary>
        public void AcceptHandoff(RobotEvent offer)
        {
            intent = "receiving";
            WorldBlackboard.Instance.PublishEvent(new RobotEvent
            { fromId = robotId, toId = offer.fromId, kind = "handoff_accept", resource = offer.resource, point = offer.point });
        }

        /// <summary>True if the planned move should be deferred this frame to avoid another arm.</summary>
        public bool ShouldYield(Vector3 plannedTip, float clearance = 0.08f)
            => WorldBlackboard.Instance.MustYield(robotId, plannedTip, clearance);
    }
}
