using System.Reflection;
using System.Timers;
using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Plugins.Remapper
{
    [Plugin("Button to Axis (Smooth)", Group = "Axis", Description = "Ramps a button to an axis value over time")]
    [PluginInput(DeviceBindingCategory.Momentary, "Button")]
    [PluginOutput(DeviceBindingCategory.Range, "Axis", Group = "Axis")]
    public class ButtonToAxisSmooth : Plugin
    {
        [PluginGui("Axis on release (%)", Order = 0, Group = "Axis")]
        public double Range { get; set; }

        [PluginGui("Axis when pressed (%)", Order = 1, Group = "Axis")]
        public double RangePressed { get; set; }

        [PluginGui("Attack Step Size (%)", Order = 2)]
        public double AttackStep { get; set; }

        [PluginGui("Release Step Size (%)", Order = 3)]
        public double ReleaseStep { get; set; }

        [PluginGui("Tick Rate (ms)", Order = 4)]
        public double TickRate { get; set; }

        private readonly Timer _timer;
        private double _currentPercentage;
        private bool _isPressed;
        private readonly object _lock = new object();

        public ButtonToAxisSmooth()
        {
            Range = -100;         // Ideal for Xbox triggers resting at -100%
            RangePressed = 100;    // Full throttle
            AttackStep = 4.0;      // 4% per tick at 16ms = ~400ms from 0-100%
            ReleaseStep = 8.0;     // Fall twice as fast
            TickRate = 16.0;       // Smooth 60Hz update rate

            _timer = new Timer();
            _timer.Elapsed += OnTick;
        }

        public override void OnActivate()
        {
            lock (_lock)
            {
                _timer.Interval = TickRate;
                _currentPercentage = Range;
                WriteOutput(0, Functions.GetRangeFromPercentage((short)_currentPercentage));
            }
        }

        public override void OnDeactivate()
        {
            lock (_lock)
            {
                _timer.Stop();
            }
        }

        public override void Update(params short[] values)
        {
            lock (_lock)
            {
                // values[0] == 0 means released, otherwise pressed (usually 1)
                _isPressed = values[0] != 0;

                if (!_timer.Enabled)
                {
                    _timer.Start();
                }
            }
        }

        private void OnTick(object sender, ElapsedEventArgs e)
        {
            lock (_lock)
            {
                if (_isPressed)
                {
                    if (_currentPercentage < RangePressed)
                    {
                        _currentPercentage += AttackStep;
                        if (_currentPercentage >= RangePressed)
                        {
                            _currentPercentage = RangePressed;
                            _timer.Stop(); // Destination reached, stop wasting CPU cycles
                        }
                    }
                }
                else
                {
                    if (_currentPercentage > Range)
                    {
                        _currentPercentage -= ReleaseStep;
                        if (_currentPercentage <= Range)
                        {
                            _currentPercentage = Range;
                            _timer.Stop(); // Returned to rest, stop timer
                        }
                    }
                }

                // Send the smoothly scaled output to ViGEm
                WriteOutput(0, Functions.GetRangeFromPercentage((short)_currentPercentage));
            }
        }

        public override PropertyValidationResult Validate(PropertyInfo propertyInfo, dynamic value)
        {
            switch (propertyInfo.Name)
            {
                case nameof(Range):
                case nameof(RangePressed):
                    return InputValidation.ValidateRange(value, -100.0, 100.0);
                case nameof(AttackStep):
                case nameof(ReleaseStep):
                    return InputValidation.ValidateRange(value, 0.1, 100.0);
                case nameof(TickRate):
                    return InputValidation.ValidateRange(value, 1.0, 1000.0);
            }

            return PropertyValidationResult.ValidResult;
        }
    }
}