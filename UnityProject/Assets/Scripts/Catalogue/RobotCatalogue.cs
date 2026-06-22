using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ArmSmith.Catalogue
{
    /// <summary>
    /// Pillar J2 — the OPEN-SOURCE ROBOT CATALOGUE. A registry of importable robots, each described by a
    /// kinematics JSON file in the SAME schema ProceduralArm.BuildFromKinematics already consumes. So adding
    /// a robot = registering a descriptor pointing at a kinematics JSON; loading it = the existing build path.
    ///
    /// Built-in entries:
    ///   - SO-101 (the real Assets/Meshes/SOARM100/kinematics.json — meshes + real joint frames).
    ///   - parametric generated arms (3-DOF starter, 5-DOF Koch-like, 6-DOF generic) written to
    ///     persistentDataPath/Catalogue/*.kin.json on demand — playable immediately, no external assets.
    ///
    /// This makes ARMSMITH multi-robot-capable (not just SO-101) and is the foundation J3 (URDF importer)
    /// drops into: a URDF→kinematics.json converter just adds a new catalogue entry.
    /// </summary>
    [Serializable]
    public class RobotDescriptor
    {
        public string id;             // stable key, e.g. "so101"
        public string displayName;    // "SO-101 (Seeed)"
        public int dof;
        public string source;         // "Assets meshes" | "generated" | "URDF import"
        public string kinematicsPath; // absolute path to a kinematics JSON (BuildFromKinematics input)
        public bool hasMeshes;        // true = real STL meshes; false = procedural primitives
        public string notes;
    }

    public static class RobotCatalogue
    {
        static readonly List<RobotDescriptor> _entries = new List<RobotDescriptor>();
        static bool _init;

        public static IReadOnlyList<RobotDescriptor> Entries { get { EnsureInit(); return _entries; } }

        public static RobotDescriptor Get(string id)
        {
            EnsureInit();
            return _entries.Find(e => e.id == id);
        }

        public static void EnsureInit()
        {
            if (_init) return;
            _init = true;

            // 1) the real SO-101 (uses the shipped kinematics.json + STL meshes)
            string so101 = Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
            _entries.Add(new RobotDescriptor
            {
                id = "so101", displayName = "SO-101 Follower (Seeed)", dof = 6, source = "Assets meshes",
                kinematicsPath = so101, hasMeshes = true,
                notes = "Real SO-ARM100/SO-101 — STS3215 servos, real URDF joint frames + STL meshes."
            });

            // 2) parametric generated arms (written lazily to persistentDataPath on first load)
            _entries.Add(new RobotDescriptor
            {
                id = "starter3", displayName = "Starter Arm (3-DOF)", dof = 3, source = "generated",
                kinematicsPath = null, hasMeshes = false,
                notes = "Simple 3-DOF teaching arm (base yaw + shoulder + elbow), on-axis tip."
            });
            _entries.Add(new RobotDescriptor
            {
                id = "koch5", displayName = "Koch-like Arm (5-DOF)", dof = 5, source = "generated",
                kinematicsPath = null, hasMeshes = false,
                notes = "5-DOF low-cost arm layout (base, shoulder, elbow, wrist-flex, wrist-roll)."
            });
            _entries.Add(new RobotDescriptor
            {
                id = "generic6", displayName = "Generic 6-DOF Arm", dof = 6, source = "generated",
                kinematicsPath = null, hasMeshes = false,
                notes = "Generic 6-DOF anthropomorphic layout (industrial-style)."
            });
        }

        /// <summary>Register a robot discovered/imported at runtime (e.g. by the URDF importer J3).</summary>
        public static void Register(RobotDescriptor d)
        {
            EnsureInit();
            if (d == null || string.IsNullOrEmpty(d.id)) return;
            int idx = _entries.FindIndex(e => e.id == d.id);
            if (idx >= 0) _entries[idx] = d; else _entries.Add(d);
        }

        /// <summary>Resolve the kinematics JSON path for a robot, GENERATING it on demand for parametric
        /// entries. Returns an absolute path BuildFromKinematics can consume, or null on failure.</summary>
        public static string ResolveKinematicsPath(string id)
        {
            EnsureInit();
            var d = Get(id);
            if (d == null) return null;
            if (!string.IsNullOrEmpty(d.kinematicsPath) && File.Exists(d.kinematicsPath)) return d.kinematicsPath;

            // generate a parametric arm JSON
            string dir = Path.Combine(Application.persistentDataPath, "Catalogue");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, id + ".kin.json");
            string json = GenerateParametricKinematics(id, d.dof);
            File.WriteAllText(path, json);
            d.kinematicsPath = path;
            return path;
        }

        // ── parametric kinematics generator (writes the same schema BuildFromKinematics parses) ──────────
        /// <summary>Build a valid kinematics JSON for an N-DOF serial arm with on-axis links. No meshes
        /// (procedural primitives are used by the builder when meshes are absent). Joint frames are simple
        /// alternating axes so the arm is reachable and IK-solvable.</summary>
        public static string GenerateParametricKinematics(string id, int dof)
        {
            dof = Mathf.Clamp(dof, 2, 6);
            // link lengths (m) for a tabletop-scale arm
            float[] linkLen = { 0.06f, 0.12f, 0.11f, 0.08f, 0.05f, 0.04f };

            var links = new List<string>();
            var joints = new List<string>();
            string prevLink = "base_link";
            links.Add(LinkJson("base_link", 0.20f));

            // joint axis pattern: J0 yaw (Z), then alternate pitch axes
            for (int i = 0; i < dof; i++)
            {
                string child = $"link{i + 1}";
                links.Add(LinkJson(child, Mathf.Max(0.04f, 0.12f - i * 0.012f)));
                // origin: first joint up off base; subsequent along previous link length
                float ox = i == 0 ? 0f : linkLen[Mathf.Clamp(i - 1, 0, linkLen.Length - 1)];
                float oz = i == 0 ? 0.05f : 0f;
                // axis: J0 = yaw (0,0,1); others = pitch (0,0,1) too but the build path twists frames; keep
                // simple on-axis revolutes that the IK handles well.
                float[] axis = { 0f, 0f, 1f };
                float lim = i == 0 ? 160f : (i % 2 == 1 ? 100f : 110f);
                joints.Add(JointJson(JointName(i, dof), prevLink, child,
                    new[] { ox, 0f, oz }, new[] { (i == 0 ? 0f : (i % 2 == 1 ? -90f : 90f)), 0f, 0f }, axis, -lim, lim));
                prevLink = child;
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"_comment\": \"Parametric {id} ({dof}-DOF) generated by RobotCatalogue\",\n");
            sb.Append("  \"units\": \"metres / degrees\",\n");
            sb.Append($"  \"dof_count\": {dof},\n");
            sb.Append("  \"servo_model\": \"Feetech STS3215\",\n");
            sb.Append("  \"links\": [\n    ").Append(string.Join(",\n    ", links)).Append("\n  ],\n");
            sb.Append("  \"joints\": [\n    ").Append(string.Join(",\n    ", joints)).Append("\n  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        static string JointName(int i, int dof)
        {
            string[] names = { "shoulder_pan", "shoulder_lift", "elbow_flex", "wrist_flex", "wrist_roll", "gripper" };
            return i < names.Length ? names[i] : $"joint{i}";
        }

        static string LinkJson(string name, float mass)
        {
            return $"{{ \"name\": \"{name}\", \"meshes\": [], \"inertial\": {{ \"mass_kg\": {F(mass)}, \"com_xyz_m\": [0,0,0] }} }}";
        }

        static string JointJson(string name, string parent, string child, float[] xyz, float[] rpyDeg, float[] axis, float lo, float hi)
        {
            return "{ " +
                $"\"name\": \"{name}\", \"type\": \"revolute\", \"parent\": \"{parent}\", \"child\": \"{child}\", " +
                $"\"origin_xyz_m\": [{F(xyz[0])},{F(xyz[1])},{F(xyz[2])}], " +
                $"\"origin_rpy_deg\": [{F(rpyDeg[0])},{F(rpyDeg[1])},{F(rpyDeg[2])}], " +
                $"\"axis_local\": [{F(axis[0])},{F(axis[1])},{F(axis[2])}], " +
                $"\"limit_deg\": [{F(lo)},{F(hi)}] }}";
        }

        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
    }
}
