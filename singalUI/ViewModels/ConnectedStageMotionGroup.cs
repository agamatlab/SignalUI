using CommunityToolkit.Mvvm.ComponentModel;
using singalUI.Models;
using singalUI.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace singalUI.ViewModels;

/// <summary>One connected stage: motion rows, per-stage current position, and raw/zero toggle.</summary>
public partial class ConnectedStageMotionGroup : ObservableObject
{
    public StageWrapper Wrapper { get; }
    public ObservableCollection<MotionConfiguration> Rows { get; }

    public string ProductTitle => StageHardwareUi.ProductName(Wrapper.HardwareType);

    /// <summary>Unique radio group names per stage so Absolute/Relative toggles do not leak across cards.</summary>
    public string MovementRadioGroup => $"StageMovA_{Wrapper.Id}";

    public string MovementRadioGroupB => $"StageMovB_{Wrapper.Id}";

    [ObservableProperty]
    private bool _displayPositionAsRaw = true;

    private double _rawX, _rawY, _rawZ, _rawRx, _rawRy, _rawRz;
    private double _zeroX, _zeroY, _zeroZ, _zeroRx, _zeroRy, _zeroRz;

    [ObservableProperty] private double _displayX;
    [ObservableProperty] private double _displayY;
    [ObservableProperty] private double _displayZ;
    [ObservableProperty] private double _displayRx;
    [ObservableProperty] private double _displayRy;
    [ObservableProperty] private double _displayRz;

    [ObservableProperty] private bool _showX;
    [ObservableProperty] private bool _showY;
    [ObservableProperty] private bool _showZ;
    [ObservableProperty] private bool _showRx;
    [ObservableProperty] private bool _showRy;
    [ObservableProperty] private bool _showRz;

    public ConnectedStageMotionGroup(StageWrapper wrapper, ObservableCollection<MotionConfiguration> rows)
    {
        Wrapper = wrapper;
        Rows = rows;
        RefreshAxisVisibility();
    }

    public void RefreshAxisVisibility()
    {
        var axes = Wrapper.EnabledAxes.ToHashSet();
        ShowX = axes.Contains(AxisType.X);
        ShowY = axes.Contains(AxisType.Y);
        ShowZ = axes.Contains(AxisType.Z);
        ShowRx = axes.Contains(AxisType.Rx);
        ShowRy = axes.Contains(AxisType.Ry);
        ShowRz = axes.Contains(AxisType.Rz);
    }

    private static bool IsRotational(AxisType axis) =>
        axis is AxisType.Rx or AxisType.Ry or AxisType.Rz;

    public void UpdateRawFromWrapper()
    {
        if (!Wrapper.IsConnected || Wrapper.Controller == null)
            return;

        foreach (var axis in Wrapper.EnabledAxes)
        {
            double controllerUnits = Wrapper.GetAxisPosition(axis);
            double disp = IsRotational(axis) ? controllerUnits : controllerUnits * 1000.0;
            switch (axis)
            {
                case AxisType.X: _rawX = disp; break;
                case AxisType.Y: _rawY = disp; break;
                case AxisType.Z: _rawZ = disp; break;
                case AxisType.Rx: _rawRx = disp; break;
                case AxisType.Ry: _rawRy = disp; break;
                case AxisType.Rz: _rawRz = disp; break;
            }
        }

        ApplyDisplayedPositions();
    }

    partial void OnDisplayPositionAsRawChanged(bool value)
    {
        if (!value)
            CaptureZeroForOwnedAxes();
        ApplyDisplayedPositions();
    }

    private void CaptureZeroForOwnedAxes()
    {
        if (ShowX) _zeroX = _rawX;
        if (ShowY) _zeroY = _rawY;
        if (ShowZ) _zeroZ = _rawZ;
        if (ShowRx) _zeroRx = _rawRx;
        if (ShowRy) _zeroRy = _rawRy;
        if (ShowRz) _zeroRz = _rawRz;
    }

    public void ApplyDisplayedPositions()
    {
        if (DisplayPositionAsRaw)
        {
            DisplayX = ShowX ? _rawX : 0;
            DisplayY = ShowY ? _rawY : 0;
            DisplayZ = ShowZ ? _rawZ : 0;
            DisplayRx = ShowRx ? _rawRx : 0;
            DisplayRy = ShowRy ? _rawRy : 0;
            DisplayRz = ShowRz ? _rawRz : 0;
        }
        else
        {
            DisplayX = ShowX ? _rawX - _zeroX : 0;
            DisplayY = ShowY ? _rawY - _zeroY : 0;
            DisplayZ = ShowZ ? _rawZ - _zeroZ : 0;
            DisplayRx = ShowRx ? _rawRx - _zeroRx : 0;
            DisplayRy = ShowRy ? _rawRy - _zeroRy : 0;
            DisplayRz = ShowRz ? _rawRz - _zeroRz : 0;
        }
    }
}
