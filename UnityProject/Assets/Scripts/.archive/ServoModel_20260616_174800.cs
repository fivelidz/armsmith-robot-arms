using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Models a real serial-bus servo (Feetech STS3215 by default) so that EVERY in-game joint command
    /// corresponds to an actual servo position command. This makes the sim a true digital twin: the
    /// joint angle you drive in the game is converted to the exact servo tick the real motor would
    /// receive (and clamped to the same limits / rate), so recorded behaviour ports 1:1 to hardware.
    ///
    /// STS3215: 4096 ticks / 360 deg, 1 Mbit/s TTL bus, ~0.088 deg/tick, ~16.5-30 kg.cm @ 12V.
    /// Conventions match ~/projects/robot_hand/python/servo_controller.py.
    /// </summary>
    [System.Serializable]
    public class ServoModel
    {
        public int servoId = 1;
        public int ticksPerRev = 4096;
        public float centerDeg = 180f;     // tick 2048 = 180 deg (servo zero is mid-range)
        public float maxSpeedDegPerSec = 360f;   // realistic STS3215 no-load ~ 0.16s/60deg => ~375 deg/s
        public float maxTorqueNm = 1.6f;   // ~16.5 kg.cm at the horn radius -> N.m
        public float minDeg = -180f, maxDeg = 180f;
        public bool invert = false;
        public float offsetDeg = 0f;

        /// <summary>Game joint angle (deg, 0=neutral) -> raw servo tick [0..ticksPerRev).</summary>
        public int AngleToTick(float jointDeg)
        {
            float d = jointDeg;
            if (invert) d = -d;
            d += offsetDeg + centerDeg;            // shift so neutral maps to centre tick
            float frac = d / 360f;
            int tick = Mathf.RoundToInt(frac * ticksPerRev);
            return Mathf.Clamp(tick, 0, ticksPerRev - 1);
        }

        public float TickToAngle(int tick)
        {
            float d = tick / (float)ticksPerRev * 360f - centerDeg - offsetDeg;
            if (invert) d = -d;
            return d;
        }

        /// <summary>Rate-limit a commanded angle by the servo's max speed over dt (digital-twin fidelity).</summary>
        public float RateLimit(float currentDeg, float commandedDeg, float dt)
        {
            float maxStep = maxSpeedDegPerSec * dt;
            float delta = Mathf.Clamp(commandedDeg - currentDeg, -maxStep, maxStep);
            return Mathf.Clamp(currentDeg + delta, minDeg, maxDeg);
        }
    }
}
