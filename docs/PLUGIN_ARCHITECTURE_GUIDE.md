# Plugin Architecture for Stage Controllers

## Problem Statement

You need to ship the application to factories, and some may have new/custom stage hardware that requires new controller implementations. You want to add support for new controllers **without rebuilding and reshipping the entire application**.

## Solution Overview

Implement a **plugin system** where stage controllers can be loaded dynamically from external DLL files at runtime.

---

## Architecture Options

### Option 1: .NET Assembly Plugin System (Recommended)

Load controller implementations from external DLL files at runtime.

**Pros:**
- ✅ No recompilation needed
- ✅ Ship only the new controller DLL
- ✅ Full C# capabilities
- ✅ Type-safe
- ✅ Easy to implement

**Cons:**
- ⚠️ Requires .NET knowledge to create plugins
- ⚠️ Version compatibility concerns

### Option 2: Scripting (C# Script / Python)

Load controller logic from script files.

**Pros:**
- ✅ Very flexible
- ✅ No compilation needed
- ✅ Easy to modify on-site

**Cons:**
- ❌ Performance overhead
- ❌ Less type-safe
- ❌ More complex error handling

### Option 3: Configuration-Based (Limited)

Define controller behavior through JSON/XML configuration.

**Pros:**
- ✅ Very simple
- ✅ No code needed

**Cons:**
- ❌ Only works for similar protocols
- ❌ Limited flexibility
- ❌ Not suitable for complex controllers

---

## Recommended Solution: .NET Plugin System

### Architecture Overview

```
SignalUI.exe (Main Application)
├── libs/
│   ├── IStageController.cs (interface)
│   └── StageController.cs (base class)
├── Plugins/
│   ├── PIController.Plugin.dll
│   ├── SigmakokiController.Plugin.dll
│   └── CustomController.Plugin.dll  ← New controller, just drop in!
└── plugin-manifest.json
```

### Step 1: Create Plugin Interface

```csharp
// File: singalUI/libs/IStageControllerPlugin.cs
namespace singalUI.libs;

/// <summary>
/// Interface that all stage controller plugins must implement
/// This allows the main application to discover and load plugins at runtime
/// </summary>
public interface IStageControllerPlugin
{
    /// <summary>Plugin metadata</summary>
    PluginMetadata Metadata { get; }
    
    /// <summary>Create a new instance of the controller</summary>
    IStageController CreateController(PluginConfiguration config);
    
    /// <summary>Validate if this plugin can run on the current platform</summary>
    bool IsCompatible();
}

/// <summary>
/// Plugin metadata for identification and versioning
/// </summary>
public class PluginMetadata
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string[] SupportedPlatforms { get; set; } = Array.Empty<string>();
    public string[] RequiredDlls { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Configuration passed to plugin when creating controller
/// </summary>
public class PluginConfiguration
{
    public Dictionary<string, object> Parameters { get; set; } = new();
    
    public T GetParameter<T>(string key, T defaultValue = default!)
    {
        if (Parameters.TryGetValue(key, out var value))
        {
            if (value is T typedValue)
                return typedValue;
            
            // Try to convert
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }
}
```

### Step 2: Create Plugin Loader

```csharp
// File: singalUI/Services/PluginLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using singalUI.libs;

namespace singalUI.Services;

/// <summary>
/// Loads stage controller plugins from external DLL files
/// </summary>
public class PluginLoader
{
    private static readonly string PluginDirectory = 
        Path.Combine(AppContext.BaseDirectory, "Plugins");
    
    private readonly Dictionary<string, IStageControllerPlugin> _loadedPlugins = new();
    
    public IReadOnlyDictionary<string, IStageControllerPlugin> LoadedPlugins => _loadedPlugins;
    
    /// <summary>
    /// Load all plugins from the Plugins directory
    /// </summary>
    public void LoadPlugins()
    {
        Console.WriteLine($"[PluginLoader] Loading plugins from: {PluginDirectory}");
        
        if (!Directory.Exists(PluginDirectory))
        {
            Console.WriteLine("[PluginLoader] Plugins directory not found, creating...");
            Directory.CreateDirectory(PluginDirectory);
            return;
        }
        
        var dllFiles = Directory.GetFiles(PluginDirectory, "*.Plugin.dll", SearchOption.AllDirectories);
        Console.WriteLine($"[PluginLoader] Found {dllFiles.Length} plugin DLL(s)");
        
        foreach (var dllPath in dllFiles)
        {
            try
            {
                LoadPlugin(dllPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PluginLoader] Failed to load plugin {Path.GetFileName(dllPath)}: {ex.Message}");
            }
        }
        
        Console.WriteLine($"[PluginLoader] Successfully loaded {_loadedPlugins.Count} plugin(s)");
    }
    
    /// <summary>
    /// Load a single plugin from a DLL file
    /// </summary>
    private void LoadPlugin(string dllPath)
    {
        Console.WriteLine($"[PluginLoader] Loading: {Path.GetFileName(dllPath)}");
        
        // Load the assembly
        var assembly = Assembly.LoadFrom(dllPath);
        
        // Find types that implement IStageControllerPlugin
        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IStageControllerPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();
        
        if (pluginTypes.Count == 0)
        {
            Console.WriteLine($"[PluginLoader] No plugin types found in {Path.GetFileName(dllPath)}");
            return;
        }
        
        foreach (var pluginType in pluginTypes)
        {
            try
            {
                // Create instance of the plugin
                var plugin = (IStageControllerPlugin)Activator.CreateInstance(pluginType)!;
                
                // Check compatibility
                if (!plugin.IsCompatible())
                {
                    Console.WriteLine($"[PluginLoader] Plugin {plugin.Metadata.Name} is not compatible with this platform");
                    continue;
                }
                
                // Register the plugin
                _loadedPlugins[plugin.Metadata.Name] = plugin;
                
                Console.WriteLine($"[PluginLoader] Loaded plugin: {plugin.Metadata.DisplayName} v{plugin.Metadata.Version}");
                Console.WriteLine($"[PluginLoader]   Manufacturer: {plugin.Metadata.Manufacturer}");
                Console.WriteLine($"[PluginLoader]   Description: {plugin.Metadata.Description}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PluginLoader] Failed to instantiate plugin type {pluginType.Name}: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Get a plugin by name
    /// </summary>
    public IStageControllerPlugin? GetPlugin(string name)
    {
        return _loadedPlugins.TryGetValue(name, out var plugin) ? plugin : null;
    }
    
    /// <summary>
    /// Create a controller from a plugin
    /// </summary>
    public IStageController? CreateController(string pluginName, PluginConfiguration config)
    {
        var plugin = GetPlugin(pluginName);
        if (plugin == null)
        {
            Console.WriteLine($"[PluginLoader] Plugin not found: {pluginName}");
            return null;
        }
        
        try
        {
            return plugin.CreateController(config);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PluginLoader] Failed to create controller from plugin {pluginName}: {ex.Message}");
            return null;
        }
    }
}
```

### Step 3: Update StageControllerFactory

```csharp
// File: singalUI/Services/StageControllerFactory.cs
using singalUI.libs;
using singalUI.Models;

namespace singalUI.Services;

public static class StageControllerFactory
{
    private static PluginLoader? _pluginLoader;
    
    /// <summary>
    /// Initialize the plugin system
    /// </summary>
    public static void InitializePlugins()
    {
        _pluginLoader = new PluginLoader();
        _pluginLoader.LoadPlugins();
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
            IsPlugin = false,
            HardwareType = StageHardwareType.PI
        });
        
        controllers.Add(new ControllerInfo
        {
            Name = "Sigmakoki",
            DisplayName = "Sigma Koki",
            IsPlugin = false,
            HardwareType = StageHardwareType.Sigmakoki
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
                    IsPlugin = true
                });
            }
        }
        
        return controllers;
    }
    
    /// <summary>
    /// Create controller (supports both built-in and plugin controllers)
    /// </summary>
    public static StageController? CreateController(StageInstance configuration)
    {
        // Try built-in controllers first
        if (configuration.HardwareType != StageHardwareType.Plugin)
        {
            return configuration.HardwareType switch
            {
                StageHardwareType.PI => new PIController(),
                StageHardwareType.Sigmakoki => CreateSigmakokiController(
                    configuration.SigmakokiController, 
                    configuration.SerialPortName),
                StageHardwareType.SigmakokiRotationStage => new Hsc103RotationStageController(
                    configuration.SerialPortName,
                    configuration.Hsc103AxisNumber,
                    configuration.DegreesPerPulse),
                _ => null
            };
        }
        
        // Try plugin controllers
        if (_pluginLoader != null && !string.IsNullOrEmpty(configuration.PluginName))
        {
            var pluginConfig = new PluginConfiguration
            {
                Parameters = configuration.PluginParameters ?? new Dictionary<string, object>()
            };
            
            return _pluginLoader.CreateController(configuration.PluginName, pluginConfig) as StageController;
        }
        
        return null;
    }
    
    private static SigmakokiController CreateSigmakokiController(
        SigmakokiControllerType sigmakokiType, 
        string portName)
    {
        var controller = new SigmakokiController(ConvertToSKControllerType(sigmakokiType));
        controller.SetPort(portName);
        return controller;
    }
    
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
}

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
```

### Step 4: Update Models

```csharp
// File: singalUI/Models/StageHardwareType.cs
public enum StageHardwareType
{
    None,
    PI,
    Sigmakoki,
    SigmakokiRotationStage,
    Plugin  // New: Indicates a plugin-based controller
}

// File: singalUI/Models/StageInstance.cs
public partial class StageInstance : ObservableObject
{
    // Existing properties...
    
    [ObservableProperty]
    private string? _pluginName;  // Name of the plugin to use
    
    [ObservableProperty]
    private Dictionary<string, object>? _pluginParameters;  // Plugin-specific configuration
}
```

### Step 5: Initialize Plugins in App

```csharp
// File: singalUI/App.axaml.cs
public override void OnFrameworkInitializationCompleted()
{
    Console.WriteLine("[App] OnFrameworkInitializationCompleted START");
    
    // Initialize plugin system FIRST
    StageControllerFactory.InitializePlugins();
    
    // ... rest of initialization
}
```

---

## Creating a Plugin (Example)

### Example: Newport Controller Plugin

```csharp
// File: NewportController.Plugin/NewportControllerPlugin.cs
using singalUI.libs;
using System;

namespace NewportController.Plugin;

/// <summary>
/// Plugin for Newport motion controllers
/// </summary>
public class NewportControllerPlugin : IStageControllerPlugin
{
    public PluginMetadata Metadata => new()
    {
        Name = "Newport",
        DisplayName = "Newport Motion Controller",
        Manufacturer = "Newport Corporation",
        Version = "1.0.0",
        Description = "Support for Newport ESP and XPS motion controllers",
        Author = "Your Company",
        SupportedPlatforms = new[] { "Windows", "Linux" },
        RequiredDlls = new[] { "Newport.ESP301.CommandInterface.dll" }
    };
    
    public bool IsCompatible()
    {
        // Check if required DLLs are present
        var dllPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "Newport.ESP301.CommandInterface.dll");
        return File.Exists(dllPath);
    }
    
    public IStageController CreateController(PluginConfiguration config)
    {
        string portName = config.GetParameter<string>("PortName", "COM1");
        int baudRate = config.GetParameter<int>("BaudRate", 19200);
        
        return new NewportController(portName, baudRate);
    }
}

/// <summary>
/// Newport controller implementation
/// </summary>
public class NewportController : StageController
{
    private readonly string _portName;
    private readonly int _baudRate;
    private System.IO.Ports.SerialPort? _port;
    private string[] _axes = Array.Empty<string>();
    
    public NewportController(string portName, int baudRate)
    {
        _portName = portName;
        _baudRate = baudRate;
    }
    
    public override void Connect()
    {
        _port = new System.IO.Ports.SerialPort(_portName, _baudRate);
        _port.Open();
        
        // Query available axes
        _axes = new[] { "1", "2", "3" };  // Example
        
        Console.WriteLine($"[Newport] Connected to {_portName}");
    }
    
    public override void Disconnect()
    {
        _port?.Close();
        _port?.Dispose();
        _port = null;
    }
    
    public override bool CheckConnected()
    {
        return _port?.IsOpen ?? false;
    }
    
    public override string[] GetAxesList()
    {
        return _axes;
    }
    
    public override void MoveRelative(int axes, double amount)
    {
        if (_port == null || !_port.IsOpen)
            throw new InvalidOperationException("Not connected");
        
        // Newport command format: "1PR{amount}"
        string command = $"{axes + 1}PR{amount}\r\n";
        _port.WriteLine(command);
        
        Console.WriteLine($"[Newport] Move axis {axes + 1} by {amount}");
    }
    
    public override double GetPosition(int axisIndex)
    {
        if (_port == null || !_port.IsOpen)
            return 0.0;
        
        // Newport command: "1TP?" (Tell Position)
        string command = $"{axisIndex + 1}TP?\r\n";
        _port.WriteLine(command);
        
        string response = _port.ReadLine();
        if (double.TryParse(response, out double position))
            return position;
        
        return 0.0;
    }
    
    public override double GetMinTravel(int axisIndex) => -50.0;
    public override double GetMaxTravel(int axisIndex) => 50.0;
    
    public override void ReferenceAxes()
    {
        // Newport homing command
        for (int i = 0; i < _axes.Length; i++)
        {
            string command = $"{i + 1}OR\r\n";  // Origin search
            _port?.WriteLine(command);
        }
    }
    
    public override bool AreAxesReferenced()
    {
        // Check if all axes are homed
        return true;  // Simplified
    }
}
```

### Plugin Project Structure

```
NewportController.Plugin/
├── NewportController.Plugin.csproj
├── NewportControllerPlugin.cs
├── NewportController.cs
└── libs/
    └── Newport.ESP301.CommandInterface.dll
```

### Plugin .csproj File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference the main application's interface DLL -->
    <Reference Include="singalUI">
      <HintPath>..\..\singalUI\bin\Debug\net8.0\singalUI.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <!-- Copy native DLLs to output -->
    <None Include="libs\*.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

---

## Deployment Process

### For Factories with Standard Hardware

Ship the main application:
```
SignalUI-v1.0.0/
├── NanoMeas.exe
├── singalUI.dll
├── (other dependencies)
└── Plugins/
    ├── PIController.Plugin.dll
    └── SigmakokiController.Plugin.dll
```

### For Factories with Custom Hardware

1. **Develop the plugin** (one-time, at your office)
2. **Ship only the plugin DLL** to the factory:
   ```
   NewportController.Plugin.zip
   ├── NewportController.Plugin.dll
   └── Newport.ESP301.CommandInterface.dll
   ```
3. **Factory installs**: Extract to `Plugins/` folder
4. **Restart application**: Plugin auto-loads

**No recompilation needed!**

---

## Configuration File Support

### Plugin Manifest (Optional)

```json
// File: Plugins/plugin-manifest.json
{
  "plugins": [
    {
      "name": "Newport",
      "enabled": true,
      "configuration": {
        "PortName": "COM5",
        "BaudRate": 19200,
        "Timeout": 5000
      }
    },
    {
      "name": "Thorlabs",
      "enabled": false,
      "configuration": {
        "SerialNumber": "12345678"
      }
    }
  ]
}
```

### Load Configuration

```csharp
// File: singalUI/Services/PluginConfigurationLoader.cs
using System.Text.Json;

public class PluginConfigurationLoader
{
    public static Dictionary<string, PluginConfiguration> LoadConfigurations()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "plugin-manifest.json");
        
        if (!File.Exists(manifestPath))
            return new Dictionary<string, PluginConfiguration>();
        
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json);
        
        var configs = new Dictionary<string, PluginConfiguration>();
        
        foreach (var plugin in manifest?.Plugins ?? Array.Empty<PluginEntry>())
        {
            if (plugin.Enabled)
            {
                configs[plugin.Name] = new PluginConfiguration
                {
                    Parameters = plugin.Configuration
                };
            }
        }
        
        return configs;
    }
}

public class PluginManifest
{
    public PluginEntry[] Plugins { get; set; } = Array.Empty<PluginEntry>();
}

public class PluginEntry
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public Dictionary<string, object> Configuration { get; set; } = new();
}
```

---

## UI Integration

### Show Available Controllers in UI

```csharp
// In CalibrationSetupViewModel or ConfigViewModel
public ObservableCollection<ControllerInfo> AvailableControllers { get; } = new();

public void LoadAvailableControllers()
{
    AvailableControllers.Clear();
    
    var controllers = StageControllerFactory.GetAvailableControllers();
    foreach (var controller in controllers)
    {
        AvailableControllers.Add(controller);
    }
}
```

### XAML ComboBox

```xml
<ComboBox ItemsSource="{Binding AvailableControllers}"
          SelectedItem="{Binding SelectedController}">
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <StackPanel>
                <TextBlock Text="{Binding DisplayName}" FontWeight="Bold"/>
                <TextBlock Text="{Binding Manufacturer}" FontSize="10" Opacity="0.7"/>
                <TextBlock Text="{Binding Description}" FontSize="10" Opacity="0.5"/>
                <TextBlock Text="[Plugin]" FontSize="10" Foreground="Green" 
                          IsVisible="{Binding IsPlugin}"/>
            </StackPanel>
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

---

## Advanced: Hot Reload (Optional)

Allow loading plugins without restarting the app:

```csharp
public class PluginLoader
{
    private FileSystemWatcher? _watcher;
    
    public void EnableHotReload()
    {
        _watcher = new FileSystemWatcher(PluginDirectory, "*.Plugin.dll");
        _watcher.Created += OnPluginFileChanged;
        _watcher.Changed += OnPluginFileChanged;
        _watcher.EnableRaisingEvents = true;
    }
    
    private void OnPluginFileChanged(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"[PluginLoader] Plugin file changed: {e.Name}");
        
        // Wait a bit for file to be fully written
        System.Threading.Thread.Sleep(500);
        
        try
        {
            LoadPlugin(e.FullPath);
            PluginReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PluginLoader] Hot reload failed: {ex.Message}");
        }
    }
    
    public event EventHandler? PluginReloaded;
}
```

---

## Security Considerations

### Plugin Validation

```csharp
public class PluginValidator
{
    public static bool ValidatePlugin(Assembly assembly)
    {
        // Check if assembly is signed
        var name = assembly.GetName();
        var publicKey = name.GetPublicKey();
        
        if (publicKey == null || publicKey.Length == 0)
        {
            Console.WriteLine("[Security] Plugin is not signed!");
            return false;
        }
        
        // Verify signature matches your company's key
        // ... signature verification logic
        
        return true;
    }
}
```

### Sandboxing (Advanced)

For maximum security, load plugins in separate AppDomains or processes (more complex).

---

## Testing Plugins

### Plugin Test Template

```csharp
// File: NewportController.Plugin.Tests/NewportControllerTests.cs
using Xunit;

public class NewportControllerTests
{
    [Fact]
    public void Plugin_Metadata_IsValid()
    {
        var plugin = new NewportControllerPlugin();
        
        Assert.NotNull(plugin.Metadata);
        Assert.Equal("Newport", plugin.Metadata.Name);
        Assert.NotEmpty(plugin.Metadata.DisplayName);
    }
    
    [Fact]
    public void CreateController_WithValidConfig_ReturnsController()
    {
        var plugin = new NewportControllerPlugin();
        var config = new PluginConfiguration();
        config.Parameters["PortName"] = "COM1";
        
        var controller = plugin.CreateController(config);
        
        Assert.NotNull(controller);
        Assert.IsType<NewportController>(controller);
    }
}
```

---

## Migration Path

### Phase 1: Add Plugin Infrastructure (No Breaking Changes)

1. Add `IStageControllerPlugin` interface
2. Add `PluginLoader` class
3. Update `StageControllerFactory` to support plugins
4. Test with existing controllers

### Phase 2: Convert Existing Controllers to Plugins (Optional)

1. Create `PIController.Plugin` project
2. Move PI controller code to plugin
3. Keep built-in version for backward compatibility

### Phase 3: Deploy to Factories

1. Ship main app with plugin support
2. Create plugins for custom hardware as needed
3. Ship only plugin DLLs to factories

---

## Summary

### What You Get

✅ **No recompilation**: Add new controllers by dropping DLL files  
✅ **Easy deployment**: Ship only the plugin DLL (few MB)  
✅ **Backward compatible**: Existing controllers still work  
✅ **Type-safe**: Full C# with IntelliSense  
✅ **Flexible**: Each plugin can have custom configuration  
✅ **Discoverable**: UI automatically shows available controllers  

### Deployment Workflow

1. **Factory has new hardware** → Contact you
2. **You develop plugin** → Test locally
3. **Ship plugin DLL** → Email or USB drive
4. **Factory copies to Plugins/** → Done!
5. **Restart app** → New controller available

### File Size Comparison

- **Full app rebuild**: ~200 MB (entire application)
- **Plugin only**: ~1-5 MB (just the controller DLL)

**100x smaller deployment!**

---

## Alternative: Script-Based Plugins (Simpler)

If you want even simpler deployment, consider Python scripts:

```python
# File: Plugins/newport_controller.py
class NewportController:
    def connect(self, port):
        # Python serial communication
        pass
    
    def move_relative(self, axis, amount):
        # Send commands
        pass
```

Load via IronPython or Python.NET. Trade-off: slower, but no compilation needed.

---

## Conclusion

The **plugin architecture** is the best solution for your use case:

- Factories get updates in minutes, not days
- No need to rebuild/test entire application
- Each factory can have custom controllers
- You maintain full control over the core application

Start with the .NET plugin system (Option 1) - it's the most robust and type-safe solution.
