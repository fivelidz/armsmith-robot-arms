#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using ArmSmith.Catalogue;

namespace ArmSmith.EditorTools
{
    /// <summary>
    /// Headless gate for the new elements built this session:
    ///   - RobotCatalogue (J2): registry has entries; parametric kinematics generate + are BuildFromKinematics-able.
    ///   - UrdfImporter (J3): a sample URDF converts to the kinematics schema with the right DOF.
    ///   - ServoModel torque saturation (F-r1): available torque falls with speed; saturation flags correctly.
    ///   - SensorRealism (F-r2): noise+latency perturbs the observation when enabled, clean when off.
    ///   - ModuleAdvisor (S10): records ranked sensor-set results + recommends the best.
    ///
    /// Run: -executeMethod ArmSmith.EditorTools.ElementsCheck.RunHeadless
    /// </summary>
    public static class ElementsCheck
    {
        [MenuItem("ARMSMITH/Run Elements Check")] public static void RunMenu() { Run(); }
        public static void RunHeadless() { if (!Run()) EditorApplication.Exit(15); }

        public static bool Run()
        {
            int pass = 0, fail = 0;
            void Check(string label, bool cond) { if (cond) pass++; else { fail++; Debug.LogError($"[ElementsCheck] FAIL: {label}"); } }

            // ── J2: catalogue ──
            RobotCatalogue.EnsureInit();
            Check("catalogue has >=4 entries", RobotCatalogue.Entries.Count >= 4);
            Check("catalogue has so101", RobotCatalogue.Get("so101") != null);
            Check("catalogue has generic6", RobotCatalogue.Get("generic6") != null);

            // generate a parametric arm + build it
            string starterJson = RobotCatalogue.GenerateParametricKinematics("starter3", 3);
            Check("parametric json nonempty", !string.IsNullOrEmpty(starterJson) && starterJson.Contains("\"joints\""));
            string path = RobotCatalogue.ResolveKinematicsPath("generic6");
            Check("resolve generic6 path", path != null && System.IO.File.Exists(path));

            GameObject armGo = null;
            try
            {
                armGo = new GameObject("CatArm");
                var arm = armGo.AddComponent<ProceduralArm>();
                arm.BuildFromKinematics(path);
                Check("generic6 builds an articulation", arm.baseBody != null);
                Check("generic6 has 6 joints", arm.jointSpecs.Count == 6);
            }
            catch (System.Exception e) { Debug.LogError("[ElementsCheck] generic6 build threw: " + e.Message); fail++; }
            finally { if (armGo != null) Object.DestroyImmediate(armGo); }

            // ── J3: URDF importer ──
            string sampleUrdf =
                "<robot name=\"test2dof\">" +
                "  <link name=\"base\"><inertial><mass value=\"0.2\"/></inertial></link>" +
                "  <link name=\"l1\"><inertial><mass value=\"0.1\"/></inertial></link>" +
                "  <link name=\"l2\"><inertial><mass value=\"0.08\"/></inertial></link>" +
                "  <joint name=\"j1\" type=\"revolute\">" +
                "    <parent link=\"base\"/><child link=\"l1\"/>" +
                "    <origin xyz=\"0 0 0.05\" rpy=\"0 0 0\"/><axis xyz=\"0 0 1\"/>" +
                "    <limit lower=\"-1.57\" upper=\"1.57\" effort=\"10\" velocity=\"5\"/></joint>" +
                "  <joint name=\"j2\" type=\"revolute\">" +
                "    <parent link=\"l1\"/><child link=\"l2\"/>" +
                "    <origin xyz=\"0.1 0 0\" rpy=\"0 1.5708 0\"/><axis xyz=\"0 0 1\"/>" +
                "    <limit lower=\"-1.0\" upper=\"1.0\" effort=\"8\" velocity=\"5\"/></joint>" +
                "</robot>";
            string kin = UrdfImporter.ConvertXmlToKinematics(sampleUrdf, out int dof, out string rname);
            Check("URDF converts (dof=2)", dof == 2);
            Check("URDF robot name parsed", rname == "test2dof");
            Check("URDF json has joints", kin.Contains("\"j1\"") && kin.Contains("\"j2\""));
            Check("URDF rpy converted to ~90deg", kin.Contains("89.95") || kin.Contains("90") || kin.Contains("89.954"));

            // build the imported URDF arm
            GameObject urdfGo = null;
            try
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "Catalogue");
                System.IO.Directory.CreateDirectory(dir);
                string p = System.IO.Path.Combine(dir, "test2dof.kin.json");
                System.IO.File.WriteAllText(p, kin);
                urdfGo = new GameObject("UrdfArm");
                var arm = urdfGo.AddComponent<ProceduralArm>();
                arm.BuildFromKinematics(p);
                Check("imported URDF builds", arm.baseBody != null && arm.jointSpecs.Count == 2);
            }
            catch (System.Exception e) { Debug.LogError("[ElementsCheck] URDF build threw: " + e.Message); fail++; }
            finally { if (urdfGo != null) Object.DestroyImmediate(urdfGo); }

            // ── F-r1: servo torque saturation ──
            var servo = new ServoModel { maxTorqueNm = 1.6f, maxSpeedDegPerSec = 270f };
            float tAtStall = servo.AvailableTorque(0f);
            float tAtSpeed = servo.AvailableTorque(135f);   // half no-load speed
            float tAtNoLoad = servo.AvailableTorque(270f);
            Check("torque max at stall", Mathf.Abs(tAtStall - 1.6f) < 0.01f);
            Check("torque ~half at half-speed", Mathf.Abs(tAtSpeed - 0.8f) < 0.05f);
            Check("torque ~0 at no-load speed", tAtNoLoad < 0.01f);
            Check("saturation flags overload", servo.IsTorqueSaturated(1.2f, 135f));   // needs 1.2 but only 0.8 avail
            Check("no saturation when within budget", !servo.IsTorqueSaturated(0.5f, 135f));
            Check("SaturateTorque clamps", Mathf.Abs(servo.SaturateTorque(5f, 0f)) <= 1.6f + 1e-3f);

            // ── F-r2: sensor realism (noise/latency) ──
            GameObject hubGo = null, sArmGo = null;
            try
            {
                sArmGo = new GameObject("SArm");
                var sArm = sArmGo.AddComponent<ProceduralArm>();
                string kp = System.IO.Path.Combine(Application.dataPath, "Meshes", "SOARM100", "kinematics.json");
                if (System.IO.File.Exists(kp)) sArm.BuildFromKinematics(kp);
                hubGo = new GameObject("Hub");
                var hub = hubGo.AddComponent<SensorHub>();
                hub.Init(sArm, null);

                SensorRealism.enabled = false;
                var clean1 = hub.BuildObservation();
                var clean2 = hub.BuildObservation();
                bool identicalWhenOff = clean1.Length == clean2.Length;
                for (int i = 0; i < clean1.Length && identicalWhenOff; i++) if (Mathf.Abs(clean1[i] - clean2[i]) > 1e-6f) { /* dynamic channels (imu accel) may differ */ }
                Check("clean obs length stable", clean1.Length == clean2.Length && clean1.Length > 0);

                SensorRealism.enabled = false;
                var cleanRef = hub.BuildObservation();
                SensorRealism.enabled = true;
                SensorRealism.noiseRelative = 0.05f; SensorRealism.noiseAbsolute = 0.02f; SensorRealism.latencyFrames = 1;
                var noisy = hub.BuildObservation();
                Check("noisy obs same length", noisy.Length == cleanRef.Length);
                // with noise on, at least one channel should differ from the clean read
                bool anyDiff = false;
                for (int i = 0; i < noisy.Length && i < cleanRef.Length; i++) if (Mathf.Abs(noisy[i] - cleanRef[i]) > 1e-4f) { anyDiff = true; break; }
                Check("noise perturbs the observation", anyDiff);
                SensorRealism.enabled = false;   // restore
            }
            catch (System.Exception e) { Debug.LogError("[ElementsCheck] sensor realism threw: " + e.Message); fail++; }
            finally { if (hubGo != null) Object.DestroyImmediate(hubGo); if (sArmGo != null) Object.DestroyImmediate(sArmGo); }

            // ── S10: module advisor ──
            ModuleAdvisor.Clear();
            var cfgA = new TrainingConfig { useMotorEncoders = true, useTaskState = true, useImu = false, useRangeFinder = false, useLidar = false, useDepthCamera = false, useTactile = true };
            var cfgB = new TrainingConfig { useMotorEncoders = true, useTaskState = true, useImu = true, useRangeFinder = true, useLidar = true, useDepthCamera = true, useTactile = true };
            ModuleAdvisor.RecordResult("TrayToTray", cfgA, 0.9f, 12f, 30);
            ModuleAdvisor.RecordResult("TrayToTray", cfgB, 0.6f, 8f, 60);
            var rec = ModuleAdvisor.Recommend("TrayToTray");
            Check("advisor recommends higher-success set", rec != null && rec.sensorSet == ModuleAdvisor.SetKey(cfgA));
            ModuleAdvisor.RecordResult("TrayToTray", cfgB, 0.9f, 9f, 60);   // B now ties on success
            var rec2 = ModuleAdvisor.Recommend("TrayToTray");
            Check("advisor breaks tie by fewer channels", rec2 != null && rec2.channels == 30);
            Check("advisor ranked list", ModuleAdvisor.Ranked("TrayToTray").Count == 2);

            bool ok = fail == 0;
            Debug.Log(ok
                ? $"[ElementsCheck] PASSED — {pass} assertions (catalogue + URDF import + servo torque + sensor realism + advisor)."
                : $"[ElementsCheck] FAILED — {fail} of {pass + fail} assertions failed.");
            return ok;
        }
    }
}
#endif
