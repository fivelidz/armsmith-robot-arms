using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Central, REMAPPABLE key bindings for the high-level control actions. ArmController and the UI read
    /// these instead of hard-coded KeyCodes, so the player can rebind them in the Control view. Persisted to
    /// PlayerPrefs so a custom binding survives restarts.
    ///
    /// Per-JOINT jog keys keep their fixed labelled scheme (T/G, Y/H, ...) shown in Help — this store covers
    /// the actions players most want to remap (mode, gripper, pause, calibrate, mouse-follow).
    /// </summary>
    public static class KeyBindings
    {
        public enum Action { ToggleMode, GripOpen, GripClose, GripToggle, Pause, Calibrate, MouseFollow, Reset }

        static readonly Dictionary<Action, KeyCode> _defaults = new Dictionary<Action, KeyCode>
        {
            { Action.ToggleMode, KeyCode.Tab },
            { Action.GripOpen,   KeyCode.Comma },
            { Action.GripClose,  KeyCode.Period },
            { Action.GripToggle, KeyCode.Space },
            { Action.Pause,      KeyCode.P },
            { Action.Calibrate,  KeyCode.C },
            { Action.MouseFollow,KeyCode.M },
            { Action.Reset,      KeyCode.Backspace },
        };

        static readonly Dictionary<Action, KeyCode> _binds = new Dictionary<Action, KeyCode>(_defaults);
        static bool _loaded;

        public static readonly Action[] All =
            { Action.ToggleMode, Action.GripToggle, Action.GripOpen, Action.GripClose, Action.MouseFollow, Action.Pause, Action.Calibrate, Action.Reset };

        public static string Label(Action a)
        {
            switch (a)
            {
                case Action.ToggleMode:  return "Toggle IK / Manual";
                case Action.GripOpen:    return "Gripper open";
                case Action.GripClose:   return "Gripper close";
                case Action.GripToggle:  return "Gripper toggle";
                case Action.Pause:       return "Pause / hold";
                case Action.Calibrate:   return "Calibrate zero";
                case Action.MouseFollow: return "Mouse-follow IK";
                case Action.Reset:       return "Reset scenario";
                default:                 return a.ToString();
            }
        }

        public static KeyCode Get(Action a)
        {
            EnsureLoaded();
            return _binds.TryGetValue(a, out var k) ? k : _defaults[a];
        }

        public static void Set(Action a, KeyCode k)
        {
            EnsureLoaded();
            _binds[a] = k;
            PlayerPrefs.SetInt("armsmith.bind." + a, (int)k);
            PlayerPrefs.Save();
        }

        public static void ResetToDefaults()
        {
            foreach (var kv in _defaults) Set(kv.Key, kv.Value);
        }

        /// <summary>Convenience: was the bound key pressed this frame?</summary>
        public static bool Down(Action a) => Input.GetKeyDown(Get(a));
        public static bool Held(Action a) => Input.GetKey(Get(a));

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            foreach (var kv in _defaults)
            {
                int v = PlayerPrefs.GetInt("armsmith.bind." + kv.Key, (int)kv.Value);
                _binds[kv.Key] = (KeyCode)v;
            }
        }
    }
}
