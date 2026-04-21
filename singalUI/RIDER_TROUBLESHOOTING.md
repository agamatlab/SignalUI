# Rider Troubleshooting Guide

## Issue: Camera Preview Not Working in Rider

### Root Cause: Working Directory Path Mismatch (mitigated in app)

**Update:** `MatrixVisionCameraService` now resolves `mvGenTLProducer.cti` from **`AppContext.BaseDirectory`** (output / publish folder) and prepends `MatrixVision\` + app root to `PATH` when unset. Rider “working directory” is less critical than before; if issues persist, check `%LocalAppData%\singalUI\camera_log.txt`.

**The Problem (historical):**
- WSL uses Linux paths: `/mnt/c/Users/agama/...`
- Rider/Windows uses Windows paths: `C:\Users\agama\...`
- Older builds relied on `Environment.CurrentDirectory` for the CTI path
- When running from Rider, the SDK could fail to find `mvGenTLProducer.cti` or DLLs

### Error Messages Seen:

```
Communication Error: Error occurred at the time of the device open.
```

This happens because the mvIMPACT Acquire SDK can't locate its GenTL producer file.

---

## Solutions

### Solution 1: Enhanced Logging

The application now logs diagnostic information:

```
=== [MatrixVision] Working Directory: [current directory]
=== [MatrixVision] Current Directory: [current directory]
=== [MatrixVision] CTI file exists: True/False ([path to mvGenTLProducer.cti])
=== [MatrixVision] CTI files in directory: [list of .cti files]
=== [MatrixVision] Device count: [number]
=== [MatrixVision] Attempting to open device ===
=== [MatrixVision] Device opened successfully / ERROR: [error details]
```

### Solution 2: Verify File Locations

**Files that MUST be present in the output directory:**

| File | Location |
|------|----------|
| mv.impact.acquire.dll | `bin\Debug\net8.0\` (copied by build) |
| mvGenTLProducer.cti | `bin\Debug\net8.0\` (copied by build) |
| PI_GCS2_DLL.dll | `bin\Debug\net8.0\` (copied by build) |
| PI_GCS2_DLL_64.dll | `bin\Debug\net8.0\` (copied by build) |

### Solution 3: Check Working Directory in Rider

When you run from Rider, the working directory is typically the project root, not `bin\Debug\net8.0\`.

**To check:**
1. Run the application from Rider
2. Check the console output for "Working Directory" and "CTI file exists"
3. If CTI file exists is `False`, the files are not in the expected location

**Possible fixes in Rider:**
- Change "Working directory" in Run Configuration to point to `bin\Debug\net8.0\`
- OR Use "Publish" profile which ensures all files are in the output directory

### Solution 4: Verify Camera Connection

The camera serial is set to auto-detect (empty string), so it should connect to the first available camera (GX000585).

**Console output when working:**
```
=== [MatrixVision] Device count: 1 ===
=== [MatrixVision] Device 0: Serial=GX000585, Family=mvBlueCOUGAR, Product=mvBlueCOUGAR-X124G ===
=== [MatrixVision] Device opened successfully ===
=== CAPTURE: Starting acquisition ===
```

---

## Stage Control Changes

**IMPORTANT:** The application now uses `StageManager` instead of gRPC for stage control.

### How Stage Control Works Now:

1. **Direct USB/Serial Connection** - No gRPC server needed
2. **StageManager** - Global singleton managing all stage wrappers
3. **StageWrapper** - Configuration + Controller for each stage

### Connecting to PI Stage:

1. Run the application from Rider
2. Go to stage configuration
3. Select "PI (Physik Instrumente)" as hardware type
4. Click "Connect" button
5. Check console for connection messages:
   ```
   === [MatrixVision] Working Directory: [...]
   Found: X USB controllers, connecting to the first one...
   Successfully connected to PI controller
   ```

---

## Testing Checklist

Run from Rider and verify:

- [ ] Console shows correct working directory (should contain `bin\Debug\net8.0\`)
- [ ] CTI file exists: True
- [ ] Camera detected (Device count: 1)
- [ ] Camera frame capture starts (=== CAPTURE: Starting acquisition ===)
- [ ] Preview displays frames

---

## Modified Files Summary

| File | Changes |
|------|----------|
| `App.axaml.cs` | Removed `GrpcStageService`, using `StageManager` for direct USB/serial control |
| `CameraSetupViewModel.cs` | Replaced gRPC calls with `StageManager` calls, added axis conversion helpers |
| `CalibrationSetupViewModel.cs` | Replaced gRPC calls with `StageManager` calls, added axis conversion helpers |
| `MatrixVisionCameraService.cs` | Enhanced logging: working directory, CTI file search, device open errors |
