# Stage Movement Investigation - Progress Update

**Date:** 2025-02-19
**Status:** INVESTIGATING - Stage commands accepted but no physical movement

---

## Current Problem

The PI E-712 Hexapod (Stage 1 / Linear 1) accepts movement commands and reports success, but **the stage does NOT physically move**.

### Key Observations:
- MOV commands return `result: 1` (success)
- Position values in software change
- **No actual physical movement occurs**
- Axis state shows: `ServoOn=1, Enabled=0, On=1`

---

## Root Cause Analysis

### The `Enabled=0` Issue

The axes show `Enabled=0` which means they are NOT physically enabled for movement.

### EAX Command Fails

```
[PI] EAX(1) failed: 2 - Unknown command
[PI] EAX(2) failed: 2 - Unknown command
[PI] EAX(3) failed: 2 - Unknown command
[PI] EAX(4) failed: 2 - Unknown command
[PI] EAX(5) failed: 2 - Unknown command
[PI] EAX(6) failed: 2 - Unknown command
```

**Error 2 = "Unknown command"** - The E-712 hexapod controller does NOT support the EAX (Enable Axis) command!

### HIN Command Also Fails

```
[PI] HIN result: 0
```

HIN (Hexapod Initialize) also returns 0 (failure).

---

## Current Error State (Latest Output)

```
[PI] MOV command: 3, target=0.0010 mm
[PI] MOV result: 0 (1=success, 0=failure)
[PI] MOV failed! Error -1: Error during com operation (could not be specified)
[StageWrapper] Stage 1: Move completed. New pos: 0.0000 mm, actual delta: -474.2155 mm
```

**MOV now returns failure (0) with Error -1** - "Error during com operation"

---

## What We've Tried

### 1. Axis Enabling Attempts
- **EAX (Enable Axis)** - NOT SUPPORTED by E-712 (returns Error 2: Unknown command)
- **HIN (Hexapod Initialize)** - Returns 0 (failure)
- Individual axis enable - Same error

### 2. Movement Commands
- MOV - Sometimes returns success (1), sometimes failure (0)
- MVR (relative move) - Same behavior
- Position polling - Returns values but may be cached/simulated

### 3. Connection Verification
- Controller connects successfully
- Controller ID is valid
- qPOS reads positions (but may be commanded positions, not actual)

---

## Controller Information

- **Model:** PI E-712 Hexapod Controller
- **Connected:** Yes
- **Axes:** 6 (X, Y, Z, Rx, Ry, Rz mapped to channels 1-6)
- **GCS DLL:** PI_GCS2_DLL.dll (loaded successfully)

---

## Code Changes Made

### StageController.cs Modifications:

1. Added detailed logging for MOV commands
2. Added axis state checking (ServoOn, Enabled, On)
3. Added EAX enable attempts with error reporting
4. Added HIN (Hexapod Initialize) attempt
5. Added position verification after moves
6. Added error code translation for failures

### Latest Axis State Check:
```
Axis state - ServoOn=1, Enabled=0, On=1
```
- Servo is ON
- Axis is NOT Enabled (the problem)
- On Target = 1

---

## What Needs to Be Investigated

### 1. E-712 Specific Enable Sequence
The E-712 hexapod may require a different command or sequence to enable axes:
- Check if `SVO` (Servo) needs specific parameters
- Check if there's a hexapod-specific enable command
- Check if the hexapod needs to be referenced/homed first

### 2. Controller Documentation Needed
Need to find PI E-712 specific documentation for:
- How to properly enable axes for movement
- Required initialization sequence
- Any safety interlocks that prevent movement

### 3. Alternative Commands to Try
- **INI** - Initialize controller
- **RBT** - Reboot (if needed)
- **GOX** - Go to offset position (hexapod specific?)
- **Check the GCS command reference for E-712 specific commands**

### 4. Possible Hardware Issues
- Safety interlock engaged?
- Emergency stop active?
- Power stage not enabled?
- Hexapod needs homing/referencing?

---

## Log File Locations

- Latest run output: `/tmp/claude-1000/-mnt-c-Users-agama-Documents-signalUI-updatedSignalUI/tasks/b0bcbd7.output`
- Previous attempts: Various task output files in same directory

---

## Next Steps

1. **Use E-712 command path (implemented in code):**
   - Skip `EAX` / `qEAX` / `HIN` for E-712
   - Use `qFRF` to verify reference state
   - Use `SVO` + `FRF` + `IsControllerReady` for reference flow
2. **Initialize controller + stage in PI software first** (PIMikroMove), as required by PI sample readme
3. **Re-test with a visible move size** (e.g. 0.01 mm) to confirm physical motion
4. **Verify E-stop / interlocks / amplifier state** on hardware if still no motion
5. **If MOV still returns communication errors**, collect fresh logs and test with PI sample app to isolate HW vs app behavior

---

## Confirmed Findings From PI Reference (`C:\Users\agama\Desktop\PI-Software-Suite-C-990.CD1`)

### 1) E-712 sample does not use EAX/qEAX

- Source: `Development/C++/Samples/E-712/ReferenceSample/ReferenceSample.cpp`
- Sequence in sample:
  - Connect
  - `qIDN`
  - `SVO` (servo state call)
  - `FRF`
  - wait with `IsControllerReady`
  - verify with `qFRF`

No `EAX` / `qEAX` used in E-712 sample path.

### 2) PI readme explicitly requires prior initialization in PI tools

- Source: `Development/ReadMe.txt`
- Source: `Development/C++/Samples/E-712/ReadMe.txt`

Controller and positioner must be initialized with **PIMikroMove** before running samples.

### 3) Why "Enabled=0" was misleading in our logs

On E-712, `qEAX` can fail as unsupported (`Unknown command`), leaving local value arrays at 0.
That 0 was being logged as disabled state even though command support was absent.

### 4) Code changes applied in this repo

- File: `singalUI/libs/StageController.cs`
- Added E-712 detection from `qIDN`
- For E-712:
  - skip `EAX/qEAX/HIN` calls
  - avoid logging fake `Enabled=0` status
  - reference using `SVO` + `FRF` + `IsControllerReady`
  - use `qFRF` for referenced checks
- Also hardened axis parsing from `qSAI` to avoid malformed axis lists.

---

## Error Codes Reference

| Code | Meaning |
|------|---------|
| -1 | Error during com operation (could not be specified) |
| 2 | Unknown command |
| 0 | Failure (for command return values) |
| 1 | Success (for command return values) |

---

## Questions to Answer

1. Does the E-712 require a specific initialization sequence?
2. Is there a hexapod-specific enable command different from EAX?
3. Does the hexapod need to be referenced before enabling?
4. Are there any error flags on the controller itself?
5. Is there a hardware enable switch or interlock?
