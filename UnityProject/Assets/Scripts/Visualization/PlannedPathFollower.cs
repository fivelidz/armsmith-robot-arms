using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Visualization
{
    /// <summary>
    /// Closes the loop PLAN -> MOTION: drives the arm's IK target along the chosen path from a
    /// DiffusionMotionPlanner (or any TrajectorySet), so the collision-free trajectory the planner
    /// produced is actually FOLLOWED by the arm. The visualizer shows the planned path + the executed
    /// trail, so you see the plan and the real motion together.
    ///
    /// Usage: assign controller + planner, call Begin(); it advances the IK target waypoint-by-waypoint
    /// (advancing when the tip gets within reachThreshold), in IK mode with mouseFollow disabled. This is
    /// exactly how a learned diffusion policy's action chunk would be executed (receding horizon) — here
    /// the "chunk" is the planned path. Press the bound key (wired in GameBootstrap) to run it.
    /// </summary>
    public class PlannedPathFollower : MonoBehaviour
    {
        public ArmController controller;
        public ProceduralArm arm;
        public DiffusionMotionPlanner planner;   // optional; else feed a path via FollowPath()

        public float reachThreshold = 0.03f;     // advance to next waypoint when tip within this (m)
        public float timeoutPerPoint = 1.5f;     // give up on a waypoint after this long (s)

        readonly List<Vector3> path = new List<Vector3>();
        int idx = -1;
        float waitTimer;
        bool wasMouseFollow;
        ArmController.Mode prevMode;

        public bool Running => idx >= 0 && idx < path.Count;

        /// <summary>Plan (via the planner) and start following the chosen path.</summary>
        public void Begin()
        {
            if (planner == null) return;
            var set = planner.Plan();
            TrajectorySample chosen = null;
            foreach (var s in set.samples) if (s.chosen) chosen = s;
            if (chosen == null && set.Count > 0) chosen = set.samples[0];
            if (chosen != null) FollowPath(chosen.points);
        }

        /// <summary>Start following an explicit world-space path.</summary>
        public void FollowPath(IEnumerable<Vector3> pts)
        {
            path.Clear();
            if (pts != null) path.AddRange(pts);
            if (controller == null || controller.ikTarget == null || path.Count == 0) { idx = -1; return; }
            wasMouseFollow = controller.mouseFollow;
            prevMode = controller.mode;
            controller.mouseFollow = false;
            controller.mode = ArmController.Mode.IK;
            idx = 0;
            waitTimer = 0f;
            controller.ikTarget.position = path[0];
        }

        public void Stop()
        {
            if (idx >= 0 && controller != null)
            {
                controller.mouseFollow = wasMouseFollow;   // restore the player's control state
            }
            idx = -1;
        }

        void Update()
        {
            if (!Running || controller == null || controller.ikTarget == null || arm == null || arm.endEffector == null)
                return;

            controller.ikTarget.position = path[idx];
            float d = Vector3.Distance(arm.endEffector.position, path[idx]);
            waitTimer += Time.deltaTime;

            if (d <= reachThreshold || waitTimer >= timeoutPerPoint)
            {
                idx++;
                waitTimer = 0f;
                if (idx >= path.Count)
                {
                    // reached the goal — park the target at the final point and restore control state.
                    controller.ikTarget.position = path[path.Count - 1];
                    Stop();
                }
            }
        }
    }
}
