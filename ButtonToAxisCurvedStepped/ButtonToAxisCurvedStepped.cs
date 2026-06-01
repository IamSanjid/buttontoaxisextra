using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using System;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
// 20:300, 50:300, 60:700, 80:1000, 100:200
// 20:500, 50:500, 60:400, 80:1000, 100:300
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
        ReverseSkewedS 
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

        [PluginGui("Steps (target%:durationMs, ...)", Order = 2)]
        public string StepDefinitions { get; set; } = "20:300, 50:500, 80:700, 100:400";

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

        [PluginGui("Release Curve Mode", Order = 8)]
        public SegmentCurveMode ReleaseMode { get; set; }

        [PluginGui("Release Curve Flip", Order = 9)]
        public bool ReleaseCurveFlip { get; set; } = false;

        [PluginGui("Release Curve Gamma", Order = 10)]
        public double ReleaseGamma { get; set; } = 0.5;

        [PluginGui("Release [TwoStage] Threshold (0-1)", Order = 11)]
        public double ReleaseTwoStageThreshold { get; set; } = 0.4;

        [PluginGui("Release [TwoStage] Ease Zone (0-1)", Order = 12)]
        public double ReleaseTwoStageEaseZone { get; set; } = 0.6;

        [PluginGui("Release Speed Multiplier", Order = 13)]
        public double ReleaseSpeed { get; set; } = 2.0;

        private struct Step
        {
            public double Target;   // e.g. 20.0 (percentage)
            public double Duration; // e.g. 300ms
        }


        private volatile bool _isRunning;
        private readonly object _lock = new object();
        private Thread _workerThread;

        private Step[] _steps;
        private int _currentSegment;
        private double _segmentProgress; // 0.0 -> 1.0 within current segment
        private bool _isPressed;
        private double _currentOutputPercentage; // Tracks our actual 0-100% output
        private bool _isReleasing;               // Are we currently in the fade-out phase?
        private double _releaseStartPercentage;  // The exact % we were at when you let go
        private double _releaseDurationMs;       // How long the fade-out should take
        private double _releaseElapsedMs;        // How far into the fade-out we are

        public ButtonToAxisCurvedStepped()
        {
            Mode = SegmentCurveMode.Smoothstep;
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
            WriteOutput(0, Functions.GetRangeFromPercentage(Range));

            WindowsLowLevel.DisablePowerThrottling();
            WindowsLowLevel.TimeBeginPeriod(1);
        }

        public override void OnDeactivate()
        {
            _isRunning = false;
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
        }

        public override void Update(params short[] values)
        {
            lock (_lock)
            {
                _isPressed = values[0] != 0;

                if (!_isRunning && _isPressed)
                {
                    _isRunning = true;
                    _workerThread = new Thread(ConverterLoop) { IsBackground = true };
                    _workerThread.Start();
                }
            }
        }

        private void ConverterLoop()
        {
            Stopwatch sw = Stopwatch.StartNew();

            // Burn the first iteration — delta after thread spawn is meaningless
            Thread.Sleep(1);
            long lastTicks = sw.ElapsedTicks;

            while (_isRunning)
            {
                long currentTicks = sw.ElapsedTicks;
                double dt = (currentTicks - lastTicks) / (double)Stopwatch.Frequency * 1000.0;
                lastTicks = currentTicks;

                lock (_lock)
                {
                    if (_isPressed)
                    {
                        // THE CATCH: If we were releasing, but you pressed the button again!
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
                    else
                    {
                        // THE SNAPSHOT: The exact millisecond you let go of the button
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

                            // Apply your Release Speed Multiplier to that time
                            _releaseDurationMs = timeToZero / ReleaseSpeed;
                            _releaseElapsedMs = 0.0;
                        }

                        // THE FADE: Draw a straight line to zero
                        _releaseElapsedMs += dt;

                        // Calculate 0.0 to 1.0 progress of the release fade
                        double releaseT = _releaseDurationMs > 0 ? (_releaseElapsedMs / _releaseDurationMs) : 1.0;
                        releaseT = Math.Min(1.0, releaseT);

                        // We still apply your ReleaseMode curve! 
                        // If it's Linear, it perfectly drops. If it's Smoothstep, it eases out.
                        double curvedReleaseT = ApplyCurve(releaseT, false);

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
                            return;
                        }
                    }

                    // Convert the 0-100% value into UCR's actual Axis Output (e.g., -100 to 100)
                    double finalAxisValue = Range + ((_currentOutputPercentage / 100.0) * (RangePressed - Range));
                    WriteOutput(0, Functions.GetRangeFromPercentage(finalAxisValue));
                }

                WindowsLowLevel.PreciseWait(8.0);
            }
        }

        private double GetCurrentOutput(bool isPressed)
        {
            // Always starts from 0%
            double segStart = _currentSegment == 0 ? 0.0 : _steps[_currentSegment - 1].Target;
            double segEnd = _steps[_currentSegment].Target;

            double curved = ApplyCurve(_segmentProgress, isPressed);
            double percentage = segStart + (curved * (segEnd - segStart));

            return Range + ((percentage / 100.0) * (RangePressed - Range));
        }

        private double ApplyCurve(double t, bool isPressed)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            double g = isPressed ? Gamma : ReleaseGamma;
            switch (isPressed ? Mode : ReleaseMode)
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
                    // This guarantees a slow start (slope 0) and a punchy finish (slope 1)
                    return (2.0 * skewed * skewed) - (skewed * skewed * skewed);
                    //double skewed = t / (t + Math.Pow(Math.Max(1e-9, 1.0 - t), 1.0 / Math.Max(0.01, g)));
                    //return skewed * skewed * (3.0 - 2.0 * skewed);
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

        private double CalculatePressPercentage()
        {
            double segStart = _currentSegment == 0 ? 0.0 : _steps[_currentSegment - 1].Target;
            double segEnd = _steps[_currentSegment].Target;
            double curved = ApplyCurve(_segmentProgress, true);
            return segStart + (curved * (segEnd - segStart));
        }

        private void MatchPercentageToSegments(double currentPercentage)
        {
            // This finds where the throttle % belongs in your steps so we can seamlessly resume
            _currentSegment = 0;
            _segmentProgress = 0.0;

            for (int i = 0; i < _steps.Length; i++)
            {
                double segStart = i == 0 ? 0.0 : _steps[i - 1].Target;
                double segEnd = _steps[i].Target;

                if (currentPercentage >= segStart && currentPercentage <= segEnd)
                {
                    _currentSegment = i;
                    double range = segEnd - segStart;

                    // Map our percentage linearly into the segment so the curve can take back over
                    if (range > 0)
                    {
                        _segmentProgress = (currentPercentage - segStart) / range;
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
                    return InputValidation.ValidateRange(value, 0.1, 5.0);
                case nameof(ReleaseSpeed):
                    return InputValidation.ValidateRange(value, 0.1, 10.0);
                case nameof(StepDefinitions):
                    try
                    {
                        var steps = StepDefinitions
                            .Replace(" ", "")
                            .Split(',')
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToArray();
                        if (steps.Length == 0)
                            return new PropertyValidationResult(false, "Need at least one step");
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
                        }
                        return PropertyValidationResult.ValidResult;
                    }
                    catch (Exception ex)
                    {
                        return new PropertyValidationResult(false, ex.Message);
                    }
            }
            return PropertyValidationResult.ValidResult;
        }
    }
}