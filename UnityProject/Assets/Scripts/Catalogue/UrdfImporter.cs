using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;

namespace ArmSmith.Catalogue
{
    /// <summary>
    /// Pillar J3 — a GENERIC URDF IMPORTER: parse a standard URDF file (the universal robot description
    /// format used by ROS / LeRobot / most open arms) into the kinematics JSON schema ProceduralArm already
    /// builds from, then register it in the RobotCatalogue. Drop a URDF + meshes -> a playable arm.
    ///
    /// Scope: serial chains (the common arm case). Reads &lt;link&gt; (mass), &lt;joint&gt; (type, parent,
    /// child, origin xyz/rpy, axis, limit lower/upper/effort/velocity). Converts radians→degrees and emits
    /// the same fields KinematicsJson expects. Continuous joints become wide-limit revolutes; fixed joints
    /// are preserved. Mesh hookup is best-effort (filenames recorded) — primitives are used if meshes absent.
    /// </summary>
    public static class UrdfImporter
    {
        /// <summary>Convert a URDF file to a kinematics JSON string. Throws on malformed XML.</summary>
        public static string ConvertToKinematics(string urdfPath, out int dof, out string robotName)
        {
            string xml = File.ReadAllText(urdfPath);
            return ConvertXmlToKinematics(xml, out dof, out robotName);
        }

        /// <summary>Convert URDF XML text → kinematics JSON. Exposed for headless testing (no file I/O).</summary>
        public static string ConvertXmlToKinematics(string xml, out int dof, out string robotName)
        {
            dof = 0; robotName = "imported";
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var robot = doc.DocumentElement;   // <robot name="...">
            if (robot == null || robot.Name != "robot") throw new Exception("URDF root <robot> not found");
            robotName = AttrOr(robot, "name", "imported");

            // links: name + mass
            var links = new List<string>();
            foreach (XmlNode n in robot.ChildNodes)
            {
                if (n.NodeType != XmlNodeType.Element || n.Name != "link") continue;
                var le = (XmlElement)n;
                string lname = AttrOr(le, "name", "link");
                float mass = 0.1f;
                var inertial = le["inertial"];
                if (inertial != null && inertial["mass"] != null)
                    mass = ParseFloat(AttrOr(inertial["mass"], "value", "0.1"), 0.1f);
                // record first mesh filename if present (best-effort)
                string meshFile = "";
                var vis = le["visual"];
                var geom = vis?["geometry"];
                var mesh = geom?["mesh"];
                if (mesh != null) meshFile = Path.GetFileName(AttrOr(mesh, "filename", ""));
                links.Add(LinkJson(lname, mass, meshFile));
            }

            // joints
            var joints = new List<string>();
            foreach (XmlNode n in robot.ChildNodes)
            {
                if (n.NodeType != XmlNodeType.Element || n.Name != "joint") continue;
                var je = (XmlElement)n;
                string jname = AttrOr(je, "name", "joint");
                string jtype = AttrOr(je, "type", "revolute");
                string parent = AttrOr(je["parent"], "link", "");
                string child = AttrOr(je["child"], "link", "");

                float[] xyz = ParseVec3(AttrOr(je["origin"], "xyz", "0 0 0"));
                float[] rpyRad = ParseVec3(AttrOr(je["origin"], "rpy", "0 0 0"));
                float[] rpyDeg = { Mathf.Rad2Deg * rpyRad[0], Mathf.Rad2Deg * rpyRad[1], Mathf.Rad2Deg * rpyRad[2] };
                float[] axis = ParseVec3(AttrOr(je["axis"], "xyz", "0 0 1"));

                bool isMoving = jtype == "revolute" || jtype == "continuous" || jtype == "prismatic";
                float lo = -180f, hi = 180f;
                var limit = je["limit"];
                if (jtype == "continuous") { lo = -180f; hi = 180f; }
                else if (limit != null)
                {
                    lo = Mathf.Rad2Deg * ParseFloat(AttrOr(limit, "lower", "-3.14159"), -Mathf.PI);
                    hi = Mathf.Rad2Deg * ParseFloat(AttrOr(limit, "upper", "3.14159"), Mathf.PI);
                }

                if (isMoving) dof++;
                // normalise type to the schema's two cases (revolute or fixed)
                string outType = isMoving ? "revolute" : "fixed";
                joints.Add(JointJson(jname, outType, parent, child, xyz, rpyDeg, axis,
                    isMoving ? new float[] { lo, hi } : null));
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"_comment\": \"Imported from URDF '{robotName}' by UrdfImporter\",\n");
            sb.Append("  \"units\": \"metres / degrees\",\n");
            sb.Append($"  \"dof_count\": {dof},\n");
            sb.Append("  \"servo_model\": \"Feetech STS3215\",\n");
            sb.Append("  \"links\": [\n    ").Append(string.Join(",\n    ", links)).Append("\n  ],\n");
            sb.Append("  \"joints\": [\n    ").Append(string.Join(",\n    ", joints)).Append("\n  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>Import a URDF file -> write kinematics JSON to persistentDataPath/Catalogue -> register
        /// it in RobotCatalogue. Returns the new descriptor (or null on failure).</summary>
        public static RobotDescriptor Import(string urdfPath)
        {
            try
            {
                string json = ConvertToKinematics(urdfPath, out int dof, out string name);
                string dir = Path.Combine(Application.persistentDataPath, "Catalogue");
                Directory.CreateDirectory(dir);
                string id = "urdf_" + Sanitize(name);
                string outPath = Path.Combine(dir, id + ".kin.json");
                File.WriteAllText(outPath, json);
                var d = new RobotDescriptor
                {
                    id = id, displayName = name + " (URDF)", dof = dof, source = "URDF import",
                    kinematicsPath = outPath, hasMeshes = false,
                    notes = "Imported from " + Path.GetFileName(urdfPath)
                };
                RobotCatalogue.Register(d);
                return d;
            }
            catch (Exception e) { Debug.LogError("[UrdfImporter] import failed: " + e.Message); return null; }
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────
        static string AttrOr(XmlElement e, string attr, string def) => e != null && e.HasAttribute(attr) ? e.GetAttribute(attr) : def;
        static string AttrOr(XmlNode n, string attr, string def) => AttrOr(n as XmlElement, attr, def);

        static float ParseFloat(string s, float def)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        static float[] ParseVec3(string s)
        {
            var parts = (s ?? "").Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var v = new float[3];
            for (int i = 0; i < 3 && i < parts.Length; i++) v[i] = ParseFloat(parts[i], 0f);
            return v;
        }

        static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            return sb.ToString();
        }

        static string LinkJson(string name, float mass, string meshFile)
        {
            string meshes = string.IsNullOrEmpty(meshFile) ? "[]"
                : $"[ {{ \"role\": \"primary_structure\", \"file\": \"{meshFile}\", \"mesh_xyz_m\": [0,0,0], \"mesh_rpy_deg\": [0,0,0] }} ]";
            return $"{{ \"name\": \"{name}\", \"meshes\": {meshes}, \"inertial\": {{ \"mass_kg\": {F(mass)}, \"com_xyz_m\": [0,0,0] }} }}";
        }

        static string JointJson(string name, string type, string parent, string child, float[] xyz, float[] rpyDeg, float[] axis, float[] limit)
        {
            string lim = limit == null ? "null" : $"[{F(limit[0])},{F(limit[1])}]";
            return "{ " +
                $"\"name\": \"{name}\", \"type\": \"{type}\", \"parent\": \"{parent}\", \"child\": \"{child}\", " +
                $"\"origin_xyz_m\": [{F(xyz[0])},{F(xyz[1])},{F(xyz[2])}], " +
                $"\"origin_rpy_deg\": [{F(rpyDeg[0])},{F(rpyDeg[1])},{F(rpyDeg[2])}], " +
                $"\"axis_local\": [{F(axis[0])},{F(axis[1])},{F(axis[2])}], " +
                $"\"limit_deg\": {lim} }}";
        }

        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
    }
}
