using singalUI.libs;

using singalUI.Models;

namespace singalUI.Services;

/// <summary>
/// Factory for creating stage controller instances based on hardware type
/// </summary>
public static class StageControllerFactory
{
    /// <summary>
    /// Create a stage controller instance based on the specified hardware type
    /// </summary>
    /// <param name="hardwareType">Type of stage hardware</param>
    /// <param name="sigmakokiType">Sigma Koki controller type (required if hardwareType is Sigmakoki)</param>
    /// <param name="portName">Serial port name (e.g., "COM3" for Windows, "/dev/ttyUSB0" for Linux)</param>
    /// <returns>StageController instance or null if type is None</returns>
    public static StageController? CreateController(StageHardwareType hardwareType, SigmakokiControllerType sigmakokiType = SigmakokiControllerType.HSC_103, string portName = "COM3")
    {
        return hardwareType switch
        {
            StageHardwareType.PI => new PIController(),
            StageHardwareType.Sigmakoki => CreateSigmakokiController(sigmakokiType, portName),
            _ => null
        };
    }

    private static SigmakokiController CreateSigmakokiController(SigmakokiControllerType sigmakokiType, string portName)
    {
        var controller = new SigmakokiController(ConvertToSKControllerType(sigmakokiType));
        controller.SetPort(portName);
        return controller;
    }

    /// <summary>
    /// Convert SigmakokiControllerType enum to SKSampleClass.Controller_Type
    /// </summary>
    private static SKSampleClass.Controller_Type ConvertToSKControllerType(SigmakokiControllerType type)
    {
        return type switch
        {
            SigmakokiControllerType.GIP_101B => SKSampleClass.Controller_Type.GIP_101B,
            SigmakokiControllerType.GSC01 => SKSampleClass.Controller_Type.GSC01,
            SigmakokiControllerType.SHOT_302GS => SKSampleClass.Controller_Type.SHOT_302GS,
            SigmakokiControllerType.HSC_103 => SKSampleClass.Controller_Type.HSC_103,
            SigmakokiControllerType.SHOT_304GS => SKSampleClass.Controller_Type.SHOT_304GS,
            SigmakokiControllerType.Hit_MV => SKSampleClass.Controller_Type.Hit_MV,
            SigmakokiControllerType.PGC_04 => SKSampleClass.Controller_Type.PGC_04,
            _ => SKSampleClass.Controller_Type.HSC_103
        };
    }

    /// <summary>
    /// Get display name for hardware type
    /// </summary>
    public static string GetDisplayName(StageHardwareType hardwareType)
    {
        return hardwareType switch
        {
            StageHardwareType.PI => "PI (Physik Instrumente)",
            StageHardwareType.Sigmakoki => "Sigma Koki",
            _ => "None"
        };
    }
}
