using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArmSmith
{
    // One timestep of a demonstration: the sensor observation the policy would see + the action taken
    // (joint targets) + gripper. This is the (obs, action) pair used for imitation learning.
    [Serializable] public class DemoStep { public float t; public float[] obs; public float[] jointTargets; public float gripper; }

    [Serializable]
    public class Demonstration
    {
        public string schema = "armsmith.demo.v1";
        public string scenario;            // task label, e.g. "TrayToTray"
        public string[] obsChannels;       // names of the observation channels (which sensors were on)
        public string[] jointNames;
        public float dt;
        public bool success;
        public List<DemoStep> steps = new List<DemoStep>();
    }

    /// <summary>
    /// Records a hand-driven demonstration (e.g. pick an object and put it in a tray) as (observation,
    /// action) pairs + the task label + whether it succeeded. This is the "record initial training
    /// actions" feature: a demo can seed/bootstrap the policy population (warm-start training by imitation)
    /// and is saved to disk. Press <Hold R while in a recording session>... wired via key in GameBootstrap.
    /// </summary>
    public class DemoRecorder : MonoBehaviour
    {
        public ProceduralArm arm;
        public ArmController controller;
        public ScenarioManager scenarios;
        public SensorHub sensorHub;
        public float dt = 0.05f;

        Demonstration demo;
        bool recording;
        float accum;
        public bool IsRecording => recording;
        public Demonstration Last { get; private set; }

        public void Bind(ProceduralArm a, ArmController c, ScenarioManager s, SensorHub h)
        { arm = a; controller = c; scenarios = s; sensorHub = h; }

        public void StartRecording()
        {
            demo = new Demonstration
            {
                scenario = scenarios != null ? scenarios.current.ToString() : "unknown",
                dt = dt,
                obsChannels = sensorHub != null ? sensorHub.BuildChannelNames() : new string[0],
            };
            var jn = new List<string>(); foreach (var js in arm.jointSpecs) jn.Add(js.name);
            demo.jointNames = jn.ToArray();
            recording = true; accum = 0f;
            Debug.Log($"[DemoRecorder] recording demo for {demo.scenario} (obs {demo.obsChannels.Length}ch)");
        }

        void FixedUpdate()
        {
            if (!recording) return;
            accum += Time.fixedDeltaTime;
            if (accum < dt) return;
            accum -= dt;
            var step = new DemoStep
            {
                t = demo.steps.Count * dt,
                obs = sensorHub != null ? sensorHub.BuildObservation() : arm.GetJointAngles(),
                jointTargets = (float[])controller.TargetAngles.Clone(),
                gripper = arm.gripper != null ? arm.gripper.closeAmount : 0f
            };
            demo.steps.Add(step);
        }

        public string StopRecording()
        {
            recording = false;
            if (demo == null || demo.steps.Count == 0) return null;
            scenarios.ComputeReward(out bool succ); demo.success = scenarios.Succeeded || succ;
            Last = demo;
            string dir = Path.Combine(Application.persistentDataPath, "Demos");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{demo.scenario}_{DateTime.Now:yyyyMMdd_HHmmss}.demo.json");
            File.WriteAllText(path, JsonUtility.ToJson(demo, true));
            Debug.Log($"[DemoRecorder] saved {demo.steps.Count} steps -> {path} (success={demo.success})");
            return path;
        }

        public int StepCount => demo != null ? demo.steps.Count : (Last != null ? Last.steps.Count : 0);
    }
}
