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

        // ===================== AUTONOMOUS SORT SOLVER =====================
        // Finds all "SortCube" objects and pick-and-places each into the green target tray (S_TrayB),
        // using IK + the gripper. This is the agent solving the SortIntoTray scenario by itself.
        public Coroutine AutoSort() => StartCoroutine(AutoSortRoutine());

        public IEnumerator AutoSortRoutine()
        {
            controller.mouseFollow = false;
            controller.mode = ArmController.Mode.IK;
            Time.timeScale = 1f;

            Transform tray = FindByName("S_TrayB");
            if (tray == null) { Log("no target tray"); yield break; }

            // gather cubes fresh each pass (they move as we place them)
            for (int pass = 0; pass < 6; pass++)
            {
                var cubes = FindAllContaining("SortCube");
                // pick the nearest cube still OUTSIDE the tray
                Transform target = null; float bestD = 999f;
                foreach (var c in cubes)
                {
                    Vector3 flat = c.position; flat.y = tray.position.y;
                    float dToTray = Vector3.Distance(flat, tray.position);
                    if (dToTray > 0.08f && dToTray < 1f)   // not already in tray
                    {
                        float dToBase = c.position.magnitude;
                        if (dToBase < bestD) { bestD = dToBase; target = c; }
                    }
                }
                if (target == null) { Log("all cubes sorted!"); break; }

                Log($"sorting {target.name} -> tray");
                yield return PickAndPlace(target.position, tray.position + Vector3.up * 0.06f);
            }
            // park
            yield return MoveTo(new Vector3(0f, 0.20f, 0.28f), 0.8f);
            Log("AutoSort done");
        }

        // Pick the object at `pick` and release it above `place`.
        IEnumerator PickAndPlace(Vector3 pick, Vector3 place)
        {
            float hover = 0.14f, grab = 0.045f;
            if (arm.gripper != null) arm.gripper.SetClose(0f);                 // open
            yield return MoveTo(new Vector3(pick.x, hover, pick.z), 1.8f);     // hover over cube
            yield return MoveTo(new Vector3(pick.x, grab, pick.z), 1.4f);      // descend
            if (arm.gripper != null) arm.gripper.SetClose(1f);                 // close
            yield return Wait(0.6f);
            yield return MoveTo(new Vector3(pick.x, hover, pick.z), 0.7f);     // lift
            yield return MoveTo(new Vector3(place.x, hover, place.z), 2.0f);   // traverse to tray
            yield return MoveTo(new Vector3(place.x, place.y, place.z), 1.4f); // descend into tray
            if (arm.gripper != null) arm.gripper.SetClose(0f);                 // release
            yield return Wait(0.5f);
            yield return MoveTo(new Vector3(place.x, hover, place.z), 0.6f);   // retreat
        }

        // pick (grasp) the object at a position; place (release) above a position. Reusable skills.
        IEnumerator PickAt(Vector3 pick)
        {
            // Gentle, slow moves so the offset-wrist URDF arm tracks smoothly (no flinging).
            float hover = 0.16f;
            if (arm.gripper != null) arm.gripper.SetClose(0f);
            yield return MoveTo(new Vector3(pick.x, hover, pick.z), 2.0f);   // hover over object

            // ROBUST GRAB: descend in steps, attempt the grab at each, retry until held (timing-proof).
            float[] grabHeights = { 0.06f, 0.045f, 0.03f, 0.06f };
            bool got = false;
            for (int attempt = 0; attempt < grabHeights.Length && !got; attempt++)
            {
                yield return MoveTo(new Vector3(pick.x, grabHeights[attempt], pick.z), 1.2f);
                yield return Wait(0.3f);
                if (arm.gripper != null) { arm.gripper.SetClose(1f); }
                yield return Wait(0.5f);
                got = arm.gripper != null && arm.gripper.IsHolding;
                if (!got && arm.gripper != null) arm.gripper.SetClose(0f);   // reopen + retry lower
            }
            yield return MoveTo(new Vector3(pick.x, hover, pick.z), 1.4f);   // lift
            Log(got ? "picked (held)" : "pick FAILED (no grab)");
        }
        IEnumerator PlaceAt(Vector3 place)
        {
            float hover = 0.16f;
            // Route UP then OVER then DOWN, passing through a well-conditioned via-point in front of the
            // base (z~0.28, y high) to avoid the offset-wrist singularity that flings loads during a
            // direct cross-workspace traverse.
            Vector3 cur = ikTarget.position;
            yield return MoveTo(new Vector3(cur.x, 0.22f, cur.z), 1.2f);              // lift high
            yield return MoveTo(new Vector3(0f, 0.22f, 0.28f), 1.6f);                 // via-point (centre, high)
            yield return MoveTo(new Vector3(place.x, 0.22f, place.z), 1.8f);          // over the tray, high
            yield return MoveTo(new Vector3(place.x, hover, place.z), 1.6f);          // descend
            yield return MoveTo(ClampY(place), 1.4f);                                 // into tray
            // Settle: wait until the tip is over the tray (within 8cm) OR 1s, whichever first (velocity
            // is capped so no explosion). Only THEN release, so the cube lands in the tray.
            yield return WaitForReach(new Vector3(place.x, place.y, place.z), 0.08f, 1.0f);
            if (arm.gripper != null) arm.gripper.SetClose(0f);                        // release
            yield return Wait(0.6f);
            yield return MoveTo(new Vector3(place.x, 0.22f, place.z), 1.2f);          // retreat up
            Log("placed");
        }

        // Resolve an object reference: "nearest" | a colour name | a name substring.
        Transform ResolveObject(string spec)
        {
            spec = spec.ToLowerInvariant();
            var movables = new List<Transform>();
            foreach (var t in GameObject.FindObjectsOfType<Transform>())
                if (t.GetComponent<Rigidbody>() != null && t.name.StartsWith("S_")) movables.Add(t);
            // colour match
            Color? want = ColorFromName(spec);
            Transform best = null; float bestScore = -1f;
            Vector3 ee = arm.endEffector != null ? arm.endEffector.position : Vector3.zero;
            foreach (var m in movables)
            {
                float score = 1f / (0.01f + Vector3.Distance(ee, m.position)); // nearest preferred
                if (want.HasValue)
                {
                    var mr = m.GetComponent<MeshRenderer>();
                    if (mr != null && mr.material != null && ColorClose(mr.material.color, want.Value)) score += 100f;
                    else continue;
                }
                else if (spec != "nearest" && spec.Length > 0 && !m.name.ToLowerInvariant().Contains(spec)) continue;
                if (score > bestScore) { bestScore = score; best = m; }
            }
            return best;
        }
        static Color? ColorFromName(string s)
        {
            switch (s) { case "red": return Color.red; case "blue": return Color.blue; case "green": return Color.green;
                case "yellow": return Color.yellow; case "orange": return new Color(1f,0.6f,0.1f); case "purple": return new Color(0.7f,0.4f,0.9f);
                default: return null; }
        }
        static bool ColorClose(Color a, Color b) => Vector3.Distance(new Vector3(a.r,a.g,a.b), new Vector3(b.r,b.g,b.b)) < 0.45f;

        IEnumerator MoveTo(Vector3 goal, float dur)
        {
            goal.y = Mathf.Max(goal.y, controller != null ? controller.minTargetY : 0.02f);
            Vector3 start = ikTarget.position; float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                ikTarget.position = Vector3.Lerp(start, goal, Mathf.SmoothStep(0, 1, t / dur));
                yield return null;
            }
            ikTarget.position = goal;
            yield return Wait(0.25f); // let IK + physics settle
        }

        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; yield return null; } }

        // Wait until the gripper tip is within `tol` of `worldGoal`, or `maxWait` seconds elapse.
        // Ensures the arm has actually CONVERGED on the target before we release the object.
        IEnumerator WaitForReach(Vector3 worldGoal, float tol, float maxWait)
        {
            float t = 0f;
            while (t < maxWait)
            {
                Vector3 tip = arm.gripper != null ? arm.gripper.TipPosition : arm.endEffector.position;
                if (Vector3.Distance(tip, worldGoal) < tol) break;
                t += Time.deltaTime;
                yield return null;
            }
        }

        Transform FindByName(string n)
        {
            foreach (var g in GameObject.FindObjectsOfType<Transform>()) if (g.name == n) return g;
            return null;
        }
        List<Transform> FindAllContaining(string sub)
        {
            var list = new List<Transform>();
            foreach (var g in GameObject.FindObjectsOfType<Transform>())
                if (g.name.Contains(sub) && g.GetComponent<Rigidbody>() != null) list.Add(g);
            return list;
        }

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
                case "sort": yield return AutoSortRoutine(); break;
                case "reach":
                    // reach <x y z> | reach <anchor>
                    if (tok.Length >= 4) { controller.mode = ArmController.Mode.IK; controller.mouseFollow = false; yield return MoveTo(ClampY(new Vector3(F(tok[1]), F(tok[2]), F(tok[3]))), 1.0f); }
                    else if (tok.Length == 2) { controller.mode = ArmController.Mode.IK; controller.mouseFollow = false; yield return MoveTo(ClampY(Anchor(tok[1])), 1.0f); }
                    break;
                case "pick":
                    // pick nearest | pick <color> | pick <objectNameSubstring>
                    {
                        Transform obj = ResolveObject(tok.Length > 1 ? tok[1] : "nearest");
                        if (obj != null) { controller.mode = ArmController.Mode.IK; controller.mouseFollow = false; yield return PickAt(obj.position); }
                        else Log("pick: no object found");
                    }
                    break;
                case "place":
                    // place <anchor> | place <x y z>
                    {
                        controller.mode = ArmController.Mode.IK; controller.mouseFollow = false;
                        Vector3 where = tok.Length >= 4 ? new Vector3(F(tok[1]), F(tok[2]), F(tok[3])) : Anchor(tok.Length > 1 ? tok[1] : "trayb");
                        yield return PlaceAt(where + Vector3.up * 0.05f);
                    }
                    break;
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
