using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// A REACTIVE scripted expert: given the CURRENT live positions of the scenario objects, it produces a
    /// competent pick-place / sort / reach waypoint plan (Cartesian poses + gripper). Because it reads the
    /// object transforms at CALL TIME, it generalises to ANY object scatter — the fix for the SortIntoTray
    /// "fixed cube positions" limitation: re-invoke per reset and the plan tracks wherever the cubes landed.
    ///
    /// This is the single source of truth for "how to solve scenario X" in Cartesian space. The trainer's
    /// warm-start (BuildPickPlaceDemo) and a runtime auto-solve both build on the same plan, so they can
    /// never drift apart. It deliberately holds NO IK — it emits world-space waypoints; the caller solves
    /// IK (controller.IKAnglesFor) so the same plan works headless (test) and live (trainer/agent).
    /// </summary>
    public struct ExpertWaypoint
    {
        public Vector3 pos;    // world target for the gripper tip
        public float grip;     // 0 open .. 1 closed
        public float hold;     // seconds to dwell / interpolate
        public ExpertWaypoint(Vector3 p, float g, float h) { pos = p; grip = g; hold = h; }
    }

    public static class ScriptedExpert
    {
        // grasp/lift/place heights tuned to the verified-good values used by the trainer demo (S7).
        const float kApproachY = 0.14f;
        const float kGraspY = 0.05f;
        const float kLiftY = 0.16f;
        const float kViaY = 0.20f;

        /// <summary>Build the reactive Cartesian plan for a scenario from a live object resolver. `resolve`
        /// maps a logical object name to its CURRENT world position (or null if absent). Returns null if the
        /// required objects aren't present.</summary>
        public static List<ExpertWaypoint> BuildPlan(ScenarioType sc, System.Func<string, Transform> resolve)
        {
            switch (sc)
            {
                case ScenarioType.ReachTouch:
                {
                    var rt = resolve("S_ReachTarget"); if (rt == null) return null;
                    Vector3 r = rt.position;
                    return new List<ExpertWaypoint> {
                        new ExpertWaypoint(new Vector3(r.x, r.y + 0.06f, r.z), 0f, 0.7f),
                        new ExpertWaypoint(r, 0f, 0.9f),
                        new ExpertWaypoint(r, 0f, 0.5f),
                    };
                }

                case ScenarioType.SortIntoTray:
                {
                    var tray = resolve("S_TrayB"); if (tray == null) return null;
                    Vector3 tp = tray.position;
                    var keys = new List<ExpertWaypoint>();
                    int slot = 0;
                    for (int ci = 0; ci < 3; ci++)
                    {
                        var cub = resolve($"S_SortCube{ci}"); if (cub == null) continue;
                        Vector3 c = cub.position;
                        float ox = (slot - 1) * 0.03f; slot++;   // spread drops inside the tray
                        keys.Add(new ExpertWaypoint(new Vector3(c.x, kApproachY, c.z), 0f, 0.5f));
                        keys.Add(new ExpertWaypoint(new Vector3(c.x, kGraspY, c.z), 0f, 0.5f));
                        keys.Add(new ExpertWaypoint(new Vector3(c.x, kGraspY, c.z), 1f, 0.8f));
                        keys.Add(new ExpertWaypoint(new Vector3(c.x, kLiftY, c.z), 1f, 0.6f));
                        keys.Add(new ExpertWaypoint(new Vector3(tp.x + ox, kApproachY, tp.z), 1f, 0.6f));
                        keys.Add(new ExpertWaypoint(new Vector3(tp.x + ox, 0.07f, tp.z), 0f, 0.6f));
                    }
                    return keys.Count > 0 ? keys : null;
                }

                default:   // PICK-and-PLACE family (PickPlace / Push / TrayToTray / DropInBin / StackTwo)
                {
                    var obj = resolve("S_Cube"); if (obj == null) return null;
                    Transform tgt; float placeY;
                    switch (sc)
                    {
                        case ScenarioType.DropInBin:    tgt = resolve("S_Bin");   placeY = 0.10f; break;
                        case ScenarioType.StackTwo:     tgt = resolve("S_CubeB"); placeY = 0.075f; break;
                        case ScenarioType.PushToZone:
                        case ScenarioType.PickPlaceCube: tgt = resolve("S_Pad") ?? resolve("S_TrayB"); placeY = 0.07f; break;
                        default:                        tgt = resolve("S_TrayB") ?? resolve("S_Pad"); placeY = 0.07f; break;
                    }
                    if (tgt == null) return null;
                    Vector3 o = obj.position, t = tgt.position;
                    return new List<ExpertWaypoint> {
                        new ExpertWaypoint(new Vector3(o.x, kApproachY, o.z), 0f, 0.7f),
                        new ExpertWaypoint(new Vector3(o.x, kGraspY, o.z), 0f, 0.7f),
                        new ExpertWaypoint(new Vector3(o.x, kGraspY, o.z), 1f, 1.0f),
                        new ExpertWaypoint(new Vector3(o.x, kLiftY, o.z), 1f, 0.8f),
                        new ExpertWaypoint(new Vector3(0f, kViaY, 0.28f), 1f, 0.7f),
                        new ExpertWaypoint(new Vector3(t.x, kLiftY, t.z), 1f, 0.7f),
                        new ExpertWaypoint(new Vector3(t.x, placeY, t.z), 1f, 0.7f),
                        new ExpertWaypoint(new Vector3(t.x, placeY, t.z), 0f, 0.8f),
                        new ExpertWaypoint(new Vector3(t.x, 0.18f, t.z), 0f, 0.6f),
                    };
                }
            }
        }
    }
}
