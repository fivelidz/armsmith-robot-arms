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

        System.Random rng = new System.Random(12345);
        JointSpec[] specs;
        float[] homePose;

        public void Init(ProceduralArm a, ArmController c, ScenarioManager s)
        {
            arm = a; controller = c; scenarios = s;
            specs = arm.jointSpecs.ToArray();
            homePose = (float[])controller.TargetAngles.Clone();
            SeedPopulation();
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
                yield return RunGeneration();
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
            for (int i = 0; i < elite && i < population.Count; i++)
            {
                var e = population[i].Clone();
                e.fitness = population[i].fitness; e.generation = generation; // keep elites' scores
                next.Add(e);
            }
            while (next.Count < populationSize)
            {
                var pa = Tournament(); var pb = Tournament();
                var child = MotionGenome.Crossover(pa, pb, rng);
                child.Mutate(mutationRate, mutationSigma, specs, rng);
                child.fitness = float.NegativeInfinity;
                next.Add(child);
            }
            population = next;
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
