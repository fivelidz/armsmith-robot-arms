using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Text command interface — the bridge a text-based agent (or the player's console) uses to drive
    /// the arm and training. Parses simple instructions into actions. An external LLM agent (zero-auth,
    /// see design/specs/INGAME_AI_SPEC.md) generates these command strings; the game executes them.
    ///
    /// Supported commands (one per line; case-insensitive):
    ///   move x y z              - set the IK target to a world position (metres)
    ///   moveto trayA|trayB|cube|home   - move IK target to a named anchor
    ///   open / close            - gripper
    ///   grip 0.0..1.0           - set gripper close amount
    ///   wait <seconds>
    ///   joint <i> <deg>         - set a joint angle directly (manual)
    ///   scenario <name>         - load a scenario (e.g. TrayToTray)
    ///   train <N>               - run N evolution generations
    ///   seed                    - reseed the population
    ///   say <text>              - log a message (agent narration)
    /// Returns a coroutine that executes a script of commands sequentially.
    /// </summary>
    public class AgentCommands : MonoBehaviour
    {
        public ArmController controller;
        public ProceduralArm arm;
        public ScenarioManager scenarios;
        public EvolutionTrainer trainer;
        public Transform ikTarget;

        public readonly List<string> log = new List<string>();
        public string lastResult = "";

        public void Bind(ArmController c, ProceduralArm a, ScenarioManager s, EvolutionTrainer t, Transform target)
        {
            controller = c; arm = a; scenarios = s; trainer = t; ikTarget = target;
        }

        /// <summary>Run a multi-line script of commands.</summary>
        public Coroutine Run(string script) => StartCoroutine(Execute(script));

        public IEnumerator Execute(string script)
        {
            foreach (var raw in script.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                yield return ExecuteOne(line);
            }
        }

        public IEnumerator ExecuteOne(string line)
        {
            var tok = line.Split(' ');
            string cmd = tok[0].ToLowerInvariant();
            Log($"> {line}");
            switch (cmd)
            {
                case "move":
                    if (tok.Length >= 4 && ikTarget != null)
                    {
                        if (controller != null) controller.mouseFollow = false; // agent drives the target
                        ikTarget.position = ClampY(new Vector3(F(tok[1]), F(tok[2]), F(tok[3])));
                    }
                    break;
                case "moveto":
                    if (ikTarget != null)
                    {
                        if (controller != null) controller.mouseFollow = false;
                        ikTarget.position = ClampY(Anchor(tok.Length > 1 ? tok[1] : "home"));
                    }
                    break;
                case "open":  if (arm.gripper != null) arm.gripper.SetClose(0f); break;
                case "close": if (arm.gripper != null) arm.gripper.SetClose(1f); break;
                case "grip":  if (arm.gripper != null && tok.Length > 1) arm.gripper.SetClose(F(tok[1])); break;
                case "wait":
                    float w = tok.Length > 1 ? F(tok[1]) : 0.5f;
                    float t0 = Time.time; while (Time.time - t0 < w) yield return null;
                    break;
                case "joint":
                    if (tok.Length >= 3)
                    {
                        controller.mode = ArmController.Mode.Manual;
                        int ji = (int)F(tok[1]); float deg = F(tok[2]);
                        var arr = (float[])controller.TargetAngles.Clone();
                        if (ji >= 0 && ji < arr.Length) { arr[ji] = deg; controller.SetTargets(arr); }
                    }
                    break;
                case "scenario":
                    if (tok.Length > 1 && System.Enum.TryParse(tok[1], true, out ScenarioType st))
                        scenarios.LoadScenario(st);
                    break;
                case "train":
                    int n = tok.Length > 1 ? (int)F(tok[1]) : 1;
                    for (int g = 0; g < n; g++) yield return trainer.RunGeneration();
                    Log($"trained {n} gens, best={(trainer.best != null ? trainer.best.fitness.ToString("F2") : "-")}");
                    break;
                case "seed": trainer.SeedPopulation(); break;
                case "say": Log(line.Substring(3).Trim()); break;
                default: Log($"unknown command: {cmd}"); break;
            }
            lastResult = $"ok: {cmd}";
            yield return null;
        }

        Vector3 Anchor(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "traya": return new Vector3(0.18f, 0.08f, 0.34f);
                case "trayb": return new Vector3(-0.18f, 0.08f, 0.34f);
                case "cube":  return scenarios != null && scenarios.cube != null ? scenarios.cube.position + Vector3.up * 0.04f : Vector3.zero;
                default:      return new Vector3(0.0f, 0.20f, 0.30f); // home/ready
            }
        }

        static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

        Vector3 ClampY(Vector3 p)
        {
            float minY = controller != null ? controller.minTargetY : 0.02f;
            p.y = Mathf.Max(p.y, minY);
            return p;
        }

        void Log(string m)
        {
            log.Add(m);
            if (log.Count > 40) log.RemoveAt(0);
            Debug.Log("[Agent] " + m);
        }

        /// <summary>A built-in demo script for tray-to-tray — the "slow but correct" first solution
        /// (I29). Evolution can later speed this up by seeding from this trajectory.</summary>
        public static string DemoTrayToTray =>
            "say Pick from Tray A, place into Tray B (slow but correct)\n" +
            "scenario TrayToTray\n" +
            "open\n" +
            "moveto trayA\nwait 1.5\n" +
            "move 0.18 0.03 0.34\nwait 1.0\n" +   // descend onto cube
            "close\nwait 0.8\n" +
            "move 0.18 0.18 0.34\nwait 1.0\n" +   // lift
            "move -0.18 0.18 0.34\nwait 1.2\n" +  // traverse to Tray B
            "move -0.18 0.06 0.34\nwait 1.0\n" +  // descend
            "open\nwait 0.8\n" +
            "move -0.18 0.20 0.34\nwait 0.8\n" +  // retreat
            "say Done";
    }
}
