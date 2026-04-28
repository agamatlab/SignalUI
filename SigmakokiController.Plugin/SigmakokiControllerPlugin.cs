using System;
using singalUI.libs;

namespace SigmakokiController.Plugin;

/// <summary>
/// Plugin for Sigma Koki motion controllers
/// </summary>
public class SigmakokiControllerPlugin : IStageControllerPlugin
{
    public PluginMetadata Metadata => new()
    {
        Name = "Sigmakoki",
        DisplayName = "Sigma Koki Controller",
        Manufacturer = "Sigma Koki Co., Ltd.",
        Version = "1.0.0",
        Description = "Sigma Koki motion controllers via serial port (RS-232). Supports HSC-103, SHOT-302GS, SHOT-304GS, and other models.",
        Author = "SignalUI Team",
        SupportedPlatforms = new[] { "Windows" }, // Serial port only works on Windows in this build
        RequiredDlls = System.Array.Empty<string>()
    };
    
    public bool IsCompatible()
    {
        // Check if running on Windows (serial port requirement)
        bool isWindows = OperatingSystem.IsWindows();
        
        Console.WriteLine($"[SigmakokiPlugin] Checking compatibility...");
        Console.WriteLine($"[SigmakokiPlugin]   Platform: {Environment.OSVersion.Platform}");
        Console.WriteLine($"[SigmakokiPlugin]   Is Windows: {isWindows}");
        Console.WriteLine($"[SigmakokiPlugin] Compatible: {isWindows}");
        
        return isWindows;
    }
    
    public StageController CreateController(PluginConfiguration config)
    {
        Console.WriteLine("[SigmakokiPlugin] Creating Sigmakoki controller instance");
        
        // Get configuration parameters
        string controllerTypeStr = config.GetParameter<string>("ControllerType", "HSC_103");
        string portName = config.GetParameter<string>("PortName", "COM3");
        
        // Parse controller type
        SKSampleClass.Controller_Type controllerType = controllerTypeStr switch
        {
            "GIP_101B" => SKSampleClass.Controller_Type.GIP_101B,
            "GSC01" => SKSampleClass.Controller_Type.GSC01,
            "SHOT_302GS" => SKSampleClass.Controller_Type.SHOT_302GS,
            "HSC_103" => SKSampleClass.Controller_Type.HSC_103,
            "SHOT_304GS" => SKSampleClass.Controller_Type.SHOT_304GS,
            "Hit_MV" => SKSampleClass.Controller_Type.Hit_MV,
            "PGC_04" => SKSampleClass.Controller_Type.PGC_04,
            _ => SKSampleClass.Controller_Type.HSC_103
        };
        
        Console.WriteLine($"[SigmakokiPlugin] Configuration: ControllerType={controllerTypeStr}, PortName={portName}");
        
        var controller = new SigmakokiController(controllerType);
        controller.SetPort(portName);
        
        return controller;
    }
}
