using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArmSmith
{
    // ---- Serializable waypoint schema (armsmith.waypoints.v1) ----
    // Matches design/specs/REAL_ROBOT_PORT_SPEC.md so the Python sidecars consume it directly.
    [Serializable] public class WpJoint { public string name; public float deg; }
    [Serializable] public class Waypoint { public float t_s; public WpJoint[] joints; public float gripper_deg; }
    [Serializable]
    public class WaypointTrajectory
    {
        public string arm_type = "so101";
        public string schema = "armsmith.waypoints.v1";
        public string units = "degrees";
        public string[] joint_names;
        public string gripper_name = "Gripper";
        public float dt_s = 0.05f;
        public List<Waypoint> waypoints = new List<Waypoint>();
    }

    /// <summary>
    /// Records the arm's commanded joint targets + gripper at a fixed dt during play, then exports them
    /// as a waypoint JSON that can drive a real arm (LeRobot / Feetech). Press G to start/stop, P to play back.
    /// </summary>
    public class BehaviourRecorder : MonoBehaviour
    {
        public ArmController controller;
        public ProceduralArm arm;
        public float dt = 0.05f;
        public string armType = "so101";

        WaypointTrajectory traj;
        bool recording;
        float accum;

        // playback
        bool playing;
        int playIdx;
        float playClock;

        public bool IsRecording => recording;
        public bool IsPlaying => playing;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.G)) { if (recording) StopRecording(); else StartRecording(); }
            if (Input.GetKeyDown(KeyCode.P)) { if (playing) StopPlayback(); else StartPlayback(); }
        }

        void FixedUpdate()
        {
            if (recording) StepRecord();
            if (playing) StepPlayback();
        }

        public void StartRecording()
        {
            traj = new WaypointTrajectory { arm_type = armType, dt_s = dt };
            var names = new List<string>();
            foreach (var js in arm.jointSpecs) names.Add(js.name);
            traj.joint_names = names.ToArray();
            recording = true; accum = 0f;
            CaptureFrame(0f);
            Debug.Log("[Recorder] recording started");
        }

        void StepRecord()
        {
            accum += Time.fixedDeltaTime;
            if (accum >= dt) { accum -= dt; CaptureFrame(traj.waypoints.Count * dt); }
        }

        void CaptureFrame(float t)
        {
            var wp = new Waypoint { t_s = t, gripper_deg = arm.gripper != null ? arm.gripper.GripperDegrees : 0f };
            var angles = controller.TargetAngles;
            var js = new WpJoint[angles.Length];
            for (int i = 0; i < angles.Length; i++)
                js[i] = new WpJoint { name = arm.jointSpecs[i].name, deg = angles[i] };
            wp.joints = js;
            traj.waypoints.Add(wp);
        }

        public string StopRecording()
        {
            recording = false;
            if (traj == null || traj.waypoints.Count == 0) return null;
            string dir = Path.Combine(Application.persistentDataPath, "Exports");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{arm.config.armName}_{DateTime.Now:yyyyMMdd_HHmmss}.waypoints.json");
            File.WriteAllText(path, JsonUtility.ToJson(traj, true));
            Debug.Log($"[Recorder] wrote {traj.waypoints.Count} waypoints -> {path}");
            return path;
        }

        public void StartPlayback()
        {
            if (traj == null || traj.waypoints.Count == 0) { Debug.Log("[Recorder] nothing to play"); return; }
            playing = true; playIdx = 0; playClock = 0f;
        }

        void StepPlayback()
        {
            if (traj == null || playIdx >= traj.waypoints.Count) { StopPlayback(); return; }
            var wp = traj.waypoints[playIdx];
            var angles = new float[arm.jointSpecs.Count];
            foreach (var j in wp.joints)
            {
                int idx = arm.jointSpecs.FindIndex(s => s.name == j.name);
                if (idx >= 0) angles[idx] = j.deg;
            }
            controller.SetTargets(angles);
            if (arm.gripper != null) arm.gripper.SetClose(wp.gripper_deg / 90f);

            playClock += Time.fixedDeltaTime;
            if (playClock >= dt) { playClock -= dt; playIdx++; }
        }

        public void StopPlayback() { playing = false; }

        /// <summary>Used by the trainer/evolution to inject an optimised trajectory for export.</summary>
        public void SetTrajectory(WaypointTrajectory t) => traj = t;
    }
}
