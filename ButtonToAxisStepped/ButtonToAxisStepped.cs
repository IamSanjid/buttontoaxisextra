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
    [Plugin("Button to Axis (Stepped)", Group = "Axis")]
    [PluginInput(DeviceBindingCategory.Momentary, "Button")]
    [PluginOutput(DeviceBindingCategory.Range, "Axis")]
    public class ButtonToAxisStepped : Plugin
    {
        [PluginGui("Axis on release (%)", Order = 0, Group = "Axis")]
        public double Range { get; set; } = -100;

        [PluginGui("Axis when pressed (%)", Order = 1, Group = "Axis")]
        public double RangePressed { get; set; } = 100;

        [PluginGui("Step Percentages(%)(comma[,] seperated)", Order = 2)]
        public string StepPercentages { get; set; } = "20, 50, 80, 100";

        [PluginGui("Initial Wait (ms)", Order = 4)]
        public double InitialWait { get; set; } = 1000; // wait before first step-down

        [PluginGui("Step Down Interval (ms)", Order = 5)]
        public double StepDownInterval { get; set; } = 500; // interval between each step-down

        private int _currentStep = 0;
        private double[] _steps;
        private bool _wasPressed = false;
        private readonly Stopwatch _timeSinceLastPress = new Stopwatch();
        private readonly Stopwatch _timeSinceLastStepDown = new Stopwatch();
        private bool _initialWaitElapsed = false;

        private volatile bool _isWatching;
        private Thread _watcherThread;

        public override void OnActivate()
        {
            _currentStep = -1;
            _wasPressed = false;
            _initialWaitElapsed = false;
            _timeSinceLastPress.Restart();
            _timeSinceLastStepDown.Restart();

            var stepPercentages = StepPercentages
                .Replace(" ", "")
                .Replace("%", "")
                .Split(',');
            _steps = stepPercentages
                .SkipWhile(s => string.IsNullOrEmpty(s))
                .Select(s => double.Parse(s))
                .ToArray();

            _isWatching = true;
            _watcherThread = new Thread(WatcherLoop) { IsBackground = true };
            _watcherThread.Start();
        }

        public override void OnDeactivate()
        {
            _isWatching = false;
            _watcherThread?.Join(200);
        }

        public override void Update(params short[] values)
        {
            bool isPressed = values[0] != 0;

            if (isPressed && !_wasPressed)
            {
                _currentStep = (_currentStep + 1) % _steps.Length;
                _timeSinceLastPress.Restart();
                _timeSinceLastStepDown.Restart();
                _initialWaitElapsed = false;
            }

            _wasPressed = isPressed;

            if (_currentStep < 0)
            {
                WriteOutput(0, Functions.GetRangeFromPercentage(Range));
                return;
            }

            double percentage = _steps[_currentStep] / 100.0;
            var lerpedPercentage = Range + (percentage * (RangePressed - Range));
            WriteOutput(0, Functions.GetRangeFromPercentage(lerpedPercentage));
        }

        private void WatcherLoop()
        {
            while (_isWatching)
            {
                if (!_wasPressed && _currentStep >= 0)
                {
                    if (!_initialWaitElapsed && _timeSinceLastPress.ElapsedMilliseconds >= InitialWait)
                    {
                        _initialWaitElapsed = true;
                        _currentStep -= 1;
                        _timeSinceLastStepDown.Restart();
                        WriteOutput(0, GetCurrentOutput());
                    }
                    else if (_initialWaitElapsed && _timeSinceLastStepDown.ElapsedMilliseconds >= StepDownInterval)
                    {
                        _currentStep -= 1;
                        _timeSinceLastStepDown.Restart();
                        WriteOutput(0, GetCurrentOutput());
                    }
                }
                Thread.Sleep(16);
            }
        }

        private short GetCurrentOutput()
        {
            if (_currentStep < 0) return Functions.GetRangeFromPercentage(Range);
            double percentage = _steps[_currentStep] / 100.0;
            var lerpedPercentage = Range + (percentage * (RangePressed - Range));
            return Functions.GetRangeFromPercentage(lerpedPercentage);
        }

        public override PropertyValidationResult Validate(PropertyInfo propertyInfo, dynamic value)
        {
            switch (propertyInfo.Name)
            {
                case nameof(Range):
                case nameof(RangePressed):
                    return InputValidation.ValidateRange(value, -100.0, 100.0);
                case nameof(StepPercentages):
                    try
                    {
                        var stepPercentages = StepPercentages.Replace(" ", "").Replace("%", "").Split(',');
                        if (stepPercentages.Length == 0)
                        {
                            return new PropertyValidationResult(false, "Need at least one step percentage");
                        }
                        foreach (var stepPercentage in stepPercentages)
                        {
                            if (string.IsNullOrEmpty(stepPercentage)) continue;
                            var percentage = double.Parse(stepPercentage);
                            var validationResult = InputValidation.ValidateRange(percentage, 0.0, 100.0);
                            if (!validationResult.IsValid)
                            {
                                return validationResult;
                            }
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
