using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    public enum ScenarioType
    {
        ReachTouch,      // move EE to a target point (no grasp)
        PushToZone,      // push cube into a zone
        PickPlaceCube,   // pick cube, place on pad
        TrayToTray,      // move cube from tray A to tray B
        StackTwo,        // stack cube on another cube
        DropInBin,       // drop cube into a bin
        SortIntoTray     // multiple scattered cubes -> all into one target tray
    }

    /// <summary>
    /// Defines and manages selectable manipulation scenarios (easy -> harder), built from primitives.
    /// Source: research/manipulation_repos/TEST_ENVIRONMENTS.md. Cycle with [ and ].
    /// Provides per-scenario reward + success so the same trainer/evolution works across tasks.
    /// </summary>
    public class ScenarioManager : MonoBehaviour
    {
        public ProceduralArm arm;
        public ArmController controller;
        public ScenarioType current = ScenarioType.TrayToTray;

        // common props (created/positioned per scenario)
        Transform trayA, trayB, pad, bin, cubeB;
        Rigidbody cubeRb;
        public Transform cube;
        Transform reachTarget;
        readonly List<Transform> sortCubes = new List<Transform>();   // multi-object "sort into tray"

        Func<Material> matFactory;
        float elapsed;
        public float timeLimit = 30f;
        public bool Succeeded { get; private set; }       // latched: true once achieved, until reset
        public bool SuccessNow { get; private set; }       // instantaneous this frame
        public float SuccessTime { get; private set; }     // when success was first reached
        public float LastReward { get; private set; }
        public float Elapsed => elapsed;

        /// <summary>Explicit, human-readable training objective for the current scenario (shown in UI,
        /// states the success condition the reward optimises).</summary>
        public string Objective()
        {
            switch (current)
            {
                case ScenarioType.ReachTouch:
                    return "REACH: move the gripper tip to the pink target (< 4 cm). No grasp needed.";
                case ScenarioType.PushToZone:
                    return "PUSH: push the cube onto the blue pad (< 6 cm, at rest).";
                case ScenarioType.PickPlaceCube:
                    return "PICK & PLACE: grasp the cube and set it on the blue pad (< 6 cm, at rest).";
                case ScenarioType.TrayToTray:
                    return "TRAY-TO-TRAY: lift the cube out of Tray A and deliver it into Tray B (< 6 cm, resting in tray).";
                case ScenarioType.StackTwo:
                    return "STACK: place the yellow cube on top of the purple cube (< 3 cm, at rest).";
                case ScenarioType.DropInBin:
                    return "DROP IN BIN: carry the cube and release it inside the blue bin.";
                case ScenarioType.SortIntoTray:
                    return "SORT INTO TRAY: place ALL the scattered cubes into the green tray.";
                default: return "";
            }
        }

        /// <summary>Reward-term breakdown string (so the player sees WHAT is being optimised).</summary>
        public string RewardSpec()
        {
            switch (current)
            {
                case ScenarioType.ReachTouch: return "reward = -dist(tip,target); +10 on success";
                case ScenarioType.StackTwo:   return "reward = -dist(cube, aboveCubeB); +10 on success";
                case ScenarioType.DropInBin:  return "reward = -dist(cube, bin); +10 in-bin & at rest";
                case ScenarioType.SortIntoTray: return "reward = -sum(dist each cube -> tray); +10 when all in";
                case ScenarioType.TrayToTray: return "reward = -0.5*grip_dist + (grasped? 0.5-trayB_dist) ; +10 success";
                default: return "reward = -0.4*grip_dist - 0.3*pad_dist; +10 success";
            }
        }

        public void Init(ProceduralArm a, ArmController c, Func<Material> mat)
        {
            arm = a; controller = c; matFactory = mat;
            BuildAll();
            LoadScenario(current);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.RightBracket)) Cycle(1);
            if (Input.GetKeyDown(KeyCode.LeftBracket)) Cycle(-1);
            if (Input.GetKeyDown(KeyCode.Escape)) LoadScenario(current);
            elapsed += Time.deltaTime;
            LastReward = ComputeReward(out bool s);
            SuccessNow = s;
            if (s && !Succeeded) { Succeeded = true; SuccessTime = elapsed; Debug.Log($"[Scenario] {current} SUCCESS at {elapsed:F1}s"); }
        }

        void Cycle(int dir)
        {
            int n = Enum.GetValues(typeof(ScenarioType)).Length;
            current = (ScenarioType)(((int)current + dir + n) % n);
            LoadScenario(current);
        }

        // ---- prop construction (build once, toggle per scenario) ----
        void BuildAll()
        {
            trayA = BuildTray("S_TrayA", new Color(0.75f, 0.25f, 0.22f));
            trayB = BuildTray("S_TrayB", new Color(0.25f, 0.7f, 0.35f));
            bin = BuildBin("S_Bin", new Color(0.3f, 0.4f, 0.8f));

            pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            pad.name = "S_Pad"; pad.localScale = new Vector3(0.1f, 0.002f, 0.1f);
            Destroy(pad.GetComponent<Collider>());
            pad.GetComponent<MeshRenderer>().sharedMaterial = matFactory();
            pad.GetComponent<MeshRenderer>().sharedMaterial.color = new Color(0.2f, 0.5f, 0.95f, 0.85f);

            cube = MakeCube("S_Cube", new Color(0.9f, 0.75f, 0.15f), 0.045f, 0.05f, true);
            cubeRb = cube.GetComponent<Rigidbody>();
            cubeB = MakeCube("S_CubeB", new Color(0.8f, 0.3f, 0.7f), 0.05f, 0.08f, true);

            // Multi-object set for SortIntoTray (3 scattered cubes of different colours).
            Color[] cols = { new Color(0.9f,0.3f,0.2f), new Color(0.2f,0.6f,0.95f), new Color(0.95f,0.8f,0.2f) };
            for (int i = 0; i < 3; i++)
                sortCubes.Add(MakeCube($"S_SortCube{i}", cols[i], 0.04f, 0.04f, true));

            reachTarget = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            reachTarget.name = "S_ReachTarget"; reachTarget.localScale = Vector3.one * 0.04f;
            Destroy(reachTarget.GetComponent<Collider>());
            reachTarget.GetComponent<MeshRenderer>().sharedMaterial = matFactory();
            reachTarget.GetComponent<MeshRenderer>().sharedMaterial.color = new Color(1f, 0.3f, 0.8f, 0.7f);
        }

        Transform MakeCube(string name, Color c, float size, float mass, bool phys)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.localScale = Vector3.one * size;
            go.GetComponent<MeshRenderer>().sharedMaterial = matFactory();
            go.GetComponent<MeshRenderer>().sharedMaterial.color = c;
            if (phys)
            {
                var rb = go.AddComponent<Rigidbody>(); rb.mass = mass;
                go.GetComponent<BoxCollider>().material =
                    new PhysicsMaterial("c") { dynamicFriction = 1.1f, staticFriction = 1.3f };
            }
            return go.transform;
        }

        Transform BuildTray(string name, Color c)
        {
            var root = new GameObject(name).transform;
            float w = 0.15f, d = 0.12f, wall = 0.015f, h = 0.035f;
            var m = matFactory(); m.color = c;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root, false); floor.transform.localPosition = new Vector3(0, 0.004f, 0);
            floor.transform.localScale = new Vector3(w, 0.008f, d);
            floor.GetComponent<MeshRenderer>().sharedMaterial = m;
            TrayWall(root, new Vector3(0, h*0.5f, d*0.5f), new Vector3(w, h, wall), m);
            TrayWall(root, new Vector3(0, h*0.5f, -d*0.5f), new Vector3(w, h, wall), m);
            TrayWall(root, new Vector3(w*0.5f, h*0.5f, 0), new Vector3(wall, h, d), m);
            TrayWall(root, new Vector3(-w*0.5f, h*0.5f, 0), new Vector3(wall, h, d), m);
            return root;
        }

        Transform BuildBin(string name, Color c)
        {
            var t = BuildTray(name, c);  // bin = deeper tray
            t.localScale = new Vector3(1f, 2.2f, 1f);
            return t;
        }

        void TrayWall(Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        // ---- scenario loading ----
        public void LoadScenario(ScenarioType type)
        {
            current = type; elapsed = 0f; Succeeded = false; SuccessNow = false; SuccessTime = 0f;
            SetActive(trayA, false); SetActive(trayB, false); SetActive(pad, false);
            SetActive(bin, false); SetActive(cube, false); SetActive(cubeB, false); SetActive(reachTarget, false);
            foreach (var sc in sortCubes) SetActive(sc, false);

            switch (type)
            {
                case ScenarioType.SortIntoTray:
                    SetActive(trayB, true);
                    trayB.position = new Vector3(-0.16f, 0f, 0.34f);   // the target tray
                    Vector3[] spots = { new Vector3(0.18f,0.03f,0.28f), new Vector3(0.12f,0.03f,0.38f), new Vector3(0.20f,0.03f,0.40f) };
                    for (int i = 0; i < sortCubes.Count; i++) { SetActive(sortCubes[i], true); Place(sortCubes[i], spots[i % spots.Length]); }
                    break;
                case ScenarioType.ReachTouch:
                    SetActive(reachTarget, true);
                    reachTarget.position = new Vector3(0.1f, 0.12f, 0.32f);
                    break;
                case ScenarioType.PushToZone:
                    SetActive(cube, true); SetActive(pad, true);
                    Place(cube, new Vector3(0.05f, 0.03f, 0.30f)); pad.position = new Vector3(-0.18f, 0.001f, 0.34f);
                    break;
                case ScenarioType.PickPlaceCube:
                    SetActive(cube, true); SetActive(pad, true);
                    Place(cube, new Vector3(0.15f, 0.03f, 0.30f)); pad.position = new Vector3(-0.15f, 0.001f, 0.32f);
                    break;
                case ScenarioType.TrayToTray:
                    SetActive(trayA, true); SetActive(trayB, true); SetActive(cube, true);
                    trayA.position = new Vector3(0.18f, 0f, 0.34f); trayB.position = new Vector3(-0.18f, 0f, 0.34f);
                    Place(cube, trayA.position + Vector3.up * 0.03f);
                    break;
                case ScenarioType.StackTwo:
                    SetActive(cube, true); SetActive(cubeB, true);
                    Place(cubeB, new Vector3(-0.12f, 0.025f, 0.32f)); Place(cube, new Vector3(0.14f, 0.03f, 0.32f));
                    break;
                case ScenarioType.DropInBin:
                    SetActive(cube, true); SetActive(bin, true);
                    bin.position = new Vector3(-0.16f, 0f, 0.34f); Place(cube, new Vector3(0.16f, 0.03f, 0.32f));
                    break;
            }
        }

        void Place(Transform t, Vector3 p)
        {
            t.position = p; t.rotation = Quaternion.identity;
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        void SetActive(Transform t, bool a) { if (t != null) t.gameObject.SetActive(a); }

        // ---- reward / success ----
        public float ComputeReward(out bool success)
        {
            success = false;
            if (arm == null) return 0f;
            Vector3 ee = arm.gripper != null ? arm.gripper.TipPosition : arm.endEffector.position;
            bool grasped = cube != null && Vector3.Distance(ee, cube.position) < 0.05f
                           && arm.gripper != null && arm.gripper.closeAmount > 0.5f;

            switch (current)
            {
                case ScenarioType.SortIntoTray:
                {
                    float total = 0f; int inTray = 0;
                    foreach (var sc in sortCubes)
                    {
                        Vector3 flat = sc.position; flat.y = trayB.position.y;
                        float d = Vector3.Distance(flat, trayB.position);
                        total += d;
                        if (d < 0.07f && sc.position.y < 0.07f) inTray++;
                    }
                    success = inTray >= sortCubes.Count && Rest();
                    return -total + inTray * 2f + (success ? 10f : 0f);
                }
                case ScenarioType.ReachTouch:
                {
                    float dist = Vector3.Distance(ee, reachTarget.position);
                    success = dist < 0.04f;
                    return -dist + (success ? 10f : 0f);
                }
                case ScenarioType.TrayToTray:
                {
                    float g = Vector3.Distance(ee, cube.position);
                    Vector3 flat = cube.position; flat.y = trayB.position.y;
                    float toB = Vector3.Distance(flat, trayB.position);
                    success = toB < 0.06f && cube.position.y < 0.07f && Rest();
                    float r = -g * 0.5f; if (grasped) r += 0.5f - toB;
                    return r + (success ? 10f : 0f);
                }
                case ScenarioType.DropInBin:
                {
                    float toBin = Vector3.Distance(new Vector3(cube.position.x,0,cube.position.z),
                                                   new Vector3(bin.position.x,0,bin.position.z));
                    success = toBin < 0.06f && cube.position.y < 0.05f && Rest();
                    return -toBin + (success ? 10f : 0f);
                }
                case ScenarioType.StackTwo:
                {
                    Vector3 above = cubeB.position + Vector3.up * 0.05f;
                    float d = Vector3.Distance(cube.position, above);
                    success = d < 0.03f && Rest();
                    return -d + (success ? 10f : 0f);
                }
                default: // PushToZone / PickPlaceCube
                {
                    float g = Vector3.Distance(ee, cube.position);
                    Vector3 flat = cube.position; flat.y = pad.position.y;
                    float toPad = Vector3.Distance(flat, pad.position);
                    success = toPad < 0.06f && Rest();
                    float r = -g * 0.4f - toPad * 0.3f;
                    return r + (success ? 10f : 0f);
                }
            }
        }

        bool Rest() => cubeRb == null || cubeRb.linearVelocity.magnitude < 0.04f;
    }
}
