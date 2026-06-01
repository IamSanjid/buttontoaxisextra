# UCR Curved Axis Plugins

A set of [Universal Control Remapper (UCR)](https://github.com/Snoothy/UCR) plugins that map digital buttons to analog axis outputs with smooth, configurable curves — designed for games where analog precision matters but you only have a keyboard or digital buttons.

I use this mainly for Forza Horizon 6 RWD Car for better throttle control when using keyboard without traction control settings enabled. But you can use it for any game with any UCR supported controller.

## Requirements
.Net Framework Runtime 4.6.2 or above, get from [here](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net462).

## Installation

Demo + Setup for Forza Horizon 6: https://vimeo.com/1197117251?share=copy&fl=sv&fe=ci

* Download UCR from the [UCR Releases](https://github.com/snoothy/ucr/releases) page, extract the contents to your desired directory, you should see `Plugins` folder.
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

  - Just configure it and it should be ready to go, just press the play button and you should also tick/toggle on the `Block` option so that no other application can read the keyboard key. By default block won't be set, check `<path_to>\UCR_v0.9.0\Providers\Core_Interception\Settings.xml` and make sure `BlockingEnabled` value is `true`.
    ```xml
    <Name>BlockingEnabled</Name>
    <Value>true</Value>
    ```

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
```text
first 50% of time  → covers first 50% of axis range (slow)
second 50% of time → covers remaining 50% of axis range (fast)
```

**SkewedS** — asymmetric S-curve that plateaus before reaching the target:

| Setting | Description | Default |
| --- | --- | --- |
| `[SkewedS] Plateau Ceiling (%)` | The axis percentage where the curve flattens and crawls to the target. `0.75` = fast ramp to 75%, then very slow crawl to 100% | `0.75` |

Example — `Gamma 0.4`, `PlateauCeiling 0.75`:

```text
0% → 75% of target  → fast skewed ramp
75% → 100% of target → very slow crawl (stays near 75% a long time)
```

Ideal for RWD throttle — the car naturally sits at 75% power and only creeps to full throttle.

---

### `ButtonToAxisCurvedStepped`

Maps a single button to an axis that ramps through user-defined percentage waypoints the longer you hold it. When released, it can perfectly rewind, reverse your custom steps, or use an entirely separate set of release waypoints.

**Use cases:**

* Throttle control with natural grip zones (e.g., 20% → 50% → 80% → 100%)
* Gradual brake pressure in racing games
* Thrust control in flight/space sims
* Any input where you want deliberate, staged power delivery

**Features:**

* Fully configurable step waypoints (target % and duration per step)
* Independent release steps for precise deceleration control
* Dedicated curve shapes for both press and release phases
* Pre-computed LookUp Tables (LUTs) for zero-overhead, jitter-free performance
* High-precision delta-time worker thread

**Settings:**

| Setting | Description |
| --- | --- |
| Axis on release (%) | Axis value when fully released (usually -100 or 0) |
| Axis when pressed (%) | Axis value at 100% (usually 100) |
| Trigger Steps | Comma-separated waypoints e.g., `20:300, 50:300, 80:1000` (`target%:durationMs`) |
| Curve Mode | Shape of the ramp within each trigger segment (see [Curve Modes](#curve-modes)) |
| Curve Flip | Inverts the shape of the trigger curve |
| Curve Gamma | Exponent for Gamma/Skewed/Exponential modes on trigger |
| Release Steps | Custom waypoints for when you let go of the button e.g., `80:300, 60:1000, 0:200` |
| Simple Rewind | Ignores release steps entirely. Simply reverses time back to 0% smoothly. |
| Reverse Trigger Steps | Ignores release steps. Plays your `Trigger Steps` sequence backwards. |
| Release Curve Mode | Shape of the ramp within each release segment |
| Release Curve Flip | Inverts the shape of the release curve |
| Release Speed Multiplier | Globally speeds up or slows down the release phase (e.g., `2.0` = twice as fast) |

*(Note: TwoStage parameters are also available independently for both Press and Release phases).*

---

## Curve Modes

| Mode | Shape | Feel | Best For |
| --- | --- | --- | --- |
| **Smoothstep** | Slow → Fast → Slow | Natural S-curve, eases in and out | General purpose, throttle control |
| **Smootherstep** | Very Slow → Fast → Very Slow | More pronounced S-curve | High-power cars, precise braking |
| **Gamma < 1.0** | Fast → Slow | Rushes to target, crawls at end | Quick response inputs |
| **Gamma > 1.0** | Slow → Fast | Builds up then lunges | Late-hit feel, turbo spool |
| **Sine** | Gentle S | Organic, slightly faster initial response than Smoothstep | Flight sims, natural feel |
| **Skewed S** | Slow → Fast → Long plateau | Spends most time in the middle range | RWD grip cars, throttle limiting |
| **TwoStage** | Slow ramp → Fast ramp | Two distinct speeds, configurable split point | Precise low-end control with fast top-end |
| **Exponential** | Very Slow → Very Fast | Dramatic late surge | Dramatic power delivery |

---

## How the Step Logic Works

Disclaimer: These percentages don't directly map to in-game Throttle percentage. Some noticeable mappings in Forza: 20% = ~7.13% in-game, 50% = ~43.7% in-game, 60% = ~60% in-game.

### Pressing the Button (Trigger Steps)

Let's use `"20:300, 50:500"` and trace what the **curve actually does** within each segment.

Here: `20%` means UCR will send 20% of your axis to the game, and `300` is the duration of the segment in milliseconds. It will go from 0% to 20% over 300ms following the specified curve mode, and then from 20% to 50% over 500ms.

**Smoothstep Example `t*t*(3-2*t)`** (Slow start, fast middle, slow end within each segment):

```text
segment 0: 0% → 20% over 300ms
  t=0.0  → 0%
  t=0.25 → 3.2%   (slow start)
  t=0.50 → 10%    (fastest here)
  t=0.75 → 16.8%  (slowing down)
  t=1.0  → 20%    (slow arrival)

segment 1: 20% → 50% over 500ms
  t=0.0  → 20%
  t=0.25 → 24.8%  (slow start again)
  t=0.50 → 35%
  t=0.75 → 45.2%
  t=1.0  → 50%
```

**The Key Insight:** The curve **resets at every segment boundary**. This creates a natural "notch" effect at each waypoint. It eases out of the previous step and eases into the next one, which is perfect for throttle control — you naturally feel where each step is without any haptic feedback.

### Releasing the Button (Release Steps Explained)

If you don't use `Simple Rewind` or `Reverse Trigger Steps`, you can define a custom path down to 0% when you let go of the key.

Let's look at this release step sequence: `80:100, 60:500, 50:200, 20:150, 0:50`

The logic works exactly like the trigger steps, but downwards. The duration component (`:500`) represents the **time it takes to arrive at that target from the preceding state**.

| Target | Duration | What actually happens in-game |
| --- | --- | --- |
| **80%** | `100ms` | Drops rapidly from 100% down to 80% in just 100ms. |
| **60%** | `500ms` | Decelerates from 80% to 60% over a long 500ms. |
| **50%** | `200ms` | Drops from 60% to 50% taking 200ms. |
| **20%** | `150ms` | Drops from 50% to 20% taking 150ms. |
| **0%** | `50ms` | Snaps from 20% to 0% in 50ms (full let-off). |

*Note: If you let go of the button early (e.g., at 75%), the plugin smartly finds the correct segment (between 80% and 60%) and resumes the downward curve seamlessly from there.*

---

## Tips for Sim Racing

* Sometimes **`SkewedS`** for Release Curves might work better than **`Linear`** for Release Curves. When driving powerful RWD cars, snapping completely off the throttle can cause lift-off oversteer (spinning out). Setting your `Release Curve Mode` to `SkewedS` allows the throttle to drop heavily at the very beginning (losing engine power so you make the corner), but then artificially *plateaus and smooths out* near the bottom, acting like a natural engine-brake buffer to keep the rear wheels stable.
* **Release Steps vs. Rewind:** If your goal is just basic smoothing, turn on `Simple Rewind (Ignore Release Steps)`. However, if you are driving a car with massive turbo lag, use custom `Release Steps`. You can set a custom release profile that drops throttle slowly in the mid-range to keep the turbo spooled through corners.
* **Curve Flip:** If a curve feels completely backward (e.g., it crawls at the beginning but lunges at the end, and you want the opposite), just toggle the `Curve Flip` or `Release Curve Flip` checkbox. It mathematically inverts the easing shape while keeping your durations perfectly intact.
