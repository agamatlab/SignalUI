using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;

namespace singalUI.Models;

/// <summary>
/// Represents a single stage controller instance
/// Axes are automatically determined based on hardware type and controller model
/// </summary>
public partial class StageInstance : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private StageHardwareType _hardwareType = StageHardwareType.None;

    [ObservableProperty]
    private SigmakokiControllerType _sigmakokiController = SigmakokiControllerType.HSC_103;

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private string _connectionStatus = "Not configured";

    [ObservableProperty]
    private string[] _availableAxes = System.Array.Empty<string>();

    /// <summary>Serial port for HSC-103 rotation stage (e.g. COM3).</summary>
    [ObservableProperty]
    private string _serialPortName = "COM3";

    /// <summary>HSC-103 physical axis index 1–3 (see hsc103.py --axis).</summary>
    [ObservableProperty]
    private int _hsc103AxisNumber = 1;

    /// <summary>Degrees per controller pulse (default matches hsc103.py).</summary>
    [ObservableProperty]
    private double _degreesPerPulse = 0.00001;

    /// <summary>
    /// Get the axes this stage controls based on its hardware type
    /// Automatically determined based on controller type
    /// </summary>
    public AxisType[] GetEnabledAxes()
    {
        return HardwareType switch
        {
            StageHardwareType.PI => GetPIAxes(),
            StageHardwareType.Sigmakoki => GetSigmakokiAxes(),
            StageHardwareType.SigmakokiRotationStage => new[] { AxisType.Rz },
            _ => System.Array.Empty<AxisType>()
        };
    }

    /// <summary>
    /// Get axes for PI controllers (depends on connected hardware)
    /// </summary>
    private AxisType[] GetPIAxes()
    {
        // For PI E-712 hexapod, always return all 6 axes
        // The controller will handle which axes are actually available
        return new[] { AxisType.X, AxisType.Y, AxisType.Z, AxisType.Rx, AxisType.Ry, AxisType.Rz };
    }

    /// <summary>
    /// Get axes for Sigma Koki controllers based on model
    /// </summary>
    private AxisType[] GetSigmakokiAxes()
    {
        // Optosigma OSMS26-100(X): single linear axis mapped to X in the UI.
        return new[] { AxisType.X };
    }

    /// <summary>
    /// Get the total number of enabled axes
    /// </summary>
    public int EnabledAxesCount => GetEnabledAxes().Length;

    /// <summary>
    /// Get display name for this stage instance
    /// </summary>
    public string DisplayName => $"Stage {Id} ({GetHardwareTypeName()})";

    /// <summary>
    /// Get hardware type name
    /// </summary>
    private string GetHardwareTypeName() => StageHardwareUi.ProductName(HardwareType);

    /// <summary>
    /// Get enabled axes as a string
    /// </summary>
    public string GetEnabledAxesString()
    {
        var axes = GetEnabledAxes();
        if (axes.Length == 0) return "None";
        return string.Join(", ", axes.Select(a => a.ToString()));
    }

    /// <summary>
    /// Check if a specific axis type is enabled on this stage
    /// </summary>
    public bool IsAxisEnabled(AxisType axis)
    {
        return GetEnabledAxes().Contains(axis);
    }
}

