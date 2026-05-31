using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace HidWizards.UCR.Plugins.Remapper
{
    [Plugin("Button to Axis (Curved)", Group = "Axis", Description = "Ramps a button to an axis value using a custom curve")]
    [PluginInput(DeviceBindingCategory.Momentary, "Button")]
    [PluginOutput(DeviceBindingCategory.Range, "Axis", Group = "Axis")]
    public class ButtonToAxisCurved : Plugin
    {
        [PluginGui("Axis on release (%)", Order = 0, Group = "Axis")]
        public double Range { get; set; }

        [PluginGui("Axis when pressed (%)", Order = 1, Group = "Axis")]
        public double RangePressed { get; set; }

        [PluginGui("Ramp Duration (ms)", Order = 2)]
        public double RampDuration { get; set; }

        [PluginGui("Curve Exponent (Gamma)", Order = 3)]
        public double Gamma { get; set; }

        [PluginGui("Release Curve (Gamma)", Order = 4)]
        public double ReleaseGamma { get; set; }

        [PluginGui("Curve Mode", Order = 5)]
        public CurveMode Mode { get; set; }

        [PluginGui("[TwoStage] Threshold (0-1)", Order = 6)]
        public double TwoStageThreshold { get; set; }

        [PluginGui("[TwoStage] Ease Zone (0-1)", Order = 7)]
        public double TwoStageEaseZone { get; set; }

        [PluginGui("[SkewsS] Plateau Ceiling (%)", Order = 8)]
        public double PlateauCeiling { get; set; }

        private double _timeProgress; // Ranges cleanly from 0.0 to 1.0
        private bool _isPressed;
        private volatile bool _isAnimating;
        private readonly object _lock = new object();
        private Thread _workerThread;

        public ButtonToAxisCurved()
        {
            Range = -100;
            RangePressed = 100;
            RampDuration = 400; // 400ms total travel time
            Gamma = 0.5;        // Starts fast, flattens out at the end
            ReleaseGamma = 1.0; // Linear release by default
            Mode = CurveMode.Gamma;
            TwoStageThreshold = 0.4; // First 40% of time is the slow zone
            TwoStageEaseZone = 0.6;  // Slow zone covers first 60% of axis travel
            PlateauCeiling = 0.75;
        }

        public override void OnActivate()
        {
            _timeProgress = 0.0;
            _isAnimating = false;
            WriteOutput(0, Functions.GetRangeFromPercentage(Range));
        }

        public override void OnDeactivate()
        {
            _isAnimating = false;
            _workerThread?.Join(200);
        }

        public override void Update(params short[] values)
        {
            lock (_lock)
            {
                _isPressed = values[0] != 0;

                // If the thread isn't running and we have movement to do, spawn it
                if (!_isAnimating)
                {
                    _isAnimating = true;
                    _workerThread = new Thread(ConverterLoop) { IsBackground = true };
                    _workerThread.Start();
                }
            }
        }

        // The high-precision background thread
        private void ConverterLoop()
        {
            Stopwatch sw = Stopwatch.StartNew();

            // Burn the first iteration — delta after thread spawn is meaningless
            Thread.Sleep(1);
            long lastTicks = sw.ElapsedTicks;

            while (_isAnimating)
            {
                // Calculate precise Delta Time in milliseconds
                long currentTicks = sw.ElapsedTicks;
                double dt = (currentTicks - lastTicks) / (double)Stopwatch.Frequency * 1000.0;
                lastTicks = currentTicks;

                bool shouldStop = false;

                lock (_lock)
                {
                    double step = dt / RampDuration;

                    if (_isPressed)
                    {
                        _timeProgress += step;
                        if (_timeProgress >= 1.0)
                        {
                            _timeProgress = 1.0;
                            shouldStop = true; // Reached max, we can sleep the thread
                        }
                    }
                    else
                    {
                        _timeProgress -= step * 2.0; // Release drops twice as fast
                        if (_timeProgress <= 0.0)
                        {
                            _timeProgress = 0.0;
                            shouldStop = true; // Returned to 0%, we can sleep the thread
                        }
                    }

                    double curvedProgress = Math.Max(0.0, Math.Min(1.0, ApplyCurve(_timeProgress, _isPressed)));
                    double currentPercentage = Range + (curvedProgress * (RangePressed - Range));

                    WriteOutput(0, Functions.GetRangeFromPercentage(currentPercentage));

                    if (shouldStop)
                    {
                        _isPressed = false;
                        _isAnimating = false;
                        break; // Exit the while loop, terminating the thread
                    }
                }

                // Yield to OS so we don't melt a CPU core. 
                // Because we use delta-time, sleep inaccuracy (1-15ms) won't break the math!
                Thread.Sleep(1);
            }
        }

        private double ApplyCurve(double t, bool pressed)
        {
            double g = pressed ? Gamma : ReleaseGamma;
            switch (Mode)
            {
                case CurveMode.Smoothstep:
                    return t * t * (3.0 - 2.0 * t); // Ignores gamma, self-contained
                case CurveMode.TwoStage:
                    return t < TwoStageThreshold
                        ? (t / TwoStageThreshold) * TwoStageEaseZone
                        : TwoStageEaseZone + ((t - TwoStageThreshold) / (1.0 - TwoStageThreshold)) * (1.0 - TwoStageEaseZone);
                case CurveMode.GammaExponential:
                    return (Math.Exp(g * t) - 1.0) / (Math.Exp(g) - 1.0);
                case CurveMode.SkewedS:
                    double skewed = t / (t + Math.Pow(1.0 - t, 1.0 / g));
                    double sCurve = skewed * skewed * (3.0 - 2.0 * skewed);

                    // Compress the fast part into 0 -> PlateauCeiling
                    // Then crawl the remainder very slowly to 1.0
                    if (sCurve < PlateauCeiling)
                    {
                        // Normal range, just rescaled to hit ceiling
                        return sCurve;
                    }
                    else
                    {
                        // Above ceiling — extremely slow crawl to 100%
                        double above = (sCurve - PlateauCeiling) / (1.0 - PlateauCeiling);
                        return PlateauCeiling + (above * above * (1.0 - PlateauCeiling));
                    }
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
                case nameof(RampDuration):
                    return InputValidation.ValidateRange(value, 10.0, 5000.0);
                case nameof(Gamma):
                    return InputValidation.ValidateRange(value, 0.1, 5.0);
                case nameof(ReleaseGamma):
                    return InputValidation.ValidateRange(value, 0.1, 5.0);
                case nameof(TwoStageThreshold):
                case nameof(TwoStageEaseZone):
                    return InputValidation.ValidateRange(value, 0.01, 0.99);
                case nameof(PlateauCeiling):
                    return InputValidation.ValidateRange(value, 0.1, 0.99);
            }
            return PropertyValidationResult.ValidResult;
        }
    }
}