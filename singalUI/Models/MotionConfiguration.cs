using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace singalUI.Models
{
    public partial class MotionConfiguration : ObservableObject
    {
        [ObservableProperty]
        private AxisType _axis;

        [ObservableProperty]
        private string _label = string.Empty;

        [ObservableProperty]
        private string _unit = string.Empty;

        [ObservableProperty]
        private bool _isEnabled;

        [ObservableProperty]
        private double _range;

        [ObservableProperty]
        private double _stepSize;

        [ObservableProperty]
        private int _numSteps;

        [ObservableProperty]
        private ControllerType _selectedController = ControllerType.None;

        // Target position for absolute mode (in µm)
        [ObservableProperty]
        private double _targetPosition;

        // Current position for calculation (set by ViewModel, in µm)
        [ObservableProperty]
        private double _currentPosition;

        // Calculated end position for relative mode: CurrentPosition + (StepSize * NumSteps)
        public double CalculatedEndPosition => CurrentPosition + (StepSize * NumSteps);

        // Auto-calculated step size for absolute mode: (TargetPosition - CurrentPosition) / NumSteps
        public double AutoStepSize
        {
            get
            {
                if (NumSteps > 0)
                {
                    return (TargetPosition - CurrentPosition) / NumSteps;
                }
                return 0;
            }
        }

        partial void OnStepSizeChanged(double value)
        {
            if (NumSteps > 0)
            {
                Range = Round(StepSize * NumSteps, 6);
            }
            OnPropertyChanged(nameof(CalculatedEndPosition));
        }

        partial void OnNumStepsChanged(int value)
        {
            Range = Round(StepSize * NumSteps, 6);
            OnPropertyChanged(nameof(CalculatedEndPosition));
            OnPropertyChanged(nameof(AutoStepSize));
        }

        partial void OnCurrentPositionChanged(double value)
        {
            OnPropertyChanged(nameof(CalculatedEndPosition));
            OnPropertyChanged(nameof(AutoStepSize));
        }

        partial void OnTargetPositionChanged(double value)
        {
            OnPropertyChanged(nameof(AutoStepSize));
        }

        private double Round(double value, int places)
        {
            return Math.Round(value, places);
        }
    }

    public enum AxisType
    {
        X, Y, Z, Rx, Ry, Rz
    }

    public enum SamplingMode
    {
        Manual,
        Triggered,
        Timed,
        Control
    }

    public enum ControllerType
    {
        None,
        ManualJog,
        AutoStep,
        PID,
        Servo,
        Stepper,
        DC
    }

    public enum BinningMode
    {
        Bin1x1,
        Bin2x2,
        Bin4x4
    }
}
