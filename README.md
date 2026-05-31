# UCR Curved Axis Plugins

A set of [Universal Control Remapper (UCR)](https://github.com/Snoothy/UCR) plugins that map digital buttons to analog axis outputs with smooth, configurable curves — designed for games where analog precision matters but you only have a keyboard or digital buttons.

I use this mainly for Forza Horizon 6 RWD Car for better throttle control when using keyboard without traction control settings enabled. But you can use it for any game with any UCR supported controller.

## Requirements
.Net Framework Runtime 4.6.2 or above, get from [here](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net462).

## Installation
* Download UCR from the [URC Releases](https://github.com/snoothy/ucr/releases) page, extract the contents to your desired directory, you should see `Plugins` folder.
* Download the latest release from the [Releases](https://github.com/iamsanjid/buttontoaxisextra/releases) page.
* Extract the contents of the zip file to the UCR `Plugins` directory.
* Follow UCR's Interception installation instructions: https://github.com/snoothy/ucr/wiki/Core_Interception#installation-procedure
* Follow UCR's ViGEmu installation instructions: https://github.com/snoothy/ucr/wiki/Core_ViGEm#installation-procedure or follow the [`Core Providers` instructions](https://github.com/snoothy/ucr/wiki/Core-Providers) for your desired controller mapping.
* Restart your computer.
* Run `UCR_unblocker.exe`.
* Open UCR as an administrator.
  - Press the icon with +.

  <img width="265" height="97" alt="image" src="https://github.com/user-attachments/assets/4e3198de-e14e-482a-bdcb-bc1097ec4705" />

  - Put a profile name, and choose keyboard from input section and your virtual controller from the output section usually ViGEmu Xbox 360 Controller.

  <img width="770" height="483" alt="image" src="https://github.com/user-attachments/assets/ea76f209-97fc-4d11-861e-babdf67cc620" />

  - Under `Axis` section you should see these 4.

  <img width="308" height="264" alt="image" src="https://github.com/user-attachments/assets/32b48050-4112-4722-9fa2-cd0018328d1c" />

  - For example if you choose `Button to Axis (Curved Stepped)` which is usually better for more cases, just put a mapping name and you should see something like this:

  <img width="655" height="610" alt="image" src="https://github.com/user-attachments/assets/672cecf1-118e-4889-a1ee-74ad4262baeb" />

  - Just configure it and it should be ready to go, just press the play button and you should also tick/toggle on the `Block` option so that no other application can read the keyboard key.

## Plugins

### `ButtonToAxisSmooth`

Basic smoothing maps a button to an axis that ramps over-time linearly from 0% to user-provided percentages.

---

### `ButtonToAxisStepped`

Maps a single button to an axis that ramps through user-defined percentage waypoints, but the waypoint percentages are cycled each time the button is pressed.

---

### `ButtonToAxisCurved`
Maps a single button to an axis with a smooth ramp on press and release.

**Use cases:**
- Keyboard W → throttle/trigger in racing games
- Keyboard to flight stick axis for flight sims
- Any digital input that needs to feel analog

**Features:**
- Multiple curve modes (see below)
- Independent press and release curves
- Configurable ramp duration
- High-precision delta-time thread (no timer jitter)

**Settings:**

| Setting | Description |
|---|---|
| Axis on release (%) | Axis value when button is not held |
| Axis when pressed (%) | Axis value at full press |
| Ramp Duration (ms) | Total time to travel from release to pressed value |
| Curve Mode | Shape of the ramp (see [Curve Modes](#curve-modes)) |
| Curve Gamma | Exponent for Gamma/Skewed/Exponential modes |
| Release Curve Gamma | Independent curve exponent for release |

**TwoStage** — splits the ramp into a slow zone and a fast zone:

| Setting | Description | Default |
|---|---|---|
| `[TwoStage] Threshold (0-1)` | When the slow zone ends (as a fraction of ramp time). `0.4` = slow for first 40% of time | `0.4` |
| `[TwoStage] Ease Zone (0-1)` | Where on the axis the slow zone tops out. `0.6` = gentle phase only reaches 60% of target | `0.6` |

Example — `Threshold 0.5`, `EaseZone 0.5`:
```
first 50% of time  → covers first 50% of axis range (slow)
second 50% of time → covers remaining 50% of axis range (fast)
```

**SkewedS** — asymmetric S-curve that plateaus before reaching the target:

| Setting | Description | Default |
|---|---|---|
| `[SkewedS] Plateau Ceiling (%)` | The axis percentage where the curve flattens and crawls to the target. `0.75` = fast ramp to 75%, then very slow crawl to 100% | `0.75` |

Example — `Gamma 0.4`, `PlateauCeiling 0.75`:
```
0% → 75% of target  → fast skewed ramp
75% → 100% of target → very slow crawl (stays near 75% a long time)
```
Ideal for RWD throttle — the car naturally sits at 75% power and only creeps to full throttle.

---

### `ButtonToAxisCurvedStepped`
Maps a single button to an axis that ramps through user-defined percentage waypoints the longer you hold it — each waypoint has its own duration and uses a smooth curve.

**Use cases:**
- Throttle control with natural grip zones (e.g. 20% → 50% → 80% → 100%)
- Gradual brake pressure in racing games
- Thrust control in flight/space sims
- Any input where you want deliberate, staged power delivery

**Features:**
- Fully configurable step waypoints (target % and duration per step)
- Smooth curve applied within each segment
- Release mirrors the curve back down at a configurable speed multiplier
- Natural "notch" feel at each waypoint boundary
- High-precision delta-time thread

**Settings:**

| Setting | Description |
|---|---|
| Axis on release (%) | Axis value when fully released |
| Axis when pressed (%) | Axis value at 100% |
| Steps (target%:durationMs) | Comma-separated waypoints e.g. `20:300, 50:500, 80:700, 100:400` |
| Curve Mode | Shape of the ramp within each segment([Curve Modes](#curve-modes)) |
| Curve Gamma | Exponent for Gamma/Skewed/Exponential modes |
| Release Speed Multiplier | How much faster the axis drops on release (e.g. `2.0` = twice as fast) |

---

## Curve Modes

| Mode | Shape | Feel | Best For |
|---|---|---|---|
| **Smoothstep** | Slow → Fast → Slow | Natural S-curve, eases in and out | General purpose, throttle control |
| **Smootherstep** | Very Slow → Fast → Very Slow | More pronounced S-curve | High-power cars, precise braking |
| **Gamma < 1.0** | Fast → Slow | Rushes to target, crawls at end | Quick response inputs |
| **Gamma > 1.0** | Slow → Fast | Builds up then lunges | Late-hit feel, turbo spool |
| **Sine** | Gentle S | Organic, slightly faster initial response than Smoothstep | Flight sims, natural feel |
| **Skewed S** | Slow → Fast → Long plateau | Spends most time in the middle range | RWD grip cars, throttle limiting |
| **TwoStage** | Slow ramp → Fast ramp | Two distinct speeds, configurable split point | Precise low-end control with fast top-end |
| **Exponential** | Very Slow → Very Fast | Dramatic late surge | Dramatic power delivery |

## How it works

**Claude's explanantion on how the ButtonToAxisCurvedStepped plugin works** in-terms of Forza Horizon 6.

Disclaimer: These % doesn't directly map to in-game Throttle percentage. Some noticable mappings: 20% = ~7.13% in-game, 50% = ~43.7% in-game, 60% = ~60% in-game

Let's use `"20:300, 50:500"` and trace what the **curve actually does** within each segment.

Here: 20% means UCR will send 20% <RT>(Or whatever xbox 360 button you have mapped to throttle) to the game, and 300 is the duration of the segment in milliseconds. Which means it will go from 0% to 20% over 300ms following the specified curve mode, and then from 20% to 50% over 500ms same way.

---

### Without any curve (linear, for reference)
```
segment 0: 0% → 20% over 300ms
  t=0.0 → 0%
  t=0.5 → 10%   (exactly halfway)
  t=1.0 → 20%

segment 1: 20% → 50% over 500ms
  t=0.0 → 20%
  t=0.5 → 35%   (exactly halfway)
  t=1.0 → 50%
```

---

### Smoothstep `t*t*(3-2*t)`
Slow start, fast middle, slow end — **within each segment**:
```
segment 0: 0% → 20% over 300ms
  t=0.0  → curved=0.00 → 0%
  t=0.25 → curved=0.16 → 3.2%   (slow start)
  t=0.50 → curved=0.50 → 10%    (fastest here)
  t=0.75 → curved=0.84 → 16.8%  (slowing down)
  t=1.0  → curved=1.00 → 20%    (slow arrival)

segment 1: 20% → 50% over 500ms
  t=0.0  → curved=0.00 → 20%
  t=0.25 → curved=0.16 → 24.8%  (slow start again)
  t=0.50 → curved=0.50 → 35%
  t=0.75 → curved=0.84 → 45.2%
  t=1.0  → curved=1.00 → 50%
```
Each segment feels like its own smooth S — **eases into and out of every waypoint**.

---

### Gamma `t^0.5` (default)
Fast start, slow end — within each segment:
```
segment 0: 0% → 20% over 300ms
  t=0.0  → curved=0.00 → 0%
  t=0.25 → curved=0.50 → 10%   (already halfway at 25% time!)
  t=0.50 → curved=0.71 → 14.1%
  t=0.75 → curved=0.87 → 17.3%
  t=1.0  → curved=1.00 → 20%
```
Rushes to the step target quickly then crawls to it — **feels urgent**.

---

### Gamma `t^2.0`
Slow start, fast end — within each segment:
```
segment 0: 0% → 20% over 300ms
  t=0.0  → curved=0.00 → 0%
  t=0.25 → curved=0.06 → 1.2%   (barely moved)
  t=0.50 → curved=0.25 → 5%
  t=0.75 → curved=0.56 → 11.2%
  t=1.0  → curved=1.00 → 20%
```
Barely moves then lunges to the target — **feels like a late hit**.

---

### Sine
Similar to Smoothstep but slightly faster initial response:
```
segment 0: 0% → 20% over 300ms
  t=0.0  → curved=0.00 → 0%
  t=0.25 → curved=0.38 → 7.6%   (faster than smoothstep here)
  t=0.50 → curved=0.71 → 14.1%
  t=0.75 → curved=0.92 → 18.4%
  t=1.0  → curved=1.00 → 20%
```
Feels more **organic** — quick initial response but still gentle at the top.

---

### The Key Insight

The curve **resets at every segment boundary**. So no matter which curve you pick, each step target gets its own full S-curve or gamma ramp from `t=0` to `t=1`. This means:

```
Smoothstep with "20:300, 50:500, 80:700, 100:400":

hold → eases into 20%   (slow start in seg 0)
     → eases out of 20% (slow end of seg 0)
     → eases into 50%   (slow start of seg 1)  ← feels like a natural "notch"
     → eases out of 50%
     → eases into 80%   ← another notch
     ... and so on
```

That notch effect at each waypoint is actually **perfect for throttle control** — you naturally feel where each step is without any haptic feedback.
