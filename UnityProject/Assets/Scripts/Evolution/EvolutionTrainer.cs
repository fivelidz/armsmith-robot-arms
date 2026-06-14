using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Evolves MotionGenomes to solve the active scenario. Each genome is rolled out on the real
    /// physics arm (ArticulationBody) by interpolating its keyframes; fitness = scenario reward at the
    /// end minus an energy penalty. A simple GA (elitism + tournament + crossover + Gaussian mutation)
    /// produces the next generation. The player can lock survivors (interactive evolution).
    ///
    /// Phase 2/3 of design/GAME_DESIGN.md. Press T to start/stop training, N for one generation.
    /// </summary>
    public class EvolutionTrainer : MonoBehaviour
    {
        public ProceduralArm arm;
        public ArmController controller;
        public ScenarioManager scenarios;

        [Header("GA params")]
        public int populationSize = 16;
        public int keysPerGenome = 4;
        public int elite = 3;
        public float mutationRate = 0.3f;
        public float mutationSigma = 25f;
        public float rolloutSpeedup = 4f;   // time scale during headless rollouts

        public List<MotionGenome> population = new List<MotionGenome>();
        public int generation = 0;
        public MotionGenome best;
        public bool Running { get; private set; }
        public string status = "idle";

        // Interactive evolution: indices the player has locked as survivors (always parents next gen).
        public readonly HashSet<int> selected = new HashSet<int>();
        public bool playerSelectionMode = false;  // if true, Breed uses only selected as parents

        System.Random rng = new System.Random(12345);
        JointSpec[] specs;
        float[] homePose;

        // Closed-loop (sensor-driven) policy training. When policyMode is on, genomes are small neural
        // nets that map the SensorHub observation -> joint deltas — i.e. training USES all sensor info.
        public SensorHub sensorHub;
        public SelfCollision selfCollision;   // for the self-collision fitness penalty
        public bool policyMode = false;
        public int policyHidden = 12;
        public List<PolicyGenome> policyPop = new List<PolicyGenome>();
        public PolicyGenome bestPolicy;
        public float policyControlHz = 20f;   // policy decision rate during rollout
        public int evalResets = 2;            // randomised resets per genome eval (generalisation)
        public float lastSuccessRate = 0f;    // success-rate over the last eval's resets (UI metric)

        // ---- Training regimen (design/specs/TRAINING_REGIMEN.md) ----
        public TrainingConfig config = new TrainingConfig();
        // per-generation history for the Training UI curves
        public readonly List<float> bestHistory = new List<float>();
        public readonly List<float> meanHistory = new List<float>();
        public readonly List<float> successHistory = new List<float>();
        public float lastBestFitness, lastMeanFitness;

        /// <summary>Shaped fitness for ONE settled rollout state, using the config reward weights + the
        /// curriculum. Pulls the scenario's task reward and adds dense shaping (reach/grasp/place/energy/
        /// self-penetration/out-of-bounds) so the policy gets a smooth gradient toward the goal.</summary>
        public float ShapedFitness(float taskReward, bool success, float energy, float selfPen)
        {
            var c = config;
            float f = taskReward * c.wReach            // scenario term already ~ -dist; scale by reach weight
                    - energy * c.wEnergy
                    - selfPen * c.wSelfPen * 50f
                    + (success ? c.wSuccess : 0f);
            // grasp + out-of-bounds shaping from live scene state
            var grip = arm != null ? arm.gripper : null;
            if (grip != null && grip.IsHolding) f += c.wGrasp;
            return f;
        }

        void RecordGeneration()
        {
            float bf = float.NegativeInfinity, sum = 0f; int cnt = 0;
            if (policyMode) { foreach (var g in policyPop) { bf = Mathf.Max(bf, g.fitness); sum += g.fitness; cnt++; } }
            else            { foreach (var g in population) { bf = Mathf.Max(bf, g.fitness); sum += g.fitness; cnt++; } }
            lastBestFitness = bf;
            lastMeanFitness = cnt > 0 ? sum / cnt : 0f;
            bestHistory.Add(lastBestFitness);
            meanHistory.Add(lastMeanFitness);
            successHistory.Add(lastSuccessRate);
            // auto-curriculum: bump difficulty when consistently succeeding
            if (config.autoCurriculum && lastSuccessRate >= config.advanceSuccessRate && config.difficulty < 1f)
                config.difficulty = Mathf.Min(1f, config.difficulty + 0.1f);
        }

        public void Init(ProceduralArm a, ArmController c, ScenarioManager s)
        {
            arm = a; controller = c; scenarios = s;
            specs = arm.jointSpecs.ToArray();
            homePose = (float[])controller.TargetAngles.Clone();
            selfCollision = arm.GetComponent<SelfCollision>();   // for the self-collision penalty
            ApplyConfig();
            SeedPopulation();
        }

        /// <summary>Push the shared TrainingConfig into the trainer's runtime params + backend + sensors.
        /// Call after the UI edits the config.</summary>
        public void ApplyConfig()
        {
            populationSize = Mathf.Max(2, config.populationSize);
            elite          = Mathf.Clamp(config.elite, 1, populationSize - 1);
            mutationRate   = config.mutationRate;
            mutationSigma  = config.mutationSigma;
            keysPerGenome  = Mathf.Max(2, config.keysPerGenome);
            policyHidden   = Mathf.Max(4, config.policyHidden);
            evalResets     = Mathf.Max(1, config.evalResets);
            rolloutSpeedup = Mathf.Max(1f, config.rolloutSpeedup);
            policyMode     = config.backend == TrainingConfig.Backend.SensorPolicy;
            if (sensorHub != null) config.ApplySensorMask(sensorHub);
        }

        public void SetSensorHub(SensorHub h) => sensorHub = h;

        public void SeedPolicyPopulation()
        {
            policyPop.Clear();
            int inSize = sensorHub != null ? Mathf.Max(1, sensorHub.ObservationSize()) : arm.jointBodies.Count;
            int outSize = arm.jointBodies.Count;
            for (int i = 0; i < populationSize; i++)
                policyPop.Add(PolicyGenome.Random(inSize, policyHidden, outSize, rng));
            generation = 0; bestPolicy = null;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.T)) { if (Running) StopTraining(); else StartTraining(); }
            if (Input.GetKeyDown(KeyCode.N) && !Running) StartCoroutine(RunGeneration());
        }

        public void SeedPopulation()
        {
            population.Clear();
            for (int i = 0; i < populationSize; i++)
                population.Add(MotionGenome.Random(arm.jointBodies.Count, keysPerGenome, specs, rng));
            generation = 0; best = null;
        }

        /// <summary>WARM-START: seed the population from a competent demonstration (a list of joint-angle
        /// keyframes from a scripted/hand-driven solve). The first genome IS the demo; the rest are
        /// mutated copies. This turns "evolve from random" (rarely cracks grasp) into "evolve from
        /// competent" (refines a working motion) — the recommended path for the hard realistic arm.</summary>
        public void SeedFromDemo(List<MotionKey> demoKeys)
        {
            if (demoKeys == null || demoKeys.Count == 0) { SeedPopulation(); return; }
            keysPerGenome = demoKeys.Count;
            population.Clear();
            // genome 0 = the demo verbatim
            var seed = new MotionGenome { keys = demoKeys.ToArray() };
            population.Add(seed);
            // rest = mutated copies (explore around the demo)
            for (int i = 1; i < populationSize; i++)
            {
                var g = seed.Clone();
                g.Mutate(0.4f, mutationSigma * 0.5f, specs, rng);
                g.fitness = float.NegativeInfinity;
                population.Add(g);
            }
            generation = 0; best = null;
            Debug.Log($"[Trainer] warm-started population from a {demoKeys.Count}-key demo");
        }

        /// <summary>Build a pick-and-place demo (joint keyframes) for the current scenario by IK-solving the
        /// key waypoints (above-object, grasp, lift, over-target, place, release). Uses the controller's
        /// TestReach-style IK to get joint angles for each waypoint. Returns null if no object/target.</summary>
        public List<MotionKey> BuildPickPlaceDemo()
        {
            Transform obj = FindByName("S_Cube");
            Transform tgt = FindByName("S_Pad") ?? FindByName("S_TrayB");
            if (obj == null || tgt == null || controller == null) return null;
            Vector3 o = obj.position, t = tgt.position;
            // Grasp height tuned to the verified-good value: the physical tip floors ~2-3cm above a
            // commanded low target (drive vs gravity at extension), so commanding y=0.05 lands the tip
            // right at the 4.5cm cube's top -> a solid ~4cm grasp gap (S7 measured). The grab waypoint
            // holds a touch longer so the proximity-gated latch fires before the lift starts.
            var wps = new (Vector3 pos, float grip, float hold)[] {
                (new Vector3(o.x, 0.14f, o.z), 0f, 0.7f),   // above object, open
                (new Vector3(o.x, 0.05f, o.z), 0f, 0.7f),   // descend to grasp height, open
                (new Vector3(o.x, 0.05f, o.z), 1f, 1.0f),   // close (grab) — hold for the latch
                (new Vector3(o.x, 0.16f, o.z), 1f, 0.8f),   // lift
                (new Vector3(0f,  0.20f, 0.28f), 1f, 0.7f), // via-point centre
                (new Vector3(t.x, 0.16f, t.z), 1f, 0.7f),   // over target
                (new Vector3(t.x, 0.07f, t.z), 1f, 0.7f),   // descend into target
                (new Vector3(t.x, 0.07f, t.z), 0f, 0.8f),   // release
                (new Vector3(t.x, 0.18f, t.z), 0f, 0.6f),   // retreat
            };
            var keys = new List<MotionKey>();
            foreach (var w in wps)
            {
                float[] angles = controller.IKAnglesFor(w.pos);   // solve joint angles for this waypoint
                keys.Add(new MotionKey { angles = angles, gripper = w.grip, hold = w.hold });
            }
            return keys;
        }

        Transform FindByName(string n)
        {
            foreach (var tr in GameObject.FindObjectsOfType<Transform>()) if (tr.name == n) return tr;
            return null;
        }

        public void StartTraining() { if (!Running) StartCoroutine(TrainLoop()); }
        public void StopTraining() { Running = false; }

        /// <summary>Run exactly ONE generation of the current backend (for the UI "+1 Gen" button).</summary>
        public void StepOneGeneration()
        {
            if (Running) return;
            StartCoroutine(policyMode ? RunPolicyGeneration() : RunGeneration());
        }

        /// <summary>Clear populations + history and reseed (UI "Reset" button).</summary>
        public void ResetTraining()
        {
            StopTraining();
            generation = 0;
            population.Clear(); policyPop.Clear();
            best = null; bestPolicy = null; selected.Clear();
            bestHistory.Clear(); meanHistory.Clear(); successHistory.Clear();
            lastBestFitness = lastMeanFitness = lastSuccessRate = 0f;
            ApplyConfig();
            if (policyMode) SeedPolicyPopulation(); else SeedPopulation();
            status = "reset";
        }

        IEnumerator TrainLoop()
        {
            Running = true;
            while (Running)
                yield return policyMode ? RunPolicyGeneration() : RunGeneration();
        }

        // ---- Closed-loop sensor-driven policy evolution ----
        public IEnumerator RunPolicyGeneration()
        {
            if (policyPop.Count == 0) SeedPolicyPopulation();
            float prevScale = Time.timeScale;
            Time.timeScale = rolloutSpeedup;

            for (int i = 0; i < policyPop.Count; i++)
            {
                if (policyPop[i].fitness > float.NegativeInfinity && policyPop[i].generation == generation) continue;
                status = $"[policy] gen {generation} eval {i + 1}/{policyPop.Count}";
                yield return RolloutPolicy(policyPop[i]);
            }
            policyPop.Sort((x, y) => y.fitness.CompareTo(x.fitness));
            bestPolicy = policyPop[0];
            status = $"[policy] gen {generation} done best={bestPolicy.fitness:F2} obs={(sensorHub != null ? sensorHub.ObservationSize() : 0)}";
            RecordGeneration();
            BreedPolicies();
            generation++;
            Time.timeScale = prevScale;
        }

        IEnumerator RolloutPolicy(PolicyGenome g)
        {
            // Evaluate across several RANDOMISED resets so the policy GENERALISES (not memorises a single
            // layout). Fitness = mean over resets; success-rate tracked for the metric in the UI.
            float dt = 1f / Mathf.Max(1f, policyControlHz);
            int steps = Mathf.RoundToInt(scenarios.timeLimit * policyControlHz);
            float fitnessSum = 0f; int successes = 0;
            int resets = Mathf.Max(1, evalResets);

            for (int r = 0; r < resets; r++)
            {
                scenarios.Reroll();   // re-randomise object positions
                controller.mode = ArmController.Mode.Manual;
                // HARD-home (teleport + zero velocities) instead of a soft drive-target set, so each reset
                // starts from a pristine articulation state. A soft set leaves accumulated joint/contact
                // state that can wedge the arm across rollouts (the "works once then jams" failure) and
                // poison the fitness signal with garbage rollouts. (S7)
                controller.HardHome(homePose);
                yield return new WaitForFixedUpdate();

                float[] cur = (float[])homePose.Clone();
                float energy = 0f;
                bool success = false;
                for (int s = 0; s < steps; s++)
                {
                    float[] obs = sensorHub != null ? sensorHub.BuildObservation() : arm.GetJointAngles();
                    float[] act = g.Forward(obs);
                    for (int j = 0; j < cur.Length && j < act.Length; j++)
                    {
                        float delta = act[j] * 4f;
                        float nv = Mathf.Clamp(cur[j] + delta, specs[j].minAngle, specs[j].maxAngle);
                        energy += Mathf.Abs(nv - cur[j]);
                        cur[j] = nv;
                    }
                    controller.SetTargets(cur);
                    arm.SetJointTargets(cur);
                    if (arm.gripper != null && act.Length > cur.Length)
                        arm.gripper.SetClose((act[cur.Length] + 1f) * 0.5f);

                    float tAccum = 0f;
                    while (tAccum < dt) { tAccum += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }

                    scenarios.ComputeReward(out bool succ);
                    if (succ) { success = true; break; }
                }
                float reward = scenarios.ComputeReward(out bool s2);
                if (s2) success = true;
                // Penalise SELF-COLLISION (folding through itself = could damage a real arm).
                float selfPen = selfCollision != null ? selfCollision.MaxSelfPenetration() : 0f;
                fitnessSum += ShapedFitness(reward, success, energy, selfPen);   // config-weighted reward shaping
                if (success) successes++;
            }
            g.fitness = fitnessSum / resets;
            g.generation = generation;
            lastSuccessRate = successes / (float)resets;
        }

        void BreedPolicies()
        {
            var next = new List<PolicyGenome>();
            for (int i = 0; i < elite && i < policyPop.Count; i++) { var e = policyPop[i].Clone(); e.fitness = policyPop[i].fitness; e.generation = generation; next.Add(e); }
            while (next.Count < populationSize)
            {
                var pa = TournamentP(); var pb = TournamentP();
                var child = PolicyGenome.Crossover(pa, pb, rng);
                child.Mutate(mutationRate, mutationSigma * 0.02f, rng);
                child.fitness = float.NegativeInfinity;
                next.Add(child);
            }
            policyPop = next;
        }

        PolicyGenome TournamentP(int k = 3)
        {
            PolicyGenome b = null;
            for (int i = 0; i < k; i++) { var c = policyPop[rng.Next(policyPop.Count)]; if (b == null || c.fitness > b.fitness) b = c; }
            return b;
        }

        public IEnumerator RunGeneration()
        {
            float prevScale = Time.timeScale;
            Time.timeScale = rolloutSpeedup;

            for (int i = 0; i < population.Count; i++)
            {
                if (population[i].fitness > float.NegativeInfinity && population[i].generation == generation)
                    continue; // already evaluated this gen
                status = $"gen {generation}  eval {i + 1}/{population.Count}";
                yield return Rollout(population[i]);
            }

            population.Sort((x, y) => y.fitness.CompareTo(x.fitness));
            best = population[0];
            status = $"gen {generation} done  best={best.fitness:F2}";

            RecordGeneration();
            Breed();
            generation++;
            Time.timeScale = prevScale;
        }

        IEnumerator Rollout(MotionGenome g)
        {
            // reset scenario + arm to home
            scenarios.LoadScenario(scenarios.current);
            controller.mode = ArmController.Mode.Manual; // we drive joint targets directly
            // HARD-home (teleport + zero velocities) so each genome is evaluated from a pristine
            // articulation state — no accumulated jam/contact carry-over across rollouts. (S7)
            controller.HardHome(homePose);
            yield return new WaitForFixedUpdate();

            float energy = 0f;
            float[] cur = (float[])homePose.Clone();

            foreach (var key in g.keys)
            {
                float t = 0f;
                float dur = Mathf.Max(0.1f, key.hold);
                float[] start = (float[])cur.Clone();
                while (t < dur)
                {
                    float a = Mathf.Clamp01(t / dur);
                    for (int j = 0; j < cur.Length; j++)
                    {
                        float target = j < key.angles.Length ? key.angles[j] : start[j];
                        float v = Mathf.Lerp(start[j], target, a);
                        energy += Mathf.Abs(v - cur[j]);
                        cur[j] = v;
                    }
                    controller.SetTargets(cur);
                    arm.SetJointTargets(cur);
                    if (arm.gripper != null) arm.gripper.SetClose(key.gripper);
                    t += Time.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                }
            }
            // settle
            for (int s = 0; s < 20; s++) yield return new WaitForFixedUpdate();

            float reward = scenarios.ComputeReward(out bool success);
            float selfPen = selfCollision != null ? selfCollision.MaxSelfPenetration() : 0f;
            g.fitness = ShapedFitness(reward, success, energy, selfPen);   // config-weighted reward shaping
            g.generation = generation;
            lastSuccessRate = success ? 1f : 0f;
        }

        // ── DF2: GA-as-demo-factory ────────────────────────────────────────────────────────────────
        // Save the best evolved genome as an armsmith.waypoints.v1 demonstration file in the Demos folder,
        // so scripts/realbot/waypoints_to_lerobot.py can turn accumulated evolved behaviours into a LeRobot
        // dataset for training a Diffusion Policy (REPORT.md §7: "repurpose the GA as a demonstration
        // factory"). Reuses BestToTrajectory() so the demo matches the exact rollout interpolation.
        // Returns the written path (null if no best yet).
        public string SaveBestAsDemo(string label = "ga_demo")
        {
            var traj = BestToTrajectory();
            if (traj == null) return null;
            string dir = System.IO.Path.Combine(Application.persistentDataPath, "Exports", "Demos");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir,
                $"{label}_gen{(best != null ? best.generation : 0)}_{System.DateTime.Now:yyyyMMdd_HHmmss}.waypoints.json");
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(traj, true));
            Debug.Log($"[EvolutionTrainer] saved GA demo -> {path} ({traj.waypoints.Count} waypoints)");
            return path;
        }

        void Breed()
        {
            var next = new List<MotionGenome>();

            // Interactive evolution: if the player locked survivors, they (and only they) seed the next gen.
            List<MotionGenome> parents = null;
            if (playerSelectionMode && selected.Count > 0)
            {
                parents = new List<MotionGenome>();
                foreach (int idx in selected)
                    if (idx >= 0 && idx < population.Count) parents.Add(population[idx]);
                // carry the selected survivors forward unchanged (elitism by choice)
                foreach (var p in parents) { var c = p.Clone(); c.fitness = p.fitness; c.generation = generation; next.Add(c); }
            }
            else
            {
                for (int i = 0; i < elite && i < population.Count; i++)
                {
                    var e = population[i].Clone();
                    e.fitness = population[i].fitness; e.generation = generation;
                    next.Add(e);
                }
            }

            while (next.Count < populationSize)
            {
                MotionGenome pa = parents != null ? parents[rng.Next(parents.Count)] : Tournament();
                MotionGenome pb = parents != null ? parents[rng.Next(parents.Count)] : Tournament();
                var child = MotionGenome.Crossover(pa, pb, rng);
                child.Mutate(mutationRate, mutationSigma, specs, rng);
                child.fitness = float.NegativeInfinity;
                next.Add(child);
            }
            population = next;
            selected.Clear();
        }

        /// <summary>Player locks/unlocks a genome index as a survivor for the next generation.</summary>
        public void ToggleSelect(int index)
        {
            if (selected.Contains(index)) selected.Remove(index);
            else selected.Add(index);
        }

        MotionGenome Tournament(int k = 3)
        {
            MotionGenome bestSel = null;
            for (int i = 0; i < k; i++)
            {
                var c = population[rng.Next(population.Count)];
                if (bestSel == null || c.fitness > bestSel.fitness) bestSel = c;
            }
            return bestSel;
        }

        /// <summary>Convert the best genome into an exportable waypoint trajectory (for the real arm).</summary>
        public WaypointTrajectory BestToTrajectory(float dt = 0.05f)
        {
            if (best == null) return null;
            var traj = new WaypointTrajectory { dt_s = dt, arm_type = "so101" };
            var names = new List<string>(); foreach (var js in arm.jointSpecs) names.Add(js.name);
            traj.joint_names = names.ToArray();
            float t = 0f;
            float[] cur = (float[])homePose.Clone();
            foreach (var key in best.keys)
            {
                int steps = Mathf.Max(1, Mathf.RoundToInt(key.hold / dt));
                float[] start = (float[])cur.Clone();
                for (int s = 1; s <= steps; s++)
                {
                    float a = s / (float)steps;
                    var wp = new Waypoint { t_s = t, gripper_deg = key.gripper * 90f };
                    var wj = new WpJoint[cur.Length];
                    for (int j = 0; j < cur.Length; j++)
                    {
                        cur[j] = Mathf.Lerp(start[j], j < key.angles.Length ? key.angles[j] : start[j], a);
                        wj[j] = new WpJoint { name = arm.jointSpecs[j].name, deg = cur[j] };
                    }
                    wp.joints = wj; traj.waypoints.Add(wp); t += dt;
                }
            }
            return traj;
        }
    }
}
