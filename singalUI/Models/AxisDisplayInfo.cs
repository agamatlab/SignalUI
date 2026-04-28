using CommunityToolkit.Mvvm.ComponentModel;

namespace singalUI.Models;

/// <summary>
/// Represents display information for a single axis
/// </summary>
public partial class AxisDisplayInfo : ObservableObject
{
    /// <summary>
    /// Axis name (e.g., "X", "Y", "Z", "Rx", "Ry", "Rz")
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Axis label with unit (e.g., "X (mm):", "Rx:")
    /// </summary>
    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>
    /// Current value as formatted text
    /// </summary>
    [ObservableProperty]
    private string _valueText = "-";

    /// <summary>
    /// Whether this is a rotational axis (affects formatting)
    /// </summary>
    [ObservableProperty]
    private bool _isRotational = false;
}
