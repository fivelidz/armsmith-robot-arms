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

        public void Init(ProceduralArm a, ArmController c, ScenarioManager s)
        {
            arm = a; controller = c; scenarios = s;
            specs = arm.jointSpecs.ToArray();
            homePose = (float[])controller.TargetAngles.Clone();
            selfCollision = arm.GetComponent<SelfCollision>();   // for the self-collision penalty
            SeedPopulation();
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

        public void StartTraining() { if (!Running) StartCoroutine(TrainLoop()); }
        public void StopTraining() { Running = false; }

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
                controller.SetTargets(homePose);
                arm.SetJointTargets(homePose);
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
                fitnessSum += reward - energy * 0.001f - selfPen * 50f + (success ? 5f : 0f);
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

            Breed();
            generation++;
            Time.timeScale = prevScale;
        }

        IEnumerator Rollout(MotionGenome g)
        {
            // reset scenario + arm to home
            scenarios.LoadScenario(scenarios.current);
            controller.mode = ArmController.Mode.Manual; // we drive joint targets directly
            controller.SetTargets(homePose);
            arm.SetJointTargets(homePose);
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
            g.fitness = reward - energy * 0.002f + (success ? 5f : 0f);
            g.generation = generation;
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
