using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Deploys a trained Diffusion Policy (served by scripts/diffusion/serve_diffusion_policy.py) to drive
    /// the arm live, in RECEDING-HORIZON style (the Diffusion Policy deployment recipe): each decision we
    /// send the current joint observation to the Python server, get back an ACTION CHUNK (H future
    /// joint+gripper-degree targets), execute the first few, then re-request. Networking runs on a
    /// background thread so the sim never stalls; the main thread only reads the latest chunk.
    ///
    /// This is the closed-loop counterpart to scripted IK and the in-sim diffusion MOTION PLANNER — here
    /// the *behavior* itself is a learned diffusion model. Toggle with a key wired in GameBootstrap. The
    /// policy's joint order is the LeRobot mapping (BaseYaw,Shoulder,Elbow,Wrist,Gripper) = the first 4 arm
    /// joints + gripper; remaining arm joints (wrist_roll) are held.
    /// </summary>
    public class DiffusionPolicyClient : MonoBehaviour
    {
        public ArmController controller;
        public ProceduralArm arm;
        public string host = "127.0.0.1";
        public int port = 6020;
        [Tooltip("Execute this many actions from each chunk before re-requesting (receding horizon).")]
        public int execPerChunk = 4;
        [Tooltip("Seconds between executed actions (matches the demo dt, 20 Hz = 0.05).")]
        public float actionDt = 0.05f;

        public bool Running { get; private set; }
        public string status = "idle";

        // --- threading: the worker requests chunks; the main thread executes them ---
        Thread worker;
        volatile bool stop;
        readonly object gate = new object();
        float[][] pendingChunk;     // latest action chunk from the server (set by worker, read by main)
        volatile bool needRequest;  // main asks worker to fetch a new chunk
        float[] latestObs;          // observation snapshot for the worker to send

        float[][] activeChunk;
        int chunkIdx;
        float actionTimer;
        ArmController.Mode prevMode;
        bool prevMouseFollow;

        public void Begin()
        {
            if (Running || controller == null || arm == null) return;
            prevMode = controller.mode;
            prevMouseFollow = controller.mouseFollow;
            controller.mouseFollow = false;
            controller.mode = ArmController.Mode.Manual;   // we drive joint targets directly from the policy
            stop = false;
            needRequest = true;
            activeChunk = null; chunkIdx = 0; actionTimer = 0f;
            worker = new Thread(WorkerLoop) { IsBackground = true };
            worker.Start();
            Running = true;
            status = "connecting";
        }

        public void Stop()
        {
            stop = true;
            Running = false;
            if (controller != null) { controller.mouseFollow = prevMouseFollow; controller.mode = prevMode; }
            status = "stopped";
        }

        void OnDisable() { Stop(); }

        void Update()
        {
            if (!Running || arm == null || controller == null) return;

            // publish a fresh observation for the worker (current joint+gripper degrees)
            latestObs = CurrentObs();

            // pull a new chunk if the worker delivered one
            lock (gate)
            {
                if (pendingChunk != null)
                {
                    activeChunk = pendingChunk; pendingChunk = null; chunkIdx = 0; actionTimer = 0f;
                    status = "executing";
                }
            }
            if (activeChunk == null) { status = "waiting for policy"; return; }

            // execute the chunk at actionDt, receding horizon
            actionTimer += Time.deltaTime;
            if (actionTimer >= actionDt)
            {
                actionTimer = 0f;
                if (chunkIdx < activeChunk.Length && chunkIdx < execPerChunk)
                {
                    ApplyAction(activeChunk[chunkIdx]);
                    chunkIdx++;
                }
                else
                {
                    activeChunk = null;
                    needRequest = true;   // ask worker for the next chunk
                }
            }
        }

        // current observation = first 4 arm joints + gripper degrees (the policy's feature order)
        float[] CurrentObs()
        {
            var a = arm.GetJointAngles();
            float grip = arm.gripper != null ? arm.gripper.GripperDegrees : 0f;
            return new float[] { Get(a, 0), Get(a, 1), Get(a, 2), Get(a, 3), grip };
        }
        static float Get(float[] a, int i) => (a != null && i < a.Length) ? a[i] : 0f;

        void ApplyAction(float[] act)
        {
            if (act == null || act.Length < 5) return;
            var targets = arm.GetJointAngles();            // keep current for joints the policy doesn't drive
            if (targets == null) return;
            if (targets.Length > 0) targets[0] = act[0];   // BaseYaw  -> shoulder_pan
            if (targets.Length > 1) targets[1] = act[1];   // Shoulder -> shoulder_lift
            if (targets.Length > 2) targets[2] = act[2];   // Elbow    -> elbow_flex
            if (targets.Length > 3) targets[3] = act[3];   // Wrist    -> wrist_flex
            controller.SetTargets(targets);
            arm.SetJointTargets(targets);
            if (arm.gripper != null) arm.gripper.SetClose(Mathf.Clamp01(act[4] / 90f));
        }

        // ---------------- worker thread: TCP request/response ----------------
        void WorkerLoop()
        {
            TcpClient cli = null;
            System.IO.StreamReader reader = null;
            System.IO.StreamWriter writer = null;
            try
            {
                cli = new TcpClient();
                cli.Connect(host, port);
                var ns = cli.GetStream();
                reader = new System.IO.StreamReader(ns, Encoding.ASCII);
                writer = new System.IO.StreamWriter(ns, Encoding.ASCII) { AutoFlush = true };
                status = "connected";

                while (!stop)
                {
                    if (!needRequest) { Thread.Sleep(5); continue; }
                    needRequest = false;
                    var obs = latestObs;
                    if (obs == null) { Thread.Sleep(5); needRequest = true; continue; }

                    // {"obs":[[...]]}  — send a single-frame obs (server pads to obs_steps)
                    var sb = new StringBuilder("{\"obs\":[[");
                    for (int i = 0; i < obs.Length; i++) { if (i > 0) sb.Append(','); sb.Append(obs[i].ToString("F3")); }
                    sb.Append("]]}");
                    writer.Write(sb.ToString()); writer.Write("\n");

                    string line = reader.ReadLine();
                    if (line == null) break;
                    var chunk = ParseAction(line);
                    if (chunk != null) lock (gate) { pendingChunk = chunk; }
                }
            }
            catch (Exception e) { status = "error: " + e.Message; }
            finally
            {
                try { reader?.Dispose(); } catch { }
                try { writer?.Dispose(); } catch { }
                try { cli?.Close(); } catch { }
            }
        }

        // minimal parser for {"action":[[..],[..],...],"horizon":H,"dim":D}
        static float[][] ParseAction(string json)
        {
            int ai = json.IndexOf("\"action\"");
            if (ai < 0) return null;
            int lb = json.IndexOf('[', ai);
            if (lb < 0) return null;
            var rows = new List<float[]>();
            int i = lb + 1;
            while (i < json.Length)
            {
                if (json[i] == ']') break;                 // end of outer array
                if (json[i] == '[')
                {
                    int end = json.IndexOf(']', i);
                    if (end < 0) break;
                    var parts = json.Substring(i + 1, end - i - 1).Split(',');
                    var row = new float[parts.Length];
                    for (int p = 0; p < parts.Length; p++) float.TryParse(parts[p], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out row[p]);
                    rows.Add(row);
                    i = end + 1;
                }
                else i++;
            }
            return rows.Count > 0 ? rows.ToArray() : null;
        }
    }
}
