# Sigmakoki/Optosigma Dynamic Axis Display Fix

## Issue

When connecting to an Optosigma/Sigmakoki stage controller, the dynamic axis display in the Camera Setup view was not working correctly. The axes would not appear, and the display would fall back to showing the default 6 axes (X, Y, Z, RX, RY, RZ) even though only 3 axes were connected.

## Root Cause

**Axis Name Mismatch:**
- The `SigmakokiController` was returning axis names as **"1", "2", "3"** (numeric strings)
- The `UpdateDynamicAxisDisplays()` method in `CameraSetupViewModel` was expecting **"X", "Y", "Z"** (letter strings)
- This mismatch caused the axis detection to fail

**Code Location:**
```csharp
// OLD CODE in StageController.cs line 273:
axes[i] = (i + 1).ToString();  // Returns "1", "2", "3"

// Expected by CameraSetupViewModel.cs line 714:
availableAxes.Add(axis.Trim().ToUpper());  // Expects "X", "Y", "Z"
```

## Solution

Changed the `InitializeAxes()` method in `SigmakokiController` to return standard axis names instead of numeric strings.

**File Modified:** `singalUI/libs/StageController.cs`

**Change:**
```csharp
private void InitializeAxes()
{
    // SIGMA KOKI axes - use standard axis names for compatibility with UI
    int maxAxes = GetMaxAxes();
    axes = new string[maxAxes];
    
    // Map to standard axis names: X, Y, Z, U (4th axis if available)
    string[] standardNames = { "X", "Y", "Z", "U" };
    for (int i = 0; i < maxAxes; i++)
    {
        axes[i] = i < standardNames.Length ? standardNames[i] : (i + 1).ToString();
    }
    Log($"[Sigmakoki] Axes initialized: [{string.Join(", ", axes)}]");
}
```

## Impact

**What Changed:**
- `GetAxesList()` now returns `["X", "Y", "Z"]` instead of `["1", "2", "3"]`
- Dynamic axis display now correctly detects Sigmakoki axes
- Camera view shows only 3 axes when Sigmakoki is connected

**What Stayed the Same:**
- All movement commands still use the correct axis indices (0, 1, 2)
- Protocol commands remain unchanged (M:+100, M:,+100, M:,,+100)
- No changes to serial communication or command format
- Backward compatible with existing code

## Testing

**Before Fix:**
1. Connect to Sigmakoki HSC-103 controller
2. Navigate to Camera Setup tab
3. **Problem:** Display shows 6 axes (X, Y, Z, RX, RY, RZ) with no values
4. Axes don't update with stage position

**After Fix:**
1. Connect to Sigmakoki HSC-103 controller
2. Navigate to Camera Setup tab
3. **Expected:** Display shows only 3 axes (X, Y, Z)
4. Axes update correctly with stage position
5. Values shown in millimeters (mm)

## Controller Type Support

The fix supports all Sigmakoki controller types:

| Controller Type | Axes | Display Names |
|----------------|------|---------------|
| GIP_101B       | 1    | X             |
| GSC01          | 1    | X             |
| SHOT_302GS     | 2    | X, Y          |
| HSC_103        | 3    | X, Y, Z       |
| SHOT_304GS     | 4    | X, Y, Z, U    |
| Hit_MV         | 4    | X, Y, Z, U    |
| PGC_04         | 4    | X, Y, Z, U    |

## Build Status

✅ Build successful (0 errors, 96 warnings - all pre-existing)
✅ No breaking changes to existing functionality
✅ Ready for testing with actual hardware

## Next Steps

1. Run the application: `NanoMeas.exe`
2. Connect to Sigmakoki/Optosigma controller
3. Verify that Camera Setup view shows correct number of axes
4. Test stage movements to ensure commands still work correctly
5. Verify position values update in real-time

## Related Files

- `singalUI/libs/StageController.cs` - Sigmakoki controller implementation
- `singalUI/ViewModels/CameraSetupViewModel.cs` - Dynamic axis display logic
- `singalUI/Views/CameraSetupView.axaml` - Axis display UI
- `singalUI/Models/AxisDisplayInfo.cs` - Axis data model
