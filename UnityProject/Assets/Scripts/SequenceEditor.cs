using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArmSmith
{
    [Serializable]
    public class SeqKey
    {
        public float[] joints;   // joint target angles (deg)
        public float gripper;    // 0 open .. 1 closed
        public float moveTime;   // seconds to travel TO this keyframe from the previous
        public float holdTime;   // seconds to dwell at this keyframe
    }

    [Serializable]
    public class Sequence
    {
        public string name = "sequence";
        public string[] jointNames;
        public List<SeqKey> keys = new List<SeqKey>();
    }

    /// <summary>
    /// Keyframe sequence editor + player. You position the arm (fly the target / per-servo keys), then
    /// CAPTURE the current pose as a keyframe (K). Build an ordered list of keyframes, then PLAY (J): the
    /// arm moves SMOOTHLY between them (not live tracking) — pick, lift, traverse, place. Keys can be
    /// inserted/deleted/retimed and the whole sequence saved to JSON (drives the real arm + seeds training).
    /// This is the "save points in a sequence so I can adjust the recorded controls" feature.
    /// Keys: K capture, J play, Backspace(seq) clear last, F10 already exports raw waypoints.
    /// </summary>
    public class SequenceEditor : MonoBehaviour
    {
        public ProceduralArm arm;
        public ArmController controller;
        public Sequence seq = new Sequence();
        public float defaultMoveTime = 1.2f;
        public float defaultHold = 0.3f;

        public bool Playing { get; private set; }
        public int PlayIndex { get; private set; } = -1;
        public int Count => seq.keys.Count;

        public void Bind(ProceduralArm a, ArmController c)
        {
            arm = a; controller = c;
            var names = new List<string>(); foreach (var js in arm.jointSpecs) names.Add(js.name);
            seq.jointNames = names.ToArray();
        }

        void Update()
        {
            if (Playing) return;
            if (Input.GetKeyDown(KeyCode.K)) CaptureKey();
            if (Input.GetKeyDown(KeyCode.J)) Play();
            // Shift+Backspace removes the last keyframe (plain Backspace = demo recorder).
            if (Input.GetKeyDown(KeyCode.Backspace) && Input.GetKey(KeyCode.LeftShift) && seq.keys.Count > 0)
                seq.keys.RemoveAt(seq.keys.Count - 1);
        }

        /// <summary>Capture the current arm pose as a new keyframe at the end of the sequence.</summary>
        public void CaptureKey()
        {
            var k = new SeqKey
            {
                joints = (float[])controller.TargetAngles.Clone(),
                gripper = arm.gripper != null ? arm.gripper.closeAmount : 0f,
                moveTime = defaultMoveTime,
                holdTime = defaultHold
            };
            seq.keys.Add(k);
            Debug.Log($"[Sequence] captured keyframe {seq.keys.Count}");
        }

        public void InsertKey(int index) { if (index >= 0 && index <= seq.keys.Count) { CaptureKey(); var last = seq.keys[seq.keys.Count - 1]; seq.keys.RemoveAt(seq.keys.Count - 1); seq.keys.Insert(index, last); } }
        public void DeleteKey(int index) { if (index >= 0 && index < seq.keys.Count) seq.keys.RemoveAt(index); }
        public void Clear() => seq.keys.Clear();

        public void Play()
        {
            if (seq.keys.Count < 1 || Playing) return;
            StartCoroutine(PlayRoutine());
        }

        IEnumerator PlayRoutine()
        {
            Playing = true;
            bool wasPaused = controller.paused;
            var prevMode = controller.mode;
            controller.mode = ArmController.Mode.Manual;   // we drive joint targets directly
            controller.paused = false;

            float[] start = (float[])controller.TargetAngles.Clone();
            for (int i = 0; i < seq.keys.Count; i++)
            {
                PlayIndex = i;
                var k = seq.keys[i];
                float[] from = start;
                float t = 0f, dur = Mathf.Max(0.05f, k.moveTime);
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float a = Mathf.SmoothStep(0f, 1f, t / dur);
                    var blend = new float[from.Length];
                    for (int j = 0; j < from.Length; j++)
                        blend[j] = Mathf.Lerp(from[j], j < k.joints.Length ? k.joints[j] : from[j], a);
                    controller.SetTargets(blend);
                    if (arm.gripper != null) arm.gripper.SetClose(k.gripper);
                    yield return null;
                }
                controller.SetTargets(k.joints);
                start = (float[])k.joints.Clone();
                // hold/dwell
                float h = 0f; while (h < k.holdTime) { h += Time.deltaTime; yield return null; }
            }
            PlayIndex = -1;
            Playing = false;
            controller.mode = prevMode;
            controller.paused = wasPaused;
        }

        /// <summary>Save the sequence as an armsmith.waypoints.v1 trajectory (real-robot + training seed).</summary>
        public string Export(float dt = 0.05f)
        {
            var traj = new WaypointTrajectory { dt_s = dt, joint_names = seq.jointNames, arm_type = "so101" };
            float t = 0f;
            float[] cur = seq.keys.Count > 0 ? (float[])seq.keys[0].joints.Clone() : new float[arm.jointSpecs.Count];
            foreach (var k in seq.keys)
            {
                int steps = Mathf.Max(1, Mathf.RoundToInt(k.moveTime / dt));
                float[] from = (float[])cur.Clone();
                for (int s = 1; s <= steps; s++)
                {
                    float a = s / (float)steps;
                    var wj = new WpJoint[cur.Length];
                    for (int j = 0; j < cur.Length; j++)
                    {
                        cur[j] = Mathf.Lerp(from[j], j < k.joints.Length ? k.joints[j] : from[j], a);
                        wj[j] = new WpJoint { name = seq.jointNames[j], deg = cur[j] };
                    }
                    traj.waypoints.Add(new Waypoint { t_s = t, joints = wj, gripper_deg = k.gripper * 90f });
                    t += dt;
                }
                int holdSteps = Mathf.RoundToInt(k.holdTime / dt);
                for (int s = 0; s < holdSteps; s++)
                {
                    var wj = new WpJoint[cur.Length];
                    for (int j = 0; j < cur.Length; j++) wj[j] = new WpJoint { name = seq.jointNames[j], deg = cur[j] };
                    traj.waypoints.Add(new Waypoint { t_s = t, joints = wj, gripper_deg = k.gripper * 90f });
                    t += dt;
                }
            }
            string dir = Path.Combine(Application.persistentDataPath, "Sequences");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{seq.name}_{DateTime.Now:yyyyMMdd_HHmmss}.waypoints.json");
            File.WriteAllText(path, JsonUtility.ToJson(traj, true));
            Debug.Log($"[Sequence] exported {traj.waypoints.Count} waypoints -> {path}");
            return path;
        }
    }
}
