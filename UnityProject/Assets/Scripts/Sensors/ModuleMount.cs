using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>A valid attachment site on a robot part: a named socket on a link with a default local
    /// pose and the module types it accepts. Parts expose these so modules snap to sensible places.</summary>
    [Serializable]
    public class MountPoint
    {
        public string name;
        public int linkIndex;          // which jointBody (-1 = base) this socket is on
        public Vector3 localPosition;
        public Vector3 localEuler;
        public string[] acceptedTypes; // null/empty = any

        public Transform Link(ProceduralArm arm)
        {
            if (linkIndex < 0) return arm.baseBody != null ? arm.baseBody.transform : arm.transform;
            return linkIndex < arm.jointBodies.Count ? arm.jointBodies[linkIndex].transform : arm.transform;
        }
    }

    /// <summary>A placed module: a sensor/camera mounted at a pose on a link. Serializable -> saved with
    /// the arm config + exported for the real rig (so the sim camera/lidar pose matches reality).</summary>
    [Serializable]
    public class ModuleInstance
    {
        public string moduleType;      // e.g. "WristCam", "Lidar2D", "RangeFinder", "IMU"
        public int linkIndex;
        public Vector3 localPosition;
        public Vector3 localEuler;
        [NonSerialized] public Transform transform;   // runtime mounted transform
    }

    /// <summary>
    /// Module-mounting system (MM1-MM4): exposes MountPoints on the arm, lets a module be MOUNTED on a
    /// link at a pose (parented), re-pose/orient it, and list current modules. The mounted pose feeds the
    /// sensor (e.g. wrist-cam viewpoint, lidar origin/direction). Saved + exportable for sim->real.
    /// Built on the extensible sensor system; new module types/parts plug in without touching this.
    /// </summary>
    public class ModuleMount : MonoBehaviour
    {
        public ProceduralArm arm;
        public readonly List<MountPoint> mountPoints = new List<MountPoint>();
        public readonly List<ModuleInstance> modules = new List<ModuleInstance>();

        public void Setup(ProceduralArm a)
        {
            arm = a;
            mountPoints.Clear();
            // Default mount sockets: one on each link surface + a wrist-cam socket on the gripper link.
            int n = arm.jointBodies.Count;
            for (int i = 0; i < n; i++)
                mountPoints.Add(new MountPoint { name = arm.jointSpecs[i].name + "_surface", linkIndex = i,
                    localPosition = new Vector3(0f, arm.jointSpecs[i].linkLength * 0.5f, 0f), localEuler = Vector3.zero });
            // wrist cam socket on the last (wrist) link, looking forward-down
            mountPoints.Add(new MountPoint { name = "wrist_cam", linkIndex = Mathf.Max(0, n - 2),
                localPosition = new Vector3(0f, 0.02f, 0.03f), localEuler = new Vector3(25f, 0f, 0f),
                acceptedTypes = new[] { "WristCam", "DepthCamera", "RangeFinder" } });
        }

        /// <summary>Mount a module GameObject on a mount point (parents it + sets pose). Returns the instance.</summary>
        public ModuleInstance Mount(GameObject moduleGo, MountPoint mp, string moduleType)
        {
            var link = mp.Link(arm);
            moduleGo.transform.SetParent(link, false);
            moduleGo.transform.localPosition = mp.localPosition;
            moduleGo.transform.localRotation = Quaternion.Euler(mp.localEuler);
            var inst = new ModuleInstance { moduleType = moduleType, linkIndex = mp.linkIndex,
                localPosition = mp.localPosition, localEuler = mp.localEuler, transform = moduleGo.transform };
            modules.Add(inst);
            return inst;
        }

        /// <summary>Mount on an arbitrary link at a free pose (e.g. dropped via raycast).</summary>
        public ModuleInstance MountFree(GameObject moduleGo, int linkIndex, Vector3 localPos, Vector3 localEuler, string moduleType)
        {
            Transform link = linkIndex < 0 ? (arm.baseBody != null ? arm.baseBody.transform : arm.transform)
                                           : arm.jointBodies[linkIndex].transform;
            moduleGo.transform.SetParent(link, false);
            moduleGo.transform.localPosition = localPos;
            moduleGo.transform.localRotation = Quaternion.Euler(localEuler);
            var inst = new ModuleInstance { moduleType = moduleType, linkIndex = linkIndex,
                localPosition = localPos, localEuler = localEuler, transform = moduleGo.transform };
            modules.Add(inst);
            return inst;
        }

        /// <summary>Re-orient a mounted module (rotation handles / keys).</summary>
        public void Reorient(ModuleInstance inst, Vector3 deltaEuler)
        {
            if (inst.transform == null) return;
            inst.localEuler += deltaEuler;
            inst.transform.localRotation = Quaternion.Euler(inst.localEuler);
        }

        public void Remove(ModuleInstance inst)
        {
            if (inst.transform != null) Destroy(inst.transform.gameObject);
            modules.Remove(inst);
        }

        /// <summary>List of placed modules with their mount link + pose (for the panel / save / export).</summary>
        public string Summary()
        {
            if (modules.Count == 0) return "no modules mounted";
            var sb = new System.Text.StringBuilder();
            foreach (var m in modules)
                sb.Append($"{m.moduleType}@{(m.linkIndex < 0 ? "base" : arm.jointSpecs[m.linkIndex].name)} ");
            return sb.ToString();
        }
    }
}
