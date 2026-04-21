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
    /// <summary>Create controller from full stage configuration (COM, HSC axis, DPP for rotation stage).</summary>
    public static StageController? CreateController(StageInstance configuration)
    {
        return configuration.HardwareType switch
        {
            StageHardwareType.PI => new PIController(),
            StageHardwareType.Sigmakoki => CreateSigmakokiController(configuration.SigmakokiController, configuration.SerialPortName),
            StageHardwareType.SigmakokiRotationStage => new Hsc103RotationStageController(
                configuration.SerialPortName,
                configuration.Hsc103AxisNumber,
                configuration.DegreesPerPulse),
            _ => null
        };
    }

    public static StageController? CreateController(StageHardwareType hardwareType, SigmakokiControllerType sigmakokiType = SigmakokiControllerType.HSC_103, string portName = "COM3")
    {
        var cfg = new StageInstance
        {
            HardwareType = hardwareType,
            SigmakokiController = sigmakokiType,
            SerialPortName = portName
        };
        return CreateController(cfg);
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
        return StageHardwareUi.ProductName(hardwareType);
    }
}
