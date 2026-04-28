# Plugin System - Quick Reference Card

## 🚀 Quick Start (3 Commands)

```bash
# 1. Build everything
dotnet build singalUI.sln

# 2. Run tests
dotnet test singalUI.Tests/singalUI.Tests.csproj --filter "Plugin"

# 3. Run app and check console for plugin loading
dotnet run --project singalUI/singalUI.csproj
```

## 📁 Key Files

| File | Purpose |
|------|---------|
| `singalUI/libs/IStageControllerPlugin.cs` | Plugin interface |
| `singalUI/Services/PluginLoader.cs` | Loads plugins from DLLs |
| `singalUI/Services/StageControllerFactory.cs` | Creates controllers |
| `MockStageController.Plugin/` | Example plugin |
| `singalUI.Tests/PluginSystemTests.cs` | Unit tests |

## 🔌 Using a Plugin

```csharp
// 1. Configure
var config = new StageInstance
{
    HardwareType = StageHardwareType.Plugin,
    PluginName = "MockStage",
    PluginParameters = new Dictionary<string, object>
    {
        { "NumAxes", 3 }
    }
};

// 2. Create
var controller = StageControllerFactory.CreateController(config);

// 3. Use
controller.Connect();
controller.MoveRelative(0, 10.0);
controller.Disconnect();
```

## 🛠️ Creating a Plugin

```csharp
// 1. Implement interface
public class MyPlugin : IStageControllerPlugin
{
    public PluginMetadata Metadata => new()
    {
        Name = "MyController",
        DisplayName = "My Controller",
        Version = "1.0.0"
    };
    
    public bool IsCompatible() => true;
    
    public StageController CreateController(PluginConfiguration config)
    {
        return new MyController();
    }
}

// 2. Implement controller
public class MyController : StageController
{
    public override void Connect() { /* ... */ }
    public override void MoveRelative(int axis, double amount) { /* ... */ }
    // ... implement other methods
}
```

## 📦 Deployment

### Standard Deployment
Ship entire app (~200 MB)

### Plugin Deployment
1. Build plugin: `dotnet build MyPlugin.csproj`
2. Ship DLL: `MyPlugin.Plugin.dll` (~2-5 MB)
3. Factory copies to: `Plugins/` folder
4. Restart app → Plugin loads automatically

## ✅ Verification Checklist

- [ ] Build succeeds without errors
- [ ] Tests pass (11 tests)
- [ ] Console shows: `[PluginLoader] ✓ Loaded plugin: Mock Stage Controller`
- [ ] Plugin DLL exists: `singalUI/bin/Debug/net8.0/Plugins/MockStageController.Plugin.dll`
- [ ] Mock controller can connect/move/disconnect

## 🐛 Common Issues

| Problem | Solution |
|---------|----------|
| Plugin not loading | Check DLL name ends with `.Plugin.dll` |
| Build errors | `dotnet clean && dotnet build` |
| Tests fail | Ensure plugin DLL is in Plugins folder |
| No console output | Check `App.axaml.cs` calls `InitializePlugins()` |

## 📚 Documentation

- **Complete Guide:** `docs/PLUGIN_ARCHITECTURE_GUIDE.md`
- **Testing Guide:** `PLUGIN_TESTING_GUIDE.md`
- **Implementation Summary:** `PLUGIN_IMPLEMENTATION_SUMMARY.md`
- **DI & Exceptions:** `docs/DEPENDENCY_INJECTION_AND_EXCEPTIONS.md`

## 🎯 Success Criteria

✅ **Ready to Test:**
- Plugin system implemented
- Mock plugin created
- Unit tests written
- Documentation complete

🚀 **Ready for Production:**
- Real hardware plugin created
- Tested with actual hardware
- Deployed to test factory
- Factory confirms plugin works

## 💡 Key Benefits

| Before | After |
|--------|-------|
| Rebuild entire app (200 MB) | Ship plugin DLL (2-5 MB) |
| Days to deploy | Minutes to deploy |
| Recompile + test everything | Test only new plugin |
| Ship to all factories | Ship to specific factory |

---

**Next Step:** Run `dotnet build singalUI.sln` and watch the magic happen! ✨
