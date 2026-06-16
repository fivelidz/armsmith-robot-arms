# Feetech STS3215 — Servo Model Research for the ARMSMITH Digital Twin

Created 2026-06-16. Purpose: make `ServoModel.cs` and the `UrdfArm.cs` ArticulationBody
drive config faithful to the real Feetech STS3215 so that commanded joint angles transfer
to hardware (SO-101 / SO-ARM100). Prioritises primary sources (Feetech datasheet, LeRobot
source, TheRobotStudio MuJoCo model). **Uncertain items are flagged ⚠.**

---

## 0. TL;DR for tuning (read this first)

- The STS3215 is a **position-controlled smart serial servo**. Its internal loop is a
  **P-dominant PID** on encoder ticks (registers P/D/I at addresses 21/22/23). Factory
  default is roughly **P≈32, D≈0, I≈0** ⚠ (see §2) — i.e. an almost pure proportional
  position hold with a small deadband, NOT a stiff industrial servo.
- **4096 ticks / 360°** is confirmed (LeRobot `MODEL_RESOLUTION["sts3215"] = 4096`;
  datasheet "360° when 0~4096"). Resolution ≈ **0.088°/tick**.
- It holds position with a **deadband (CW/CCW dead zone, default ±1 tick ≈ ±0.088°)** and
  **noticeable compliance** — under static gravity load it *sags* until the proportional
  error generates enough torque to balance the load. This is the single most important
  real-world behaviour to reproduce.
- The community-standard SO-101 **MuJoCo** model (TheRobotStudio, official) uses, per joint:
  `kp = 998.22`, `kv = 2.731`, `forcerange = ±2.94 N·m` (joint) / `±3.35` (actuator),
  `damping = 0.60`, `frictionloss = 0.052`, `armature = 0.028`, and **±0.5° backlash**.
  These were derived assuming the **servo P-gain = 16** (half of factory default). This is
  the best single reference point for matching sim ↔ real.
- For Unity: drive `stiffness` is the analogue of MuJoCo `kp` (≈ servo P-gain × torque-const
  scaling); `damping` ≈ `kv`; `forceLimit` = stall torque ≈ **2.94 N·m @ 12 V (or ~1.5 N·m @
  7.4 V)**. A rate limiter (already in `ServoModel.RateLimit`) approximates the servo's
  internal acceleration/velocity profile.

---

## 1. Electrical / mechanical spec

Source: official Feetech product page for the 12 V ST-3215-C018 variant
(https://www.feetechrc.com/525603.html) and the SO-ARM100 BOM
(https://github.com/TheRobotStudio/SO-ARM100).

| Parameter | 12 V variant (C018) | 7.4 V variant (SO-101 follower, C001 etc.) | Notes |
|---|---|---|---|
| **Stall (peak) torque** | **30 kg·cm = 2.94 N·m @ 12 V** | **16.5 kg·cm ≈ 1.62 N·m @ 6 V** (~1.5 N·m @ 5 V) | Datasheet "Peak stall torque 30kg.cm@12V"; SO-ARM100 README states 7.4 V version = 16.5 kg·cm @ 6 V |
| **Rated (continuous) torque** | **10 kg·cm ≈ 0.98 N·m @ 12 V** | ~lower, ⚠ not separately published | Datasheet "Rated torque 10kg.cm@12V" — note rated ≈ 1/3 of stall |
| **No-load speed** | **0.222 s/60° @ 12 V ≈ 45 rev/min ≈ 270°/s** | slower at 7.4 V ⚠ | Datasheet "0.222sec/60°@12V". Older 0.16 s/60° figures circulate ⚠ |
| **No-load current** | 180 mA @ 12 V | — | Datasheet |
| **Stall current** | 2.7 A @ 12 V | — | Datasheet |
| **Voltage range** | **4–14 V operating** | follower 7.4 V nominal, leader 7.4 V | Datasheet "Operating Voltage 4-14V"; SO-101 follower can be 7.4 V *or* 12 V |
| **Gear ratio** | not on C018 page ⚠ | **1/345 (C001 follower), 1/191 (C044), 1/147 (C046)** | SO-ARM100 BOM lists gear ratio per part number. *Different joints on the leader use different ratios.* The follower uses **1/345 on all 6 joints** |
| **Gear / case material** | steel gears, PA+GF case, ball bearings | same | Datasheet |
| **Encoder resolution** | **4096 ticks/rev (12-bit magnetic absolute)** ✅ confirmed | same | Datasheet "12 bit high precision magnetic"; LeRobot `MODEL_RESOLUTION=4096`. ⇒ **0.0879°/tick** |
| **Position range** | **0–360° (0–4095 ticks)**; "multi-turn continuous" mode available | same | Datasheet. In single-turn position mode the usable range is the full 360°; SO-101 joints are software-limited to subranges (see §5) |
| **Backlash** | not published by Feetech ⚠ | **modelled as ±0.5° in the SO-101 MuJoCo** | TheRobotStudio MuJoCo `class="backlash"` uses ±0.008727 rad = ±0.5°. Treat as best community estimate, not a datasheet value ⚠ |
| **Weight / size** | 55 g, 45.2 × 24.7 × 35 mm | same | Datasheet |
| **Operating temp** | −20 to +60 °C | same | Datasheet |

**Model number** = 777 (LeRobot `MODEL_NUMBER_TABLE`). Protocol = Feetech "SMS/STS" = a
Dynamixel-Protocol-1-like half-duplex packet protocol (LeRobot `MODEL_PROTOCOL=0`).

---

## 2. Control modes & the actual control law

Source: LeRobot Feetech control table
(`src/lerobot/motors/feetech/tables.py`, `feetech.py`) and Feetech FT-SMS/STS e-manual
(referenced in that file: `http://doc.feetech.cn/#/prodinfodownload?srcType=FT-SMS-STS-emanual-...`).

### 2.1 Operating modes (register `Operating_Mode`, addr 33)
From LeRobot `OperatingMode` enum:
- **0 = POSITION** (servo/joint mode) — *this is what SO-101 / LeRobot uses.*
- **1 = VELOCITY** (constant-speed, controlled by Goal_Velocity addr 46, bit15 = direction).
- **2 = PWM** (open-loop voltage/duty, parameter 0x2c run-time, bit11 = direction).
- **3 = STEP** (step-servo, parameter 0x2a count, bit15 = direction).

There is **no dedicated current/torque-control mode** like Dynamixel's "Current Control
Mode." Torque is limited via `Torque_Limit`/`Max_Torque_Limit`, and a "Goal current"/
"Protection current" exists, but closed-loop torque control is not exposed the way a
Dynamixel XM does. ⚠ For a torque-faithful twin, treat the STS3215 as **position-mode-only**.

### 2.2 The position control law (what actually runs inside the servo)
The internal loop is a **digital PID on position error (in encoder ticks)** producing a PWM
duty to the motor. The relevant registers (LeRobot `STS_SMS_SERIES_CONTROL_TABLE`):

| Register | Addr | Role |
|---|---|---|
| `P_Coefficient` | 21 | **Position proportional gain** (the dominant term) |
| `D_Coefficient` | 22 | Position derivative gain |
| `I_Coefficient` | 23 | Position integral gain |
| `Minimum_Startup_Force` | 24 | min PWM/torque to overcome stiction ("punch") |
| `CW_Dead_Zone` | 26 | clockwise deadband (ticks) |
| `CCW_Dead_Zone` | 27 | counter-clockwise deadband (ticks) |
| `Protection_Current` | 28 | over-current trip |
| `Protective_Torque` | 34 | torque held after overload trips (compliance fallback) |
| `Overload_Torque` | 36 | torque threshold for overload protection |
| `Acceleration` | 41 | trajectory accel for goal moves |
| `Goal_Position` | 42 | commanded tick |
| `Goal_Time` | 44 | time-to-target (for timed moves) |
| `Goal_Velocity` | 46 | speed cap for the move |
| `Torque_Limit` | 48 | runtime torque ceiling |

**Control law (effective):** `PWM_duty = clamp( P·e + D·(de/dt) + I·∫e , ±Torque_Limit )`,
where `e = Goal_Position − Present_Position` in ticks, with the result **zeroed inside the
dead zone** and floored at `Minimum_Startup_Force`. Because D and I default to 0, the real
behaviour is essentially **proportional position control with a deadband and a torque cap**
→ a *spring to target* with saturation. This is exactly an ArticulationBody PD drive with
high P, low D, finite force limit, plus a deadband.

**Default PID values ⚠ (uncertain — Feetech does not publish a single canonical set):**
- Community/SDK-observed factory defaults are commonly cited as **P ≈ 32, D ≈ 0, I ≈ 0**
  (8-bit registers, range 0–255). ⚠ Some firmware/units ship P in the high-teens to ~32.
- The official **SO-101 MuJoCo model derived its gains assuming P = 16** (see its XML
  comment, §6), i.e. they assumed a *softer-than-default* proportional gain. Treat **P in
  the range 16–32** as the realistic operating band and expose it as a tunable.
- **Verify on the actual arm** by reading addr 21 over the bus before trusting any number.

### 2.3 Stiffness / compliance / deadband behaviour
- **Holds position via a proportional spring, not rigidly.** With P≈16–32 and a finite
  torque cap, a loaded joint settles at the position where `P·e = load_torque`, i.e. it
  **sags** by a steady-state error proportional to load / P. This is real and must be
  modelled (sim that holds perfectly will over-promise).
- **Deadband**: default CW/CCW dead zone is small (≈1 tick ≈ 0.088°) ⚠ but means tiny
  errors produce no correction → a ±0.1° "slop" around target on top of backlash.
- **Compliance is *not* separately programmable** the way Dynamixel AX "compliance
  margin/slope" was; you change effective compliance by changing P and Torque_Limit.
- LeRobot sets `Maximum_Acceleration=254` and `Acceleration=254` in `configure_motors()`
  to make moves snappy, and clears Phase bit 4 on the STS3215 so position reads stay in
  `[0, 4095]`.

---

## 3. Position-command → motion mapping (timing, bus, loop rate)

Source: LeRobot `feetech.py` (`DEFAULT_BAUDRATE = 1_000_000`, `Return_Delay_Time`,
patch_setPacketTimeout), datasheet, SO-ARM100 docs.

- **Command units on the wire:** raw ticks (0–4095). LeRobot exposes `Goal_Position` /
  `Present_Position` *normalized* to a calibrated range, but at the bus level it's ticks.
  `steps = round(deg/360 × 4096)` — matches `ServoModel.AngleToTick`.
- **Baud rate:** datasheet supports **38400 bps – 1 Mbps**; LeRobot default = **1,000,000
  bps**. Baud register table tops out at 1 Mbps for STS.
- **Return delay:** factory default `Return_Delay_Time = 250` ⇒ **500 µs** response delay;
  LeRobot reduces it to 0 (≈2 µs) in `configure_motors()` to cut latency.
- **Internal trajectory / rate limiting:** in position mode the servo runs an internal
  **acceleration-limited move** toward Goal_Position governed by `Acceleration` (addr 41,
  also `Maximum_Acceleration` addr 85) and `Goal_Velocity` (addr 46). With LeRobot's
  `Acceleration=254` this ramp is short but non-zero — there's a finite slew, not a teleport.
  This is what `ServoModel.RateLimit` approximates with `maxSpeedDegPerSec`.
- **Bus throughput / control-loop Hz for SO-101 (6 servos on one half-duplex bus):**
  - One full-duplex round trip per servo ≈ packet (8–10 bytes) + status + return delay.
    At 1 Mbps, a sync-write to all 6 + a sync-read of 6 positions is on the order of
    **0.5–1.5 ms** of bus time. ⚠ exact figure depends on packet length & USB latency.
  - LeRobot uses **GroupSyncRead / GroupSyncWrite** so all 6 joints are written in one
    packet (positions) and read in one packet (feedback).
  - Practical LeRobot teleop/record/policy loops run at **~30–60 Hz** (often gated by the
    camera / policy, not the bus). The bus itself can sustain **~100–200 Hz** for 6 servos.
    ⚠ Community reports 1 Mbps + low USB latency timer needed to hit the high end.
  - **USB-serial latency** (FTDI/CH340 latency timer, default 16 ms on some adapters) is the
    usual real-world bottleneck; LeRobot's `patch_setPacketTimeout` and low return-delay
    mitigate it. Flag this when porting: set the latency timer to 1 ms.

**Twin implication:** at the sim's physics rate (Unity default 50 Hz / 0.02 s, this project
runs ~120 Hz per MotorPhysicsCheck), each commanded angle should be (a) tick-quantised, (b)
rate-limited to ~270°/s no-load (scaled down under load — see §4), and (c) you may model a
1–2 frame command latency to mirror bus + USB delay. ⚠ latency is small; optional.

---

## 4. Real-world behaviour under gravity load

Sources: datasheet (rated vs stall), SO-ARM100 README torque notes, MuJoCo model choices,
general serial-servo behaviour.

- **Do they sag/droop under static load? YES.** Because the hold is proportional, a joint
  bearing a gravity moment settles at `e_ss = load_torque / (P·k)` where `k` converts P to
  torque/tick. With the softer P (≈16) used by SO-101 sims, **several degrees of static
  droop under a loaded extended arm is expected and realistic.** The ARMSMITH notes in
  `UrdfArm.cs` describe exactly this (wrist sagging 35–38° when stiffness too low) — the fix
  there (gravity feed-forward + higher drive) is the standard robotics answer and is fine
  for gameplay, but for *fidelity* you should allow a few degrees of load-dependent droop.
- **Holding torque vs rated torque:** holding (continuous) torque ≈ **rated 10 kg·cm
  (~0.98 N·m) @ 12 V**, far below the **30 kg·cm stall**. Sustained holding near stall
  overheats. So the *usable* static torque for the twin's `forceLimit` should be closer to
  **~1.0 N·m continuous (12 V)** / **~0.5 N·m (7.4 V)** for a steady hold, with the
  **2.94 N·m / 1.5 N·m stall used only as the saturation ceiling** for transients.
- **Overheating / torque dropoff:** the STS3215 has temperature feedback (`Present_Temperature`
  addr 63, `Max_Temperature_Limit` default ~70–80 °C) and **overload protection**
  (`Overload_Torque`, `Protection_Time`, `Protective_Torque`). Under sustained high load it
  will **reduce torque / unload (drop to Protective_Torque) to protect itself**, then can
  cut torque entirely. This is a well-known SO-101 community gotcha: a heavily-loaded or
  stalled follower joint goes limp after a few seconds. ⚠ Worth a sim flag ("thermal
  unload") if you want to reproduce failure modes, but not essential for nominal tuning.
- **Torque falls with voltage:** 7.4 V follower has roughly **half** the torque of the 12 V
  variant. If the target hardware is the cheap 7.4 V kit, tune `forceLimit` to ~1.5 N·m
  stall / ~0.5 N·m continuous.

---

## 5. How LeRobot / the SO-101 community drives them

Sources: LeRobot `feetech.py`, `tables.py`; SO-ARM100 README; SO-101 MuJoCo model.

- **Driver:** `FeetechMotorsBus` (LeRobot) wraps the `scservo_sdk` (Feetech's Dynamixel-SDK
  fork). Protocol version 0, default baud 1 Mbps.
- **Units:** LeRobot commands joints in **calibrated/normalized position**; user-facing API
  (`robot.send_action`) takes **degrees** for SO-101 (matches this project's waypoint schema,
  which is in degrees). Internally degrees → ticks.
- **Calibration:** LeRobot records per-motor `Homing_Offset` (addr 31), `Min_Position_Limit`
  (9) and `Max_Position_Limit` (11). Relationship: **`Present_Position = Actual_Position −
  Homing_Offset`**. Half-turn homing sets offset so the mid-range tick (2047) is "center" —
  exactly the `centerDeg = 180°` / tick-2048 convention already in `ServoModel.cs`. ✅
- **`configure_motors()` defaults LeRobot applies:** `Return_Delay_Time=0`,
  `Maximum_Acceleration=254`, `Acceleration=254`, and clears Phase bit 0x10 on STS3215.
- **Sign-magnitude encoding:** Goal/Present position use **bit 15 as sign**; Present_Load
  uses bit 10, Homing_Offset bit 11 (LeRobot `STS_SMS_SERIES_ENCODINGS_TABLE`). Important if
  the recorder reads raw values.
- **Safety idioms:** `ensure_safe_goal_position` / `max_relative_target` clamp per-step
  deltas — mirror this in the sim's rate limiter and in `scripts/realbot/safety.py`.
- **Sim-to-real gotchas the community has hit:**
  1. **Position overflow / negative reads** on STS3215 unless Phase bit 4 is cleared
     (LeRobot does this explicitly). Read positions can wrap past 4095 otherwise.
  2. **USB latency timer** (16 ms default) destroys loop rate — set to 1 ms.
  3. **Unofficial PyPI `feetech-servo-sdk`** has a packet-timeout bug LeRobot monkey-patches
     (`patch_setPacketTimeout`). Use LeRobot's patched path.
  4. **Gear-ratio mismatch leader vs follower** (1/345 vs 1/147/1/191) → different speed and
     backdrivability per joint; a one-size joint model is an approximation. ⚠ Follower
     (the one you'd run policies on) is uniform 1/345.
  5. **Thermal unload under sustained load** (see §4) surprises people doing long holds.
  6. **No true torque mode** — don't expect Dynamixel-style current control.

---

## 6. Recommendations — matching a Unity ArticulationBody PD drive to the STS3215

### 6.1 The reference numbers (official SO-101 MuJoCo, TheRobotStudio)
File `Simulation/SO101/so101_new_calib.xml` (vendored upstream), `class="sts3215"`:

```xml
<joint    damping="0.60" frictionloss="0.052" armature="0.028"/>
<position kp="998.22" kv="2.731" forcerange="-2.94 2.94"/>   <!-- per joint -->
<!-- actuator-level forcerange = ±3.35 N·m, ctrlrange = joint limits in rad -->
<!-- backlash class: joint range ±0.008727 rad (±0.5°), damping 0.01, armature 0.01 -->
```
**Crucial comment in that file:** *"These gains are not a 1-to-1 mapping of the servo gains
used in Lerobot… assuming that the servo proportional gain is set to 16."* So `kp=998.22`
is the *physical torque-per-radian* the servo produces when its register **P = 16**.

### 6.2 The mapping (servo register P → sim kp), from the published derivation
Source: Gregory119/RBE501-RL-arm-project `gymnasium_env/README.md` (the doc the MuJoCo
model cites). Modelling the servo as a DC motor under proportional voltage control:

```
τ_m = Km·(V − Vb)/R ,   V = Gp·(180/π)·(θ_d − θ) ,   Vb = Kb·θ̇
⇒ τ_m = [Km·Gp·(180/π)/R]·(θ_d − θ)  −  [Km·Kb/R]·θ̇
        └──────────  kp  ──────────┘     └──── kv ────┘
```
So:
- **kp = Km·Gp·(180/π)/R**  →  **kp is LINEAR in the servo P-gain Gp.**
- **kv = Km·Kb/R** (a fixed back-EMF damping, independent of Gp).
- `forcerange` = stall torque (±2.94 N·m @ 12 V).

That means if you read a *different* P off the real servo (say P=32 instead of 16), scale
`kp` proportionally: **kp(P) = 998.22 × (P/16)**. (e.g. P=32 → kp ≈ 1996.)

### 6.3 Translating MuJoCo `kp/kv/forcerange` → Unity ArticulationDrive
Unity's `ArticulationDrive` torque (in **stiffness mode**, the default this project uses) is:

```
τ = stiffness·(target − q) − damping·q̇ ,   clamped to ±forceLimit
```
MuJoCo `position` actuator with `kp`,`kv` gives `τ = kp·(target − q) − kv·q̇`, clamped to
`forcerange`. **They are the same PD law**, so the mapping is direct *provided units match*:

| MuJoCo | Unity ArticulationDrive | Unit note |
|---|---|---|
| `kp` (N·m/rad) | `stiffness` | ⚠ **Unity drive uses DEGREES**: `stiffness` is N·m per *degree*. So `stiffness = kp × (π/180) = kp × 0.01745`. |
| `kv` (N·m·s/rad) | `damping` | likewise per deg/s: `damping = kv × 0.01745`. |
| `forcerange` (N·m) | `forceLimit` | same units (N·m), direct. |
| `damping`(joint) + `frictionloss` + `armature` | partly `damping`, partly link inertia | Unity has no frictionloss; fold a little into `damping`. `armature` ≈ extra rotor inertia — Unity has no direct field; can bump link inertiaTensor slightly. ⚠ |

**Worked target values for a 12 V STS3215, assuming servo P = 16 (the MuJoCo baseline):**
- `stiffness = 998.22 × 0.01745 ≈ **17.4 N·m/deg**`  ⚠ — this is *physically correct* but is
  **orders of magnitude lower** than this project's current `stiffness = 20000/14000/6000`.
  The project's high numbers exist because (a) the procedural links are very light (0.08–0.15
  kg) and under-damped low stiffness let them sag/oscillate, and (b) gravity feed-forward was
  added later. **For a faithful twin you have two coherent options:**
  1. **Physical-units route (recommended for fidelity):** use realistic inertia (the MuJoCo
     masses: base 0.147, links ~0.10 kg, gripper 0.087 kg) **and** `stiffness ≈ 17 N·m/deg`,
     `damping ≈ 2.73×0.01745 ≈ 0.048 N·m·s/deg`, `forceLimit = 2.94 N·m` (12 V) / `1.5`
     (7.4 V). Then **add gravity feed-forward** (already present) so the small kp doesn't
     leave huge static droop — this is exactly what real users do (and the droop that
     remains is the *real* servo droop you want).
  2. **Gameplay route (current code):** keep the high stiffness for visual stability, but
     **expose a "servo fidelity" mode** that swaps in the physical values above when exporting
     /validating against hardware. Document that high-stiffness mode does NOT predict real
     droop.
- **Recommended starting drive (per joint, 12 V, fidelity mode):**

| Joint(s) | stiffness (N·m/deg) | damping (N·m·s/deg) | forceLimit (N·m) | rate limit (°/s) |
|---|---|---|---|---|
| all SO-101 joints (uniform 1/345 follower) | **17.4** (P=16) … **34.8** (P=32) | **0.05** | **2.94** (12 V) / **1.5** (7.4 V) | **270** no-load, fall to ~0 near stall |
| backlash (optional extra joint) | free, ±0.5° | 0.01 | — | — |

  Keep it **uniform across joints** — the real follower uses the *same* servo & gear on every
  joint; the per-tier scaling in `UrdfArm.cs` is a gameplay stabiliser, not physical.

### 6.4 Putting the servo's discrete/non-ideal behaviour into the twin
To match *observed* settle/hold behaviour (not just the PD law):
1. **Tick-quantise** the target: `target = TickToAngle(AngleToTick(commandedDeg))` →
   reproduces 0.088° resolution. (ServoModel already converts; feed the quantised value to
   the drive target.)
2. **Deadband**: if `|target − q| < ~0.1°`, hold (zero corrective torque) → reproduces the
   CW/CCW dead zone. Optional; small effect.
3. **Backlash**: add ±0.5° of free play (a small free joint, as the MuJoCo model does) or a
   dead-zone in the transmission. This is the main source of sim-real position mismatch at
   the tip.
4. **Rate limiter**: keep `ServoModel.RateLimit`. Set `maxSpeedDegPerSec ≈ 270` (12 V
   no-load) and optionally **scale it down with load** (the N-T curve: speed → 0 as torque →
   stall) for realism. ⚠ Current code uses 360 — lower to ~270.
5. **Continuous-torque ceiling for holds**: for steady-state holding tests use `forceLimit ≈
   1.0 N·m` (12 V continuous) rather than the 2.94 stall, so the twin droops like the real
   servo on long holds. Use the full stall only for transient moves.
6. **Latency** (optional): delay the applied target by 1 physics frame to mimic bus + USB.

### 6.5 What to set in `ServoModel.cs` (concrete edits to consider — not yet applied)
- `maxSpeedDegPerSec`: **360 → 270** (0.222 s/60° @ 12 V). For 7.4 V use ~180–200 ⚠.
- `maxTorqueNm`: keep **1.6** if targeting the 7.4 V follower (≈16.5 kg·cm), or set **2.94**
  for the 12 V variant. Add a `continuousTorqueNm ≈ 1.0` (12 V) / `0.5` (7.4 V) field for holds.
- Add tunables: `servoPGain` (default 16, range 0–255), and derive drive stiffness from it
  via `stiffnessNmPerDeg = 998.22 × (servoPGain/16) × (π/180)`.
- Add `deadbandDeg ≈ 0.088`, `backlashDeg ≈ 0.5` (flagged ⚠ estimate).
- `ticksPerRev = 4096`, `centerDeg = 180` already correct. ✅

---

## 7. Confidence / flags

| Claim | Confidence | Source |
|---|---|---|
| 4096 ticks/rev, 0.088°/tick | **High** ✅ | Datasheet + LeRobot code |
| 30 kg·cm stall @12 V / 16.5 kg·cm @6 V (7.4 V kit) | **High** ✅ | Feetech datasheet + SO-ARM100 README |
| Rated 10 kg·cm @12 V (continuous ≪ stall) | **High** ✅ | Datasheet |
| No-load 0.222 s/60° @12 V (~270°/s) | **High** ✅ | Datasheet (older 0.16 s figures ⚠) |
| Gear ratios 1/345, 1/191, 1/147 by part | **High** ✅ | SO-ARM100 BOM |
| Position-mode P/D/I registers @ 21/22/23 | **High** ✅ | LeRobot tables.py + Feetech e-manual ref |
| Internal law = P-dominant PID + deadband + torque cap | **High** ✅ | register semantics + mode enum |
| **Factory default P≈32 / D=0 / I=0** | **Low ⚠** | community/SDK lore — **read addr 21 on the real arm** |
| MuJoCo kp=998.22 / kv=2.731 / force ±2.94, ±0.5° backlash, P=16 assumption | **High** ✅ | TheRobotStudio official MuJoCo XML |
| kp ∝ servo P-gain mapping | **High** ✅ | derivation cited by the MuJoCo model |
| Backlash = ±0.5° | **Medium ⚠** | modelling choice, not a Feetech datasheet value |
| Thermal unload under sustained load | **Medium ⚠** | overload registers exist + community reports |
| Bus can do ~100–200 Hz for 6 servos @1 Mbps; loops run 30–60 Hz | **Medium ⚠** | 1 Mbps default in code + general practice; not benchmarked here |
| No true torque/current control mode | **High** ✅ | LeRobot OperatingMode enum (only POS/VEL/PWM/STEP) |

---

## 8. Sources (URLs)

1. **Feetech STS3215 (12 V, ST-3215-C018) product/datasheet page** —
   https://www.feetechrc.com/525603.html  (stall 30 kg·cm@12V, rated 10 kg·cm, 0.222 s/60°,
   4–14 V, 4096, 360°). PDF: linked "ST-3215-C018-串型规格书-20230720.pdf" on that page.
2. **Feetech FT-SMS/STS e-manual / register doc** (referenced by LeRobot) —
   http://doc.feetech.cn/  (control table addresses, PID/dead-zone registers).
3. **TheRobotStudio SO-ARM100 / SO-101 repo** (BOM: gear ratios, 7.4 V vs 12 V torque, kits,
   debugging) — https://github.com/TheRobotStudio/SO-ARM100
4. **Official SO-101 MuJoCo model** (kp/kv/forcerange, backlash, P=16 note) —
   https://github.com/TheRobotStudio/SO-ARM100/blob/main/Simulation/SO101/so101_new_calib.xml
5. **Servo→sim gain derivation** (kp = Km·Gp·180/π / R, kv = Km·Kb/R) —
   https://github.com/Gregory119/RBE501-RL-arm-project/blob/main/gymnasium_env/README.md
6. **LeRobot Feetech driver** (baud 1 Mbps, modes, calibration, configure_motors, Phase fix,
   sign-magnitude) — https://github.com/huggingface/lerobot/blob/main/src/lerobot/motors/feetech/feetech.py
7. **LeRobot Feetech control table** (exact register addresses & resolution=4096) —
   https://github.com/huggingface/lerobot/blob/main/src/lerobot/motors/feetech/tables.py
8. **LeRobot SO-101 setup/assembly docs** — https://huggingface.co/docs/lerobot/so101
9. **(Comparison) Dynamixel XM430-W350 e-manual** — used only to understand the standard
   position-PID + profile-velocity/acceleration control architecture that Feetech mirrors —
   https://emanual.robotis.com/docs/en/dxl/x/xm430-w350/
10. **Linux Feetech debug GUI** (read live P/D/I & test) —
    https://github.com/Kotakku/FT_SCServo_Debug_Qt  ;  Feetech software:
    https://www.feetechrc.com/software.html

---

## 9. Action items for ARMSMITH (informational — no code changed yet)

- [ ] On the real arm, **read register 21 (P), 22 (D), 23 (I), 26/27 (dead zone)** per joint
      and record them in `scripts/realbot/joint_map.json`. This resolves the biggest ⚠.
- [ ] Add `servoPGain`, `continuousTorqueNm`, `deadbandDeg`, `backlashDeg`, and voltage
      (`7.4`/`12`) fields to `ServoModel.cs`; derive drive stiffness from P via §6.2.
- [ ] Lower `ServoModel.maxSpeedDegPerSec` 360 → ~270 (12 V) and scale with load.
- [ ] Add a **"servo fidelity" drive mode** to `UrdfArm.cs` that swaps the gameplay
      stiffness tiers for the physical PD values (§6.3) + gravity FF, for hardware validation.
- [ ] Optionally model **thermal unload** and **±0.5° backlash** for failure-mode realism.
