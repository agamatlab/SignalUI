# PI E-712 Support and Dynamic Axis Display Implementation

## Summary

Successfully implemented PI E-712 six-axis platform support and dynamic axis display in the camera view. The implementation includes:

1. **PI Controller Plugin**: Extracted PIController from the main application into a standalone plugin
2. **E-712 Support**: Full support for PI E-712 six-axis controllers (X, Y, Z, Rx, Ry, Rz)
3. **Dynamic Axis Display**: Camera view now dynamically shows axes based on connected stage controller

## Changes Made

### 1. PI Controller Plugin (PIController.StageController)

**Files Created:**
- `PIController.StageController/PIController.cs` - Complete PI controller implementation with E-712 support
- `PIController.StageController/PIControllerPlugin.cs` - Plugin entry point
- `PIController.StageController/PIController.StageController.csproj` - Plugin project file

**Key Features:**
- Automatic E-712 detection via IDN query
- Special handling for E-712 (skips EAX/qEAX commands)
- Support for 3-axis (C-863) and 6-axis (E-712) configurations
- USB connection with retry logic
- GCS2 protocol communication
- Automatic DLL copying to `StageControllers/PI/` directory

**E-712 Detection Logic:**
```csharp
if (idn.Contains("E-712", StringComparison.OrdinalIgnoreCase)) {
    isE712Controller = true;
    Console.WriteLine("[PI] Detected E-712 controller");
}
```

### 2. Dynamic Axis Display

**Files Created:**
- `singalUI/Models/AxisDisplayInfo.cs` - Model for axis display information

**Files Modified:**
- `singalUI/ViewModels/CameraSetupViewModel.cs`:
  - Added `ObservableCollection<AxisDisplayInfo> AxisDisplays` property
  - Added `UpdateDynamicAxisDisplays()` method to populate axes based on connected controllers
  - Integrated with existing `PostPosePreviewTexts()` method

- `singalUI/Views/CameraSetupView.axaml`:
  - Replaced hardcoded 6-axis grid with dynamic `ItemsControl`
  - Uses `WrapPanel` for flexible axis layout
  - Maintains same visual style (Consolas font, dark theme)

**How It Works:**
1. When pose data is updated, the ViewModel queries all connected stage controllers
2. Calls `GetAxesList()` on each controller to get available axes
3. Creates/updates `AxisDisplayInfo` objects for each axis
4. UI automatically updates via data binding to show only available axes

**Supported Configurations:**
- **Mock Controller**: 3 axes (X, Y, Z)
- **PI E-712**: 6 axes (X, Y, Z, Rx, Ry, Rz)
- **PI C-863**: 3 axes (X, Y, Z)
- **Sigmakoki**: Variable axes depending on configuration
- **Default**: Shows all 6 axes if no controller connected

### 3. Main Application Updates

**Files Modified:**
- `singalUI/libs/StageController.cs`:
  - Removed PIController class (now in plugin)
  - Kept abstract base class and other controllers

- `singalUI/Services/StageControllerFactory.cs`:
  - Already configured to load PI as plugin
  - No changes needed

- `singalUI/App.axaml.cs`:
  - Already initializes plugin system on startup
  - No changes needed

## Build Structure

```
StageControllers/
├── Mock/
│   └── MockStageController.Plugin.dll
└── PI/
    ├── PIController.StageController.dll
    ├── PI_GCS2_DLL.dll
    └── PI_GCS2_DLL_x64.dll
```

## Testing

### Build Status
- ✅ Main application builds successfully (0 errors, 96 warnings - all pre-existing)
- ✅ PI plugin builds successfully
- ✅ Mock plugin builds successfully
- ✅ All plugins copied to correct directories

### Plugin Loading
The application will automatically:
1. Scan `StageControllers/` directory on startup
2. Load all plugin DLLs
3. Register PI controller as available hardware type
4. Create PI controller instances when selected

### Dynamic Axis Display
The camera view will:
1. Show only axes available on connected controllers
2. Update automatically when controllers connect/disconnect
3. Format linear axes as "X (mm): 12.345678900"
4. Format rotational axes as "Rx: 45° 30' 15.5""
5. Show "-" when no pose data available

## Usage

### Connecting to PI E-712
1. Launch the application
2. Go to Configuration tab
3. Select "PI (Physik Instrumente)" as hardware type
4. Click "Connect"
5. Application will detect E-712 and configure 6 axes automatically

### Viewing Axes in Camera Tab
1. Connect to a stage controller
2. Navigate to Camera Setup tab
3. Bottom of camera preview shows available axes dynamically
4. Axes update in real-time as stage moves

## Technical Details

### PI E-712 Specifics
- **Axes**: X, Y, Z (linear, mm), Rx, Ry, Rz (rotational, degrees)
- **Communication**: USB via GCS2 DLL
- **Special Handling**: E-712 doesn't support EAX/qEAX commands
- **Units**: Millimeters for linear, degrees for rotational

### Plugin Architecture
- **Interface**: `IStageControllerPlugin`
- **Loading**: Dynamic assembly loading via `Assembly.LoadFrom()`
- **Discovery**: Reflection to find plugin classes
- **Isolation**: Each plugin in separate directory with dependencies

### Axis Display Algorithm
1. Query all connected `StageWrapper` instances
2. Call `GetAxesList()` on each controller
3. Merge axes into unique set
4. Order by standard sequence (X, Y, Z, Rx, Ry, Rz)
5. Create/update UI elements via ObservableCollection
6. Bind values from pose estimation data

## Future Enhancements

Potential improvements:
- Add axis enable/disable toggles
- Show axis limits and current percentage
- Add axis-specific color coding
- Display velocity and acceleration
- Show homing/reference status per axis
- Add axis-specific error indicators

## Files Summary

**New Files (3):**
- PIController.StageController/PIController.cs (688 lines)
- PIController.StageController/PIControllerPlugin.cs (30 lines)
- singalUI/Models/AxisDisplayInfo.cs (35 lines)

**Modified Files (3):**
- singalUI/ViewModels/CameraSetupViewModel.cs (+95 lines)
- singalUI/Views/CameraSetupView.axaml (-30 lines, +15 lines)
- singalUI/libs/StageController.cs (-688 lines)

**Total Changes:**
- Lines added: ~863
- Lines removed: ~718
- Net change: +145 lines

## Conclusion

The implementation successfully:
✅ Adds PI E-712 six-axis platform support as a plugin
✅ Maintains backward compatibility with existing controllers
✅ Provides dynamic axis display in camera view
✅ Follows existing architecture patterns
✅ Builds without errors
✅ Ready for testing with actual hardware

The system is now ready to support PI E-712 controllers and will automatically adapt the UI to show the correct number of axes for any connected stage controller.
