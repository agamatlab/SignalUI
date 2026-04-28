using singalUI.libs;
using singalUI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace singalUI.Services;

/// <summary>
/// Factory for creating stage controller instances based on hardware type
/// Supports both built-in controllers and plugin-based controllers
/// </summary>
public static class StageControllerFactory
{
    private static PluginLoader? _pluginLoader;
    
    /// <summary>
    /// Initialize the plugin system - must be called before using plugins
    /// </summary>
    public static void InitializePlugins()
    {
        Console.WriteLine("[StageControllerFactory] Initializing plugin system...");
        _pluginLoader = new PluginLoader();
        int loadedCount = _pluginLoader.LoadPlugins();
        Console.WriteLine($"[StageControllerFactory] Plugin system initialized with {loadedCount} plugin(s)");
    }
    
    /// <summary>
    /// Get all available controller types (built-in + plugins)
    /// </summary>
    public static List<ControllerInfo> GetAvailableControllers()
    {
        var controllers = new List<ControllerInfo>();
        
        // Built-in controllers
        controllers.Add(new ControllerInfo
        {
            Name = "PI",
            DisplayName = "PI (Physik Instrumente)",
            Manufacturer = "Physik Instrumente",
            Description = "PI motion controllers via USB",
            IsPlugin = false,
            HardwareType = StageHardwareType.PI
        });
        
        controllers.Add(new ControllerInfo
        {
            Name = "Sigmakoki",
            DisplayName = "Sigma Koki",
            Manufacturer = "Sigma Koki",
            Description = "Sigma Koki controllers via serial port",
            IsPlugin = false,
            HardwareType = StageHardwareType.Sigmakoki
        });
        
        controllers.Add(new ControllerInfo
        {
            Name = "SigmakokiRotation",
            DisplayName = "Sigma Koki HSC-103 Rotation",
            Manufacturer = "Sigma Koki",
            Description = "HSC-103 rotation stage via Python bridge",
            IsPlugin = false,
            HardwareType = StageHardwareType.SigmakokiRotationStage
        });
        
        // Plugin controllers
        if (_pluginLoader != null)
        {
            foreach (var plugin in _pluginLoader.LoadedPlugins.Values)
            {
                controllers.Add(new ControllerInfo
                {
                    Name = plugin.Metadata.Name,
                    DisplayName = plugin.Metadata.DisplayName,
                    Manufacturer = plugin.Metadata.Manufacturer,
                    Version = plugin.Metadata.Version,
                    Description = plugin.Metadata.Description,
                    IsPlugin = true,
                    HardwareType = StageHardwareType.Plugin
                });
            }
        }
        
        return controllers;
    }
    
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
        // Use built-in controllers for Sigmakoki only
        if (configuration.HardwareType != StageHardwareType.Plugin)
        {
            switch (configuration.HardwareType)
            {
                case StageHardwareType.PI:
                    // PI is now a plugin - try to load it
                    Console.WriteLine("[StageControllerFactory] PI controller is now a plugin");
                    if (_pluginLoader != null)
                    {
                        var piConfig = new PluginConfiguration();
                        return _pluginLoader.CreateController("PI", piConfig);
                    }
                    Console.WriteLine("[StageControllerFactory] PI plugin not found");
                    return null;
                    
                case StageHardwareType.Sigmakoki:
                    Console.WriteLine("[StageControllerFactory] Creating built-in Sigmakoki controller");
                    return CreateSigmakokiController(configuration.SigmakokiController, configuration.SerialPortName);
                    
                case StageHardwareType.SigmakokiRotationStage:
                    // Removed - Python wrapper no longer supported
                    Console.WriteLine("[StageControllerFactory] SigmakokiRotationStage removed (Python wrapper)");
                    return null;
                    
                case StageHardwareType.None:
                default:
                    return null;
            }
        }
        
        // Try to create from plugin
        if (_pluginLoader != null && !string.IsNullOrEmpty(configuration.PluginName))
        {
            var pluginConfig = new PluginConfiguration
            {
                Parameters = configuration.PluginParameters ?? new Dictionary<string, object>()
            };
            
            Console.WriteLine($"[StageControllerFactory] Creating controller from plugin: {configuration.PluginName}");
            return _pluginLoader.CreateController(configuration.PluginName, pluginConfig);
        }
        
        Console.WriteLine("[StageControllerFactory] No plugin name specified for Plugin hardware type");
        return null;
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
    
    /// <summary>
    /// Get the plugin loader instance
    /// </summary>
    public static PluginLoader? GetPluginLoader()
    {
        return _pluginLoader;
    }
}

/// <summary>
/// Information about an available controller (built-in or plugin)
/// </summary>
public class ControllerInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPlugin { get; set; }
    public StageHardwareType? HardwareType { get; set; }
}
