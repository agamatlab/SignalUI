namespace singalUI.Models;

/// <summary>One rotation sweep sample: stage angle (rad) and sensor 6-DoF pose (rad / mm).</summary>
public sealed class RotationCalibrationSample
{
    public double ThetaRad { get; set; }
    public double Rx { get; set; }
    public double Ry { get; set; }
    public double Rz { get; set; }
    public double Tx { get; set; }
    public double Ty { get; set; }
    public double Tz { get; set; }

    public string Summary =>
        $"θ={ThetaRad * 180.0 / System.Math.PI:F2}° | " +
        $"R=[{Rx:F4},{Ry:F4},{Rz:F4}] rad | T=[{Tx:F4},{Ty:F4},{Tz:F4}] mm";
}
