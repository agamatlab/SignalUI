using System;
using singalUI.libs;

namespace PIController.Plugin;

/// <summary>
/// Plugin for PI (Physik Instrumente) motion controllers
/// </summary>
public class PIControllerPlugin : IStageControllerPlugin
{
    public PluginMetadata Metadata => new()
    {
        Name = "PI",
        DisplayName = "PI (Physik Instrumente)",
        Manufacturer = "Physik Instrumente (PI) GmbH & Co. KG",
        Version = "1.0.0",
        Description = "PI motion controllers via USB (GCS2 protocol). Supports E-712 hexapod and other PI stages.",
        Author = "SignalUI Team",
        SupportedPlatforms = new[] { "Windows", "Linux", "OSX" },
        RequiredDlls = new[] { "PI_GCS2_DLL_x64.dll", "PI_GCS2_DLL.dll" }
    };
    
    public bool IsCompatible()
    {
        // Check if PI DLLs are available
        var dllPath64 = System.IO.Path.Combine(AppContext.BaseDirectory, "Plugins", "PI_GCS2_DLL_x64.dll");
        var dllPath32 = System.IO.Path.Combine(AppContext.BaseDirectory, "Plugins", "PI_GCS2_DLL.dll");
        
        bool has64 = System.IO.File.Exists(dllPath64);
        bool has32 = System.IO.File.Exists(dllPath32);
        
        Console.WriteLine($"[PIPlugin] Checking compatibility...");
        Console.WriteLine($"[PIPlugin]   PI_GCS2_DLL_x64.dll: {(has64 ? "Found" : "Not found")}");
        Console.WriteLine($"[PIPlugin]   PI_GCS2_DLL.dll: {(has32 ? "Found" : "Not found")}");
        
        // Need at least one DLL
        bool compatible = has64 || has32;
        Console.WriteLine($"[PIPlugin] Compatible: {compatible}");
        
        return compatible;
    }
    
    public StageController CreateController(PluginConfiguration config)
    {
        Console.WriteLine("[PIPlugin] Creating PI controller instance");
        
        // PI controller doesn't need configuration parameters
        // All settings are queried from the hardware
        
        return new PIController();
    }
}
