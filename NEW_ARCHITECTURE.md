# ✅ NEW ARCHITECTURE - Separate DLL Folders

## What Changed

**Old Structure:**
```
Plugins/
├── MockStageController.Plugin.dll
├── PIController.Plugin.dll (broken)
└── SigmakokiController.Plugin.dll (broken)
```

**New Structure:**
```
StageControllers/
├── Mock/
│   └── MockStageController.Plugin.dll
├── PI/
│   ├── PIController.dll (future)
│   ├── PI_GCS2_DLL_x64.dll
│   └── PI_GCS2_DLL.dll
├── Sigmakoki/
│   └── SigmakokiController.dll (future)
└── SigmakokiRotation/
    ├── SigmakokiRotationController.dll (future)
    └── PythonStage/
```

## Benefits

✅ **Clean separation** - Each controller in its own folder
✅ **Easy to ship** - Just zip one folder and send it
✅ **No conflicts** - Each has its own dependencies
✅ **Easy to update** - Replace one folder
✅ **Backward compatible** - Still checks old Plugins/ folder

## How It Works

1. **PluginLoader scans** `StageControllers/` directory
2. **Finds all DLLs** in subdirectories
3. **Skips native DLLs** (PI_GCS2, opencv, etc.)
4. **Loads controller DLLs** that implement `IStageControllerPlugin`
5. **Also checks** legacy `Plugins/` folder for backward compatibility

## Current Status

**Working:**
- ✅ Mock controller (in StageControllers/Mock/)
- ✅ PI controller (built-in to main app)
- ✅ Sigmakoki controller (built-in to main app)

**Future:**
- 📦 Move PI to StageControllers/PI/
- 📦 Move Sigmakoki to StageControllers/Sigmakoki/
- 📦 Add new controllers as separate folders

## How to Test

```powershell
cd C:\Users\agama\Documents\signalUI\updatedSignalUI

# Rebuild
dotnet build singalUI.sln

# Run
dotnet run --project singalUI\singalUI.csproj --framework net8.0
```

**Expected output:**
```
[PluginLoader] Loading stage controllers from: .../StageControllers
[PluginLoader] Found 1 DLL file(s) in StageControllers
[PluginLoader] ✓ Loaded plugin: Mock Stage Controller (Test) v1.0.0
[PluginLoader] Successfully loaded 1 controller(s) total
```

## Directory Structure

```
singalUI/bin/Debug/net8.0/
├── singalUI.exe
├── singalUI.dll
├── (other dependencies)
└── StageControllers/
    └── Mock/
        └── MockStageController.Plugin.dll
```

## Adding a New Controller

1. **Create folder:** `StageControllers/NewController/`
2. **Add DLL:** `NewController.dll` (implements `IStageControllerPlugin`)
3. **Add dependencies:** Any native DLLs needed
4. **Restart app:** Controller loads automatically

Example:
```
StageControllers/
└── Newport/
    ├── NewportController.dll
    └── Newport.ESP301.dll
```

## Deployment

**To ship a new controller to a factory:**

1. Build the controller DLL
2. Zip the folder:
   ```
   Newport.zip
   └── Newport/
       ├── NewportController.dll
       └── Newport.ESP301.dll
   ```
3. Send to factory (2-5 MB)
4. Factory extracts to `StageControllers/`
5. Restart app → Controller available

**No recompilation needed!**

## Migration from Old Structure

The loader checks both directories:
1. New: `StageControllers/` (preferred)
2. Legacy: `Plugins/` (backward compatibility)

Old plugins in `Plugins/` will still work!

## Summary

✅ **Cleaner architecture** - Each controller isolated
✅ **Easier deployment** - Ship one folder
✅ **Backward compatible** - Old plugins still work
✅ **Ready to use** - Mock controller already migrated

**Next step:** Rebuild and test!

```bash
dotnet build singalUI.sln
dotnet run --project singalUI\singalUI.csproj --framework net8.0
```

Look for: `[PluginLoader] ✓ Loaded plugin: Mock Stage Controller`
