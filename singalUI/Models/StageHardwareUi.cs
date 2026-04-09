namespace singalUI.Models;

/// <summary>User-facing labels for stage hardware (Setup / Stage tab).</summary>
public static class StageHardwareUi
{
    public static string ProductName(StageHardwareType hardwareType) =>
        hardwareType switch
        {
            StageHardwareType.PI => "PI 6-dof",
            StageHardwareType.Sigmakoki => "Optosigma OSMS26-100(X)",
            StageHardwareType.SigmakokiRotationStage => "Optosigma OSMS-120YAW",
            _ => "None"
        };
}
