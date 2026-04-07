# Camera Troubleshooting Guide

## Known Issue: Camera Preview Not Displaying

### Problem
Camera preview shows no image, even though the camera is connected.

### Root Cause
**Camera Serial Number Mismatch**

The `MatrixVisionCameraService` was hardcoded to look for a camera with serial number `GX000597`, but the actual connected camera had a different serial number (`GX000585`).

**Location:** `singalUI/Services/Services/MatrixVisionCameraService.cs:39`

```csharp
// OLD - Hardcoded serial (WRONG)
public string CameraSerial { get; set; } = "GX000597";
```

### Solution
Changed the camera serial to an empty string to automatically use the first available detected camera:

```csharp
// NEW - Uses first available camera
public string CameraSerial { get; set; } = "";  // Empty = use first available camera
```

### How to Verify

Run the application and check the console output:

**Before Fix:**
```
=== [MatrixVision] Device count: 0 ===
=== [MatrixVision] ERROR: No devices found! ===
```

**After Fix (Working):**
```
=== [MatrixVision] Device count: 1 ===
=== [MatrixVision] Device 0: Serial=GX000585, Family=mvBlueCOUGAR, Product=mvBlueCOUGAR-X124G ===
=== CAPTURE: Starting acquisition ===
=== CAPTURE: Frame 30 (seq: 30) ===
=== CAPTURE: Frame 60 (seq: 60) ===
```

---

## Camera Specifications

| Property | Value |
|----------|-------|
| Serial | GX000585 |
| Family | mvBlueCOUGAR |
| Product | mvBlueCOUGAR-X124G |
| Interface | GigE |
| Max Resolution | 2448 x 2048 |

---

## Troubleshooting Steps

If camera preview is not working:

1. **Check Camera Connection**
   - Ensure camera is powered on (LED indicators)
   - Verify Ethernet cable is connected to GigE port
   - Check network adapter configuration for GigE

2. **Verify SDK Installation**
   - Ensure mvIMPACT Acquire SDK is installed at `C:\Program Files\MATRIX VISION\`
   - Verify `mv.impact.acquire.dll` is present

3. **Test with Matrix Vision Tools**
   - Run `wxPropView.exe` from `C:\Program Files\MATRIX VISION\mvIMPACT Acquire\bin\`
   - Check if camera appears in device list

4. **Check Console Output**
   - Look for `=== [MatrixVision] Device count: X ===`
   - If count is 0, no cameras detected

5. **Try Auto-Detect**
   - Set `CameraSerial = ""` to use first available camera
   - Rebuild and run application

---

## Published exe (`SignalUI/out`) and GigE (RJ45)

The app resolves `mvGenTLProducer.cti` from **`AppContext.BaseDirectory`** (the folder containing the published app), not from the process “Start in” folder, so shortcuts no longer need a special working directory for the CTI.

**Build / copy checklist (full folder deploy):**

| Item | Expected location |
|------|-------------------|
| `*.exe` | Publish root |
| `mvGenTLProducer.cti` | Publish root |
| `mv.impact.acquire.dll` | Publish root |
| `MatrixVision\**\mv*.dll` | Copied from your Matrix Vision install at build time |

Build requires mvIMPACT Acquire at `C:\Program Files\MATRIX VISION\mvIMPACT Acquire\` (see `singalUI.csproj`). Publish:

```powershell
dotnet publish singalUI\singalUI.csproj -c Release -o ..\out
.\tools\VerifyPublishManifest.ps1 -OutDir ..\out
```

**Runtime log:** `%LocalAppData%\singalUI\camera_log.txt` includes `moduleBase`, `mvGenTLProducer.cti found`, and `deviceCount` after `updateDeviceList`.

**wxPropView comparison (same PC, same Ethernet NIC):** Run `wxPropView.exe` under `C:\Program Files\MATRIX VISION\mvIMPACT Acquire\bin\`. If the device list is empty there too, fix subnet, Windows Firewall, or the GigE adapter before debugging the Avalonia app.

---

## Related Files

- `singalUI/Services/Services/MatrixVisionCameraService.cs` - Camera service implementation
- `singalUI/ViewModels/CameraSetupViewModel.cs` - Camera UI view model
- `singalUI/Views/CameraSetupView.axaml` - Camera preview UI
