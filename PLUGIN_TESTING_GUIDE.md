# Plugin System - Quick Start Guide

## Overview

The SignalUI plugin system allows you to add support for new stage controllers without recompiling the entire application. Simply drop a plugin DLL into the `Plugins/` folder and restart the app.

## Building and Testing

### Step 1: Build the Solution

```bash
# From the solution root directory
dotnet build singalUI.sln
```

This will:
1. Build the main application (`singalUI`)
2. Build the test plugin (`MockStageController.Plugin`)
3. Automatically copy the plugin DLL to `singalUI/bin/Debug/net8.0/Plugins/`

### Step 2: Run Unit Tests

```bash
# Run all tests
dotnet test singalUI.Tests/singalUI.Tests.csproj

# Run only plugin tests
dotnet test singalUI.Tests/singalUI.Tests.csproj --filter "FullyQualifiedName~Plugin"
```

### Step 3: Run the Application

```bash
cd singalUI
dotnet run
```

Watch the console output for plugin loading messages:
```
[App] Initializing plugin system...
[StageControllerFactory] Initializing plugin system...
[PluginLoader] Loading plugins from: .../Plugins
[PluginLoader] Found 1 plugin DLL(s)
[PluginLoader] Loading: MockStageController.Plugin.dll
[PluginLoader] ✓ Loaded plugin: Mock Stage Controller (Test) v1.0.0
```

## Testing the Plugin System

### Manual Testing

1. **Check Available Controllers:**
   ```csharp
   var controllers = StageControllerFactory.GetAvailableControllers();
   foreach (var controller in controllers)
   {
       Console.WriteLine($"{controller.DisplayName} - {controller.Description}");
   }
   ```

2. **Create and Test Mock Controller:**
   ```csharp
   // Create configuration
   var config = new StageInstance
   {
       HardwareType = StageHardwareType.Plugin,
       PluginName = "MockStage",
       PluginParameters = new Dictionary<string, object>
       {
           { "NumAxes", 3 },
           { "MinTravel", -50.0 },
           { "MaxTravel", 50.0 }
       }
   };
   
   // Create controller
   var controller = StageControllerFactory.CreateController(config);
   
   // Test operations
   controller.Connect();
   Console.WriteLine($"Connected: {controller.CheckConnected()}");
   Console.WriteLine($"Axes: {string.Join(", ", controller.GetAxesList())}");
   
   controller.MoveRelative(0, 10.0);
   Console.WriteLine($"Position: {controller.GetPosition(0)}");
   
   controller.Disconnect();
   ```

### Automated Testing

Run the integration tests:
```bash
dotnet test singalUI.Tests/singalUI.Tests.csproj --filter "MockPluginIntegrationTests"
```

Expected output:
```
✓ MockPlugin_LoadsSuccessfully
✓ MockPlugin_CanCreateController
✓ MockController_ConnectAndDisconnect_Works
✓ MockController_MoveRelative_UpdatesPosition
✓ MockController_ReferenceAxes_ResetsPositions
✓ MockController_GetTravelLimits_ReturnsConfiguredValues
```

## Verifying Plugin Loading

### Check Console Output

When the app starts, you should see:
```
[PluginLoader] Loading plugins from: C:\...\Plugins
[PluginLoader] Found 1 plugin DLL(s)
[PluginLoader] Loading: MockStageController.Plugin.dll
[PluginLoader] Assembly loaded: MockStageController.Plugin, Version=1.0.0.0
[PluginLoader] Found 1 plugin type(s)
[PluginLoader] Instantiating plugin type: MockStageController.Plugin.MockStageControllerPlugin
[MockStagePlugin] Checking compatibility... OK
[PluginLoader] ✓ Loaded plugin: Mock Stage Controller (Test) v1.0.0
[PluginLoader]   Manufacturer: Test Company
[PluginLoader]   Description: A mock stage controller for testing...
```

### Check Plugin Directory

```bash
ls singalUI/bin/Debug/net8.0/Plugins/
# Should show: MockStageController.Plugin.dll
```

## Troubleshooting

### Plugin Not Loading

1. **Check DLL exists:**
   ```bash
   ls singalUI/bin/Debug/net8.0/Plugins/MockStageController.Plugin.dll
   ```

2. **Check DLL naming:** Plugin DLLs must end with `.Plugin.dll`

3. **Check console for errors:**
   Look for `[PluginLoader] Failed to load plugin` messages

4. **Verify plugin implements interface:**
   The plugin class must implement `IStageControllerPlugin`

### Build Issues

1. **Clean and rebuild:**
   ```bash
   dotnet clean
   dotnet build
   ```

2. **Check project references:**
   The plugin project must reference `singalUI.csproj`

3. **Check target framework:**
   Both projects must target `net8.0`

## Creating Your Own Plugin

See `docs/PLUGIN_ARCHITECTURE_GUIDE.md` for detailed instructions on creating custom plugins.

Quick template:
```csharp
using singalUI.libs;

namespace MyController.Plugin;

public class MyControllerPlugin : IStageControllerPlugin
{
    public PluginMetadata Metadata => new()
    {
        Name = "MyController",
        DisplayName = "My Custom Controller",
        Manufacturer = "My Company",
        Version = "1.0.0",
        Description = "Custom stage controller",
        Author = "Your Name"
    };
    
    public bool IsCompatible() => true;
    
    public StageController CreateController(PluginConfiguration config)
    {
        return new MyController();
    }
}

public class MyController : StageController
{
    // Implement abstract methods...
}
```

## Next Steps

1. ✅ Build the solution
2. ✅ Run unit tests
3. ✅ Run the application and check console output
4. ✅ Test the mock controller manually
5. 📝 Create your own plugin for real hardware
6. 🚀 Deploy plugin DLL to factories

## Success Criteria

- [ ] Solution builds without errors
- [ ] All unit tests pass
- [ ] Plugin loads successfully (check console)
- [ ] Mock controller can connect/disconnect
- [ ] Mock controller can move axes
- [ ] Mock controller appears in available controllers list
