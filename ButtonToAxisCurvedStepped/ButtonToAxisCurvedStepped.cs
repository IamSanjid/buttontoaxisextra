using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using System;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace HidWizards.UCR.Plugins.Remapper
{
    public enum SegmentCurveMode { Smoothstep, Smootherstep, Gamma, Sine, SkewedS, Exponential }

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

        [PluginGui("Curve Gamma", Order = 4)]
        public double Gamma { get; set; } = 0.5;

        [PluginGui("Release Speed Multiplier", Order = 5)]
        public double ReleaseSpeed { get; set; } = 2.0;

        private struct Step
        {
            public double Target;   // e.g. 20.0 (percentage)
            public double Duration; // e.g. 300ms
        }

        private Step[] _steps;
        private int _currentSegment;
        private double _segmentProgress; // 0.0 -> 1.0 within current segment
        private bool _isPressed;
        private volatile bool _isRunning;
        private readonly object _lock = new object();
        private Thread _workerThread;

        public ButtonToAxisCurvedStepped()
        {
            Mode = SegmentCurveMode.Smoothstep;
        }

        public override void OnActivate()
        {
            _currentSegment = 0;
            _segmentProgress = 0.0;
            _isPressed = false;
            _isRunning = false;
            ParseSteps();
            WriteOutput(0, Functions.GetRangeFromPercentage(Range));
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
                        double segDuration = _steps[_currentSegment].Duration;
                        _segmentProgress += dt / segDuration;

                        if (_segmentProgress >= 1.0)
                        {
                            // Reached end of this segment
                            if (_currentSegment < _steps.Length - 1)
                            {
                                // Advance to next segment, carry over excess progress
                                double excess = _segmentProgress - 1.0;
                                _currentSegment++;
                                // Rescale excess relative to new segment duration
                                _segmentProgress = excess * (_steps[_currentSegment - 1].Duration / _steps[_currentSegment].Duration);
                            }
                            else
                            {
                                // At last segment, clamp and hold
                                _segmentProgress = 1.0;
                            }
                        }
                    }
                    else
                    {
                        // Release — mirror back but ReleaseSpeed times faster
                        double segDuration = _steps[_currentSegment].Duration / ReleaseSpeed;
                        _segmentProgress -= dt / segDuration;

                        if (_segmentProgress <= 0.0)
                        {
                            if (_currentSegment > 0)
                            {
                                // Step back into previous segment from the top
                                double excess = -_segmentProgress;
                                _currentSegment--;
                                _segmentProgress = 1.0 - (excess * (_steps[_currentSegment + 1].Duration / _steps[_currentSegment].Duration));
                                _segmentProgress = Math.Max(0.0, _segmentProgress);
                            }
                            else
                            {
                                // Fully released back to start
                                _segmentProgress = 0.0;
                                _isRunning = false;
                                WriteOutput(0, Functions.GetRangeFromPercentage(Range));
                                break;
                            }
                        }
                    }

                    WriteOutput(0, Functions.GetRangeFromPercentage(GetCurrentOutput()));
                }

                Thread.Sleep(16);
            }
        }

        private double GetCurrentOutput()
        {
            double segStart = _currentSegment == 0 ? 0.0 : _steps[_currentSegment - 1].Target;
            double segEnd = _steps[_currentSegment].Target;

            double curved = ApplyCurve(_segmentProgress);
            double percentage = segStart + (curved * (segEnd - segStart));

            return Range + ((percentage / 100.0) * (RangePressed - Range));
        }

        private double ApplyCurve(double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            double g = Gamma;
            switch (Mode)
            {
                case SegmentCurveMode.Smoothstep:
                    return t * t * (3.0 - 2.0 * t);
                case SegmentCurveMode.Smootherstep:
                    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
                case SegmentCurveMode.Sine:
                    return Math.Sin(t * Math.PI / 2.0);
                case SegmentCurveMode.SkewedS:
                    double skewed = t / (t + Math.Pow(Math.Max(1e-9, 1.0 - t), 1.0 / Math.Max(0.01, g)));
                    return skewed * skewed * (3.0 - 2.0 * skewed);
                case SegmentCurveMode.Exponential:
                    return (Math.Exp(g * t) - 1.0) / (Math.Exp(g) - 1.0);
                default: // Gamma
                    return Math.Pow(t, g);
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