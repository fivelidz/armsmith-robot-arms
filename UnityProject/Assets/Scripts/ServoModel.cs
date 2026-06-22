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
        // No-load speed: Feetech datasheet for the 12V STS3215 is 0.222 s/60deg => ~270 deg/s (the older
        // 0.16 s/60deg figure that circulates is unreliable — see research/arm_hardware/STS3215_SERVO_MODEL.md).
        // The 7.4V SO-101 follower is slower still; 270 is the faithful no-load ceiling, rate-limited below.
        public float maxSpeedDegPerSec = 270f;
        // Stall/peak torque: 7.4V SO-101 follower = 16.5 kg.cm ~= 1.62 N.m (12V variant is 30 kg.cm = 2.94 N.m).
        public float maxTorqueNm = 1.6f;
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

        // ── F-r1: STS3215 TORQUE SATURATION (datasheet-accurate speed/torque curve) ─────────────────────
        // A real DC-servo's AVAILABLE torque falls roughly linearly from stall torque (at zero speed) to
        // zero (at no-load speed): τ_avail(ω) = τ_stall · (1 − |ω| / ω_noload). The drive can never command
        // more than this, so a heavy load at speed slips/sags. This complements the rate-limit (which caps
        // SPEED) by capping FORCE — together they make the small SO-101 servos behave like the real motors.

        /// <summary>Torque the servo can still deliver at a given angular speed (deg/s). Clamped to [0, max].</summary>
        public float AvailableTorque(float speedDegPerSec)
        {
            float frac = 1f - Mathf.Abs(speedDegPerSec) / Mathf.Max(1f, maxSpeedDegPerSec);
            return Mathf.Clamp01(frac) * maxTorqueNm;
        }

        /// <summary>Saturate a requested torque to what the motor can actually produce at the current speed.
        /// Returns the deliverable torque (same sign as request).</summary>
        public float SaturateTorque(float requestedNm, float speedDegPerSec)
        {
            float cap = AvailableTorque(speedDegPerSec);
            return Mathf.Clamp(requestedNm, -cap, cap);
        }

        /// <summary>True if a required holding/move torque EXCEEDS what the servo can deliver at this speed —
        /// i.e. the joint will sag/slip. Useful for honest UI feedback + training penalties.</summary>
        public bool IsTorqueSaturated(float requiredNm, float speedDegPerSec)
            => Mathf.Abs(requiredNm) > AvailableTorque(speedDegPerSec) + 1e-4f;
    }
}
