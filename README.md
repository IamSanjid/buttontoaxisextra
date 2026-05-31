# UCR Button To Axis Extra Plugins

I use this mainly for Forza Horizon 6 RWD Car for better throttle control when using keyboard without traction control settings enabled. But you can use it for any game with any UCR supported controller.

## Installation
* Download UCR from the [URC Releases](https://github.com/snoothy/ucr/releases) page, extract the contents to your desired directory, you should see `Plugins` folder.
* Download the latest release from the [Releases](https://github.com/iamsanjid/buttontoaxisextra/releases) page.
* Extract the contents of the zip file to the UCR `Plugins` directory.
* Follow UCR's Interception installation instructions: https://github.com/snoothy/ucr/wiki/Core_Interception#installation-procedure
* Follow UCR's ViGEmu installation instructions: https://github.com/snoothy/ucr/wiki/Core_ViGEm#installation-procedure or follow the [`Core Providers` instructions](https://github.com/snoothy/ucr/wiki/Core-Providers) for your desired controller mapping.
* Restart your computer.
* Open UCR as an administrator.

## How it works

**Claude's explanantion on how the ButtonToAxisCurvedStepped plugin works** in-terms of Forza Horizon 6.

Disclaimer: These % doesn't directly map to in-game Throttle percentage. Some noticable mappings: 20% = ~7.13% in-game, 50% = ~43.7% in-game, 60% = ~60% in-game

Let's use `"20:300, 50:500"` and trace what the **curve actually does** within each segment.

Here: 20 is will send 20% <RT>(Or whatever xbox 360 button you have mapped to throttle) to the game, and 300 is the duration of the segment in milliseconds. Which means it will go from 0% to 20% over 300ms following the specified curve, and then from 20% to 50% over 500ms.

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
