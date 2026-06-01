using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
// 20:300, 50:300, 60:700, 80:1000, 100:200
// 20:200, 50:300, 60:400, 80:1000, 100:300
namespace HidWizards.UCR.Plugins.Remapper
{
    public enum SegmentCurveMode
    {
        Smoothstep,
        Smootherstep,
        Gamma,
        Sine,
        SkewedS,
        Exponential,
        Linear,
        TwoStage,
    }

    [Plugin("Button to Axis (Stepped Curve)", Group = "Axis", Description = "Hold a button to ramp through stepped axis values using a curve")]
    [PluginInput(DeviceBindingCategory.Momentary, "Button")]
    [PluginOutput(DeviceBindingCategory.Range, "Axis", Group = "Axis")]
    public class ButtonToAxisCurvedStepped : Plugin
    {
        [PluginGui("Axis on release (%)", Order = 0, Group = "Axis")]
        public double Range { get; set; } = -100;

        [PluginGui("Axis when pressed (%)", Order = 1, Group = "Axis")]
        public double RangePressed { get; set; } = 100;

        [PluginGui("Trigger Steps (target%:durationMs, ...)", Order = 2)]
        public string StepDefinitions { get; set; } = "20:300, 50:300, 60:700, 80:1000, 100:200";

        [PluginGui("Curve Mode", Order = 3)]
        public SegmentCurveMode Mode { get; set; }

        [PluginGui("Curve Flip", Order = 4)]
        public bool CurveFlip { get; set; } = false;

        [PluginGui("Curve Gamma", Order = 5)]
        public double Gamma { get; set; } = 0.5;

        [PluginGui("[TwoStage] Threshold (0-1)", Order = 6)]
        public double TwoStageThreshold { get; set; } = 0.4;

        [PluginGui("[TwoStage] Ease Zone (0-1)", Order = 7)]
        public double TwoStageEaseZone { get; set; } = 0.6;

        [PluginGui("Release Steps (target%:durationMs, ...)", Order = 8)]
        public string ReleaseStepDefinitions { get; set; } = "80:100, 60:500, 50:200, 20:150, 0:50";

        [PluginGui("Simple Rewind (Ignore Release Steps)", Order = 9)]
        public bool SimpleRewindRelease { get; set; } = false;

        [PluginGui("Reverse Trigger Steps (Ignore Release Steps)", Order = 10)]
        public bool ReversedRelease { get; set; } = false;

        [PluginGui("Release Curve Mode", Order = 11)]
        public SegmentCurveMode ReleaseMode { get; set; }

        [PluginGui("Release Curve Flip", Order = 12)]
        public bool ReleaseCurveFlip { get; set; } = false;

        [PluginGui("Release Curve Gamma", Order = 13)]
        public double ReleaseGamma { get; set; } = 0.5;

        [PluginGui("Release [TwoStage] Threshold (0-1)", Order = 14)]
        public double ReleaseTwoStageThreshold { get; set; } = 0.4;

        [PluginGui("Release [TwoStage] Ease Zone (0-1)", Order = 15)]
        public double ReleaseTwoStageEaseZone { get; set; } = 0.6;

        [PluginGui("Release Speed Multiplier", Order = 16)]
        public double ReleaseSpeed { get; set; } = 2.0;

        private struct Step
        {
            public double Target;   // e.g. 20.0 (percentage)
            public double Duration; // e.g. 300ms
        }

        private Step[] _steps;
        private Step[] _releaseSteps;
        private int _currentSegment;
        private int _currentReleaseSegment;
        private double _segmentProgress; // 0.0 -> 1.0 within current segment
        private double _currentOutputPercentage; // Tracks our actual 0-100% output
        private bool _isReleasing;               // Are we currently in the fade-out phase?
        private double _releaseStartPercentage;  // The exact % we were at when you let go
        private double _releaseDurationMs;       // How long the fade-out should take
        private double _releaseElapsedMs;        // How far into the fade-out we are

        private volatile bool _isPressed;

        private readonly AutoResetEvent _wakeEvent = new AutoResetEvent(false);
        private volatile bool _pluginActive;
        private volatile bool _isRunning;
        private readonly object _lock = new object();
        private Thread _workerThread;

        public ButtonToAxisCurvedStepped()
        {
            Mode = SegmentCurveMode.Linear;
            ReleaseMode = SegmentCurveMode.Linear;
        }

        public override void OnActivate()
        {
            _currentSegment = 0;
            _segmentProgress = 0.0;
            _isPressed = false;
            _isRunning = false;
            _isReleasing = false;
            _currentOutputPercentage = 0.0;
            ParseSteps();
            BuildLookupTables();

            _pluginActive = true;
            _workerThread = new Thread(PersistentWorkerLoop) { IsBackground = true };
            _workerThread.Start();

            WriteOutput(0, Functions.GetRangeFromPercentage(Range));

            WindowsLowLevel.DisablePowerThrottling();
            WindowsLowLevel.TimeBeginPeriod(1);
        }

        public override void OnDeactivate()
        {
            _pluginActive = false;
            _wakeEvent.Set(); // Wake thread to let it die
            _workerThread?.Join(200);
        }

        private void ParseSteps()
        {
            _steps = StepDefinitions
                .Replace(" ", "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s =>
                {
                    var parts = s.Split(':');
                    return new Step
                    {
                        Target = double.Parse(parts[0]),
                        Duration = double.Parse(parts[1])
                    };
                })
                .OrderBy(step => step.Target)
                .ToArray();

            if (ReversedRelease)
            {
                int len = _steps.Length;
                _releaseSteps = new Step[len];

                for (int i = 0; i < len; i++)
                {
                    // Step backwards through the trigger array
                    int pressIndex = len - 1 - i;

                    _releaseSteps[i] = new Step
                    {
                        // The target on release is the START of the trigger segment 
                        // (or 0.0 if we are on the very last release segment)
                        Target = (pressIndex == 0) ? 0.0 : _steps[pressIndex - 1].Target,

                        // Keep the exact duration paired with this physical interval
                        Duration = _steps[pressIndex].Duration
                    };
                }

                SimpleRewindRelease = false;
            }
            else if (!SimpleRewindRelease)
            {
                _releaseSteps = ReleaseStepDefinitions
                    .Replace(" ", "")
                    .Split(',')
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s =>
                    {
                        var parts = s.Split(':');
                        return new Step
                        {
                            Target = double.Parse(parts[0]),
                            Duration = double.Parse(parts[1])
                        };
                    })
                    .OrderByDescending(step => step.Target)
                    .ToArray();
            }

        }

        bool lastIsPressed = false;
        public override void Update(params short[] values)
        {
            lock (_lock)
            {
                _isPressed = values[0] != 0;

                if (lastIsPressed != _isPressed)
                {
                    Logger.Info("IsPressed changed: " + _isPressed);
                    lastIsPressed = _isPressed;
                }

                if (!_isRunning && _isPressed)
                {
                    _isRunning = true;
                    _wakeEvent.Set(); // Instantly unblocks the worker thread
                }
            }
        }

        private void PersistentWorkerLoop()
        {
            while (_pluginActive)
            {
                _wakeEvent.WaitOne(); // Zero-CPU sleep until a button is pressed

                if (!_pluginActive) break;

                // Since we can't generic-dispatch easily from a persistent loop without allocations,
                // you can branch inside the loop, or keep the generics by having the loop 
                // read the configuration flag once it wakes up.
                if (SimpleRewindRelease)
                    ConverterLoop(new SimpleRewindReleaseHandler());
                else
                    ConverterLoop(new NormalReleaseHandler());
            }
        }

        private interface IReleaseHandler
        {
            bool Handle(ButtonToAxisCurvedStepped instance, double dt);
        }

        private struct NormalReleaseHandler : IReleaseHandler
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Handle(ButtonToAxisCurvedStepped instance, double dt)
                => instance.HandleReleaseState(dt);
        }

        private struct SimpleRewindReleaseHandler : IReleaseHandler
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Handle(ButtonToAxisCurvedStepped instance, double dt)
                => instance.HandleSimpleRewindReleaseState(dt);
        }

        private void ConverterLoop<TRelease>(TRelease releaseHandler)
            where TRelease : struct, IReleaseHandler
        {
            Stopwatch sw = Stopwatch.StartNew();
            Thread.Sleep(1);
            long lastTicks = sw.ElapsedTicks;
            var axisDelta = RangePressed - Range;

            while (_isRunning)
            {
                long currentTicks = sw.ElapsedTicks;
                double dt = (currentTicks - lastTicks) / (double)Stopwatch.Frequency * 1000.0;
                lastTicks = currentTicks;

                bool localIsPressed = _isPressed;

                bool shouldContinue;
                if (localIsPressed)
                {
                    HandlePressedState(dt);
                    shouldContinue = true;
                }
                else
                {
                    // JIT devirtualizes this — no overhead
                    shouldContinue = releaseHandler.Handle(this, dt);
                }
                if (!shouldContinue) return;

                // Convert the 0-100% value into UCR's actual Axis Output (e.g., -100 to 100)
                double finalAxisValue = Range + ((_currentOutputPercentage / 100.0) * axisDelta);
                WriteOutput(0, Functions.GetRangeFromPercentage(finalAxisValue));

                WindowsLowLevel.PreciseWait(8.0);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandlePressedState(double dt)
        {
            // THE CATCH: If we were releasing, but user pressed the button again!
            if (_isReleasing)
            {
                _isReleasing = false;
                MatchPercentageToSegments(_currentOutputPercentage);
            }

            // NORMAL PRESS PROGRESSION
            double segDuration = _steps[_currentSegment].Duration;
            _segmentProgress += dt / segDuration;

            if (_segmentProgress >= 1.0)
            {
                if (_currentSegment < _steps.Length - 1)
                {
                    double excess = _segmentProgress - 1.0;
                    _currentSegment++;
                    _segmentProgress = excess * (_steps[_currentSegment - 1].Duration / _steps[_currentSegment].Duration);
                }
                else
                {
                    _segmentProgress = 1.0;
                }
            }

            // Calculate where we are on the curve
            _currentOutputPercentage = CalculatePressPercentage();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HandleSimpleRewindReleaseState(double dt)
        {
            // THE SNAPSHOT: The exact millisecond user let go of the button
            if (!_isReleasing)
            {
                _isReleasing = true;
                _releaseStartPercentage = _currentOutputPercentage;

                // Calculate how much total time it WOULD have taken to rewind to 0 normally
                double timeToZero = _segmentProgress * _steps[_currentSegment].Duration;
                for (int i = 0; i < _currentSegment; i++)
                {
                    timeToZero += _steps[i].Duration;
                }

                _releaseDurationMs = timeToZero / ReleaseSpeed;
                _releaseElapsedMs = 0.0;
            }

            // THE FADE: Draw a straight line to zero
            _releaseElapsedMs += dt;

            // Calculate 0.0 to 1.0 progress of the release fade
            double releaseT = _releaseDurationMs > 0 ? (_releaseElapsedMs / _releaseDurationMs) : 1.0;
            releaseT = Math.Min(1.0, releaseT);

            // We still apply the ReleaseMode curve! 
            // If it's Linear, it perfectly drops. If it's Smoothstep, it eases out and so on.
            double curvedReleaseT = ApplyCurveFast(releaseT, false);

            // Lerp from the snapshot value down to 0
            _currentOutputPercentage = _releaseStartPercentage * (1.0 - curvedReleaseT);

            // Turn off the thread once we hit zero
            if (releaseT >= 1.0)
            {
                _currentOutputPercentage = 0.0;
                _currentSegment = 0;
                _segmentProgress = 0.0;
                _isReleasing = false;
                _isRunning = false;
                _isPressed = false;

                WriteOutput(0, Functions.GetRangeFromPercentage(Range));
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HandleReleaseState(double dt)
        {
            // THE SNAPSHOT: The exact millisecond user let go of the button
            if (!_isReleasing)
            {
                _isReleasing = true;
                MatchPercentageToReleaseSegments(_currentOutputPercentage);
            }

            // RELEASE PROGRESSION
            double segDuration = _releaseSteps[_currentReleaseSegment].Duration;

            _segmentProgress += (dt * ReleaseSpeed) / segDuration;

            if (_segmentProgress >= 1.0)
            {
                if (_currentReleaseSegment < _releaseSteps.Length - 1)
                {
                    double excess = _segmentProgress - 1.0;
                    _currentReleaseSegment++;

                    // Carry over excess time to the next segment
                    _segmentProgress = excess * (_releaseSteps[_currentReleaseSegment - 1].Duration / _releaseSteps[_currentReleaseSegment].Duration);
                }
                else
                {
                    // End of the release sequence
                    _segmentProgress = 1.0;
                    _currentOutputPercentage = 0.0; // Ensure we perfectly hit 0
                    _currentSegment = 0;
                    _currentReleaseSegment = 0;
                    _isReleasing = false;
                    _isRunning = false;
                    _isPressed = false;

                    WriteOutput(0, Functions.GetRangeFromPercentage(Range));
                    return false;
                }
            }

            _currentOutputPercentage = CalculateReleasePercentage();

            return true;
        }

        private double GetCurrentOutput(bool isPressed)
        {
            // Always starts from 0%
            double segStart = _currentSegment == 0 ? 0.0 : _steps[_currentSegment - 1].Target;
            double segEnd = _steps[_currentSegment].Target;

            double curved = ApplyCurveFast(_segmentProgress, isPressed);
            double percentage = segStart + (curved * (segEnd - segStart));

            return Range + ((percentage / 100.0) * (RangePressed - Range));
        }

        // for history
        private double ApplyCurve(double t, bool isPressed)
        {
            // Clamp t to ensure we never calculate outside bounds
            t = Math.Max(0.0, Math.Min(1.0, t));

            bool flip = isPressed ? CurveFlip : ReleaseCurveFlip;

            if (flip)
            {
                // 1. Invert the input (1.0 - t)
                // 2. Evaluate the curve
                // 3. Invert the output (1.0 - Result)
                // This flips the easing shape but keeps the 0.0 -> 1.0 physical direction
                return 1.0 - EvaluateCurveShape(1.0 - t, isPressed);
            }
            else
            {
                // Normal curve behavior
                return EvaluateCurveShape(t, isPressed);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double EvaluateCurveShape(double t, bool isPressed)
        {
            double g = isPressed ? Gamma : ReleaseGamma;
            SegmentCurveMode mode = isPressed ? Mode : ReleaseMode;

            switch (mode)
            {
                case SegmentCurveMode.Smoothstep:
                    return t * t * (3.0 - 2.0 * t);
                case SegmentCurveMode.Smootherstep:
                    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
                case SegmentCurveMode.Gamma:
                    return Math.Pow(t, g);
                case SegmentCurveMode.Sine:
                    return Math.Sin(t * Math.PI / 2.0);
                case SegmentCurveMode.SkewedS:
                    double denom = Math.Max(1e-9, 1.0 - t);
                    double p = 1.0 / Math.Max(0.01, g);
                    double skewed = t / (t + Math.Pow(denom, p));
                    return (3.0 * skewed * skewed) - (2.0 * skewed * skewed * skewed);
                case SegmentCurveMode.Exponential:
                    return (Math.Exp(g * t) - 1.0) / (Math.Exp(g) - 1.0);
                case SegmentCurveMode.Linear:
                    return t;
                case SegmentCurveMode.TwoStage:
                    var threshold = isPressed ? TwoStageThreshold : ReleaseTwoStageThreshold;
                    var easeZone = isPressed ? TwoStageEaseZone : ReleaseTwoStageEaseZone;
                    return t < threshold
                        ? (t / threshold) * easeZone
                        : easeZone + ((t - threshold) / (1.0 - threshold)) * (1.0 - easeZone);
                default:
                    throw new InvalidOperationException("Unreachable");
            }
        }

        private const int LUT_RESOLUTION = 4096;
        private double[] _pressCurveLUT;
        private double[] _releaseCurveLUT;

        private void BuildLookupTables()
        {
            _pressCurveLUT = new double[LUT_RESOLUTION];
            _releaseCurveLUT = new double[LUT_RESOLUTION];

            for (int i = 0; i < LUT_RESOLUTION; i++)
            {
                double t = i / (double)(LUT_RESOLUTION - 1);

                double pressVal = EvaluateCurveShape(t, true);
                _pressCurveLUT[i] = CurveFlip
                    ? 1.0 - EvaluateCurveShape(1.0 - t, true)
                    : pressVal;

                double releaseVal = EvaluateCurveShape(t, false);
                _releaseCurveLUT[i] = ReleaseCurveFlip
                    ? 1.0 - EvaluateCurveShape(1.0 - t, false)
                    : releaseVal;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double ApplyCurveFast(double t, bool isPressed)
        {
            // Fast inline clamping (often compiles down better than Math.Max/Min method calls)
            t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);

            // Convert 0.0-1.0 into an integer index 0-4095
            double pos = t * (LUT_RESOLUTION - 1);
            int index = (int)pos;
            double frac = pos - index;

            // Clamp index so index+1 never goes out of bounds
            // When index == 4095, frac == 0.0 anyway so lerp result is identical
            int indexNext = index < LUT_RESOLUTION - 1 ? index + 1 : index;

            double[] lut = isPressed ? _pressCurveLUT : _releaseCurveLUT;
            return lut[index] + frac * (lut[indexNext] - lut[index]);
        }

        private double CalculatePressPercentage()
        {
            double segStart = _currentSegment == 0 ? 0.0 : _steps[_currentSegment - 1].Target;
            double segEnd = _steps[_currentSegment].Target;
            double curved = ApplyCurveFast(_segmentProgress, true);
            return segStart + (curved * (segEnd - segStart));
        }

        private void MatchPercentageToSegments(double currentPercentage)
        {
            // This finds where the trigger % belongs in our steps so we can seamlessly resume
            _currentSegment = 0;
            _segmentProgress = 0.0;

            for (int i = _steps.Length - 1; i >= 0; i--)
            {
                double segStart = i == 0 ? 0.0 : _steps[i - 1].Target;
                double segEnd = _steps[i].Target;

                // Because we are moving backwards through an ascending array,
                // the first segStart we are >= to is our bucket.
                if (currentPercentage >= segStart)
                {
                    _currentSegment = i;
                    double range = segEnd - segStart;

                    if (range > 0)
                    {
                        _segmentProgress = (currentPercentage - segStart) / range;
                    }
                    break;
                }
            }
        }

        private double CalculateReleasePercentage()
        {
            // On release, we start at 100% and move down toward 0%
            double segStart = _currentReleaseSegment == 0 ? 100.0 : _releaseSteps[_currentReleaseSegment - 1].Target;
            double segEnd = _releaseSteps[_currentReleaseSegment].Target;

            double curved = ApplyCurveFast(_segmentProgress, false);

            // Lerp downwards
            return segStart + (curved * (segEnd - segStart));
        }

        private void MatchPercentageToReleaseSegments(double currentPercentage)
        {
            _currentReleaseSegment = 0;
            _segmentProgress = 0.0;

            for (int i = 0; i < _releaseSteps.Length; i++)
            {
                double segStart = i == 0 ? 100.0 : _releaseSteps[i - 1].Target;
                double segEnd = _releaseSteps[i].Target;

                if (currentPercentage >= segEnd)
                {
                    _currentReleaseSegment = i;
                    double range = segStart - segEnd;

                    if (range > 0)
                    {
                        _segmentProgress = (segStart - currentPercentage) / range;
                    }
                    break;
                }
            }
        }

        public override PropertyValidationResult Validate(PropertyInfo propertyInfo, dynamic value)
        {
            switch (propertyInfo.Name)
            {
                case nameof(Range):
                case nameof(RangePressed):
                    return InputValidation.ValidateRange(value, -100.0, 100.0);
                case nameof(Gamma):
                case nameof(ReleaseGamma):
                    return InputValidation.ValidateRange(value, 0.1, 5.0);
                case nameof(ReleaseSpeed):
                    return InputValidation.ValidateRange(value, 0.1, 10.0);
                case nameof(StepDefinitions):
                case nameof(ReleaseStepDefinitions):
                    return VerifySteps((string)value);
            }
            return PropertyValidationResult.ValidResult;
        }

        public PropertyValidationResult VerifySteps(string value)
        {
            try
            {
                var steps = value
                    .Replace(" ", "")
                    .Split(',')
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
                if (steps.Length == 0)
                    return new PropertyValidationResult(false, "Need at least one step");
                var foundZero = false;
                var foundHundred = false;
                foreach (var step in steps)
                {
                    var parts = step.Split(':');
                    if (parts.Length != 2)
                        return new PropertyValidationResult(false, $"Invalid step format '{step}', use target%:durationMs");
                    var target = double.Parse(parts[0]);
                    var duration = double.Parse(parts[1]);
                    var targetResult = InputValidation.ValidateRange(target, 0.0, 100.0);
                    if (!targetResult.IsValid) return targetResult;
                    var durationResult = InputValidation.ValidateRange(duration, 10.0, 10000.0);
                    if (!durationResult.IsValid) return durationResult;

                    foundZero = foundZero || target <= 0.0;
                    foundHundred = foundHundred || target >= 100.0;
                }

                if (!foundZero && !foundHundred) return new PropertyValidationResult(false, "Invalid steps, either need to end with 0% or 100%");

                return PropertyValidationResult.ValidResult;
            }
            catch (Exception ex)
            {
                return new PropertyValidationResult(false, ex.Message);
            }
        }
    }
}