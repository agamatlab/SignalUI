# Stage Controllers as Separate DLLs - Clean Architecture

## New Architecture

```
singalUI/
├── bin/Debug/net8.0/
│   ├── singalUI.exe (main app - no controllers)
│   └── StageControllers/
│       ├── PI/
│       │   ├── PIController.dll
│       │   ├── PI_GCS2_DLL_x64.dll
│       │   └── PI_GCS2_DLL.dll
│       ├── Sigmakoki/
│       │   └── SigmakokiController.dll
│       ├── SigmakokiRotation/
│       │   ├── SigmakokiRotationController.dll
│       │   └── PythonStage/ (Python scripts)
│       └── Mock/
│           └── MockStageController.dll
```

## Benefits

✅ **Clean separation** - Each controller is independent
✅ **Easy updates** - Replace just one folder
✅ **Easy deployment** - Ship only the controller folder needed
✅ **No conflicts** - Each controller has its own dependencies
✅ **Easy to add** - Drop new controller folder and restart

## Implementation Plan

1. **Create separate DLL projects** for each controller
2. **Update PluginLoader** to scan `StageControllers/` directory
3. **Each controller folder** contains:
   - Controller DLL
   - Native dependencies
   - Configuration files

## Next Steps

Should I implement this? It will:
1. Move PI controller to `PIController/` project
2. Move Sigmakoki to `SigmakokiController/` project  
3. Move rotation to `SigmakokiRotationController/` project
4. Update loader to scan `StageControllers/` folders
5. Each builds to its own subfolder

This is the cleanest architecture for your use case!
