# 🔧 ISSUE IDENTIFIED - Plugin Architecture Problem

## What Went Wrong

When converting existing controllers to plugins, we created a **circular dependency**:

1. **PIController.Plugin** tries to use `PIController` class
2. **SigmakokiController.Plugin** tries to use `SigmakokiController` class
3. But these classes are in the **main application** (`singalUI.dll`)
4. The plugins reference the main app, but the classes aren't accessible at runtime

This causes the app to crash when loading these plugins.

## Why Mock Plugin Works

The **MockStageController.Plugin** works because:
- It contains its own complete controller implementation
- It doesn't depend on classes from the main app
- It's a self-contained plugin

## Two Solutions

### Solution 1: Keep Controllers in Main App (Recommended for Now)

**Pros:**
- Minimal changes
- Existing code keeps working
- Only new controllers need to be plugins

**Cons:**
- PI and Sigmakoki aren't truly plugins
- Can't update them independently

**Implementation:**
```
Main App (singalUI.dll)
├── PIController (built-in)
├── SigmakokiController (built-in)
└── Loads plugins from Plugins/
    └── MockStageController.Plugin.dll
    └── (future custom plugins)
```

### Solution 2: Move Controllers Completely to Plugins (Better Long-term)

**Pros:**
- Fully modular
- Can update any controller independently
- True plugin architecture

**Cons:**
- More work
- Need to copy controller code to plugins
- Larger plugin DLLs

**Implementation:**
```
Main App (singalUI.dll) - Core only
└── Loads plugins from Plugins/
    ├── PIController.Plugin.dll (contains full PI code)
    ├── SigmakokiController.Plugin.dll (contains full Sigmakoki code)
    └── MockStageController.Plugin.dll
```

## Quick Fix - Use Solution 1

Run this to disable the broken plugins:

```cmd
disable-broken-plugins.bat
```

Then run:
```cmd
dotnet run --project singalUI\singalUI.csproj --framework net8.0
```

You'll see:
```
[PluginLoader] Found 1 plugin DLL(s)
[PluginLoader] ✓ Loaded plugin: Mock Stage Controller (Test) v1.0.0
[StageControllerFactory] Plugin system initialized with 1 plugin(s)
```

## Proper Fix - Implement Solution 1

Update `StageControllerFactory.cs` to use built-in controllers for PI and Sigmakoki:

```csharp
public static StageController? CreateController(StageInstance configuration)
{
    // Use built-in controllers for PI and Sigmakoki
    switch (configuration.HardwareType)
    {
        case StageHardwareType.PI:
            return new PIController();
            
        case StageHardwareType.Sigmakoki:
            var controller = new SigmakokiController(
                ConvertToSKControllerType(configuration.SigmakokiController));
            controller.SetPort(configuration.SerialPortName);
            return controller;
            
        case StageHardwareType.SigmakokiRotationStage:
            return new Hsc103RotationStageController(
                configuration.SerialPortName,
                configuration.Hsc103AxisNumber,
                configuration.DegreesPerPulse);
    }
    
    // Try plugin for everything else
    if (_pluginLoader != null && !string.IsNullOrEmpty(configuration.PluginName))
    {
        var pluginConfig = new PluginConfiguration
        {
            Parameters = configuration.PluginParameters ?? new Dictionary<string, object>()
        };
        return _pluginLoader.CreateController(configuration.PluginName, pluginConfig);
    }
    
    return null;
}
```

## What Works Right Now

✅ **Plugin System Infrastructure** - Fully working
✅ **MockStageController.Plugin** - Works perfectly
✅ **Plugin Loading** - Works
✅ **Plugin Configuration** - Works
✅ **Tests** - Pass for mock plugin

❌ **PIController.Plugin** - Broken (circular dependency)
❌ **SigmakokiController.Plugin** - Broken (circular dependency)

## Recommended Next Steps

1. **Disable broken plugins** (run `disable-broken-plugins.bat`)
2. **Test with Mock plugin** to verify system works
3. **Decide on architecture:**
   - Keep PI/Sigmakoki built-in (easier)
   - OR move them fully to plugins (better long-term)
4. **Create new plugins** for future hardware

## Testing Mock Plugin

Once you disable the broken plugins:

```powershell
dotnet run --project singalUI\singalUI.csproj --framework net8.0
```

Then in your code:
```csharp
var config = new StageInstance
{
    HardwareType = StageHardwareType.Plugin,
    PluginName = "MockStage"
};

var controller = StageControllerFactory.CreateController(config);
controller.Connect();
// Use the mock controller
```

## Summary

The plugin **system** works perfectly. The issue is with **how we tried to convert existing controllers**. 

The Mock plugin proves the system works. For PI and Sigmakoki, we should either:
1. Keep them built-in (quick fix)
2. Copy their full implementation to plugins (proper fix)

Would you like me to implement the proper fix (Solution 1 - keep them built-in)?
