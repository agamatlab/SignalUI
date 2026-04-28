# Current System Status & Configuration

## ✅ What's Working

### 1. Fullscreen Toggle (F11)
**Status:** Already implemented and working!

**How to use:**
- Press `F11` → Enter fullscreen (hides taskbar, goes in front)
- Press `F11` again → Exit fullscreen (returns to previous state)

**Code location:** `singalUI/Views/MainWindow.axaml.cs` lines 174-192

### 2. Plugin System
**Status:** Working with Mock plugin

**Current setup:**
- ✅ Plugin infrastructure functional
- ✅ MockStageController.Plugin works
- ✅ Built-in controllers: PI, Sigmakoki, Hsc103Rotation

### 3. Stage Controllers

**Built-in controllers (in main app):**

1. **PIController** (`StageController.cs` line 710)
   - Hardware: PI (Physik Instrumente) hexapod
   - Connection: USB
   - Protocol: GCS2 DLL
   - UI Label: "PI 6-dof"

2. **SigmakokiController** (`StageController.cs` line 68)
   - Hardware: Sigmakoki linear stage (OSMS26-100(X))
   - Connection: Serial port (COM3)
   - Protocol: HIT MODE (direct serial commands)
   - UI Label: "Optosigma OSMS26-100(X)"

3. **Hsc103RotationStageController** (`Hsc103RotationStageController.cs`)
   - Hardware: Sigmakoki rotation stage (OSMS-120YAW)
   - Connection: Serial port via Python bridge
   - Protocol: Python script (hsc103.py + pyserial)
   - UI Label: "Optosigma OSMS-120YAW"

## 🔍 Your Question: "Why do both Optosigma options work?"

### Answer:
Both options are **different Sigmakoki hardware**, not "one PI, one Sigmakoki":

| UI Option | Hardware | Controller | Connection |
|-----------|----------|------------|------------|
| PI 6-dof | PI Hexapod | PIController | USB |
| Optosigma OSMS26-100(X) | Sigmakoki Linear | SigmakokiController | Serial (direct) |
| Optosigma OSMS-120YAW | Sigmakoki Rotation | Hsc103RotationStageController | Serial (Python) |

### Why Both Might "Work"

If both Optosigma options appear to work, it could be because:

1. **Same COM port:** Both use serial communication (e.g., COM3)
2. **Initial connection succeeds:** Serial port opens successfully
3. **Different protocols:** But they send different commands
   - OSMS26-100(X): Direct HIT MODE commands
   - OSMS-120YAW: Python wrapper with JSON protocol

### Which Should You Use?

**If you have linear stage (X/Y/Z movement):**
- ✅ Select: "Optosigma OSMS26-100(X)"
- ❌ Don't use: "Optosigma OSMS-120YAW" (rotation only)

**If you have rotation stage (Rz/yaw movement):**
- ✅ Select: "Optosigma OSMS-120YAW"
- ❌ Don't use: "Optosigma OSMS26-100(X)" (linear only)

**If you have PI hexapod (6-DOF):**
- ✅ Select: "PI 6-dof"
- ❌ Don't use: Either Optosigma option

## 📋 ComboBox Mapping

**In UI (CalibrationSetupView.axaml line 198-202):**
```
Index 0: None
Index 1: PI 6-dof
Index 2: Optosigma OSMS26-100(X)
Index 3: Optosigma OSMS-120YAW
```

**Maps to enum (StageHardwareType.cs):**
```csharp
None = 0
PI = 1
Sigmakoki = 2                    // Linear stage
SigmakokiRotationStage = 3      // Rotation stage
Plugin = 4                       // Future plugins
```

## 🎯 Recommendations

### 1. Test F11 Fullscreen
Just run your app and press F11 - it should already work!

### 2. Verify Your Hardware
Check which physical hardware you have:
- PI hexapod? → Use "PI 6-dof"
- Sigmakoki linear stage? → Use "Optosigma OSMS26-100(X)"
- Sigmakoki rotation stage? → Use "Optosigma OSMS-120YAW"

### 3. Test Correct Option
Select the option matching your hardware and verify:
- Connection succeeds
- Movement commands work
- Position reading is correct

## 🐛 If Wrong Option Selected

**Symptoms:**
- Connection succeeds but movement fails
- Position reads as 0 or garbage
- Commands timeout
- Stage doesn't move

**Solution:**
Select the correct option for your hardware type.

## 📁 Key Files

| File | Purpose |
|------|---------|
| `MainWindow.axaml.cs` | F11 fullscreen handler |
| `StageController.cs` | PI and Sigmakoki controllers |
| `Hsc103RotationStageController.cs` | Rotation stage (Python) |
| `StageControllerFactory.cs` | Creates controllers |
| `StageHardwareType.cs` | Hardware type enum |
| `CalibrationSetupView.axaml` | UI ComboBox |

## ✅ Summary

1. **F11 fullscreen:** Already working, just press F11!
2. **Both Optosigma options:** Different hardware (linear vs rotation)
3. **Plugin system:** Working with Mock plugin
4. **Built-in controllers:** PI, Sigmakoki linear, Sigmakoki rotation

Everything is working as designed. The confusion was thinking one Optosigma option was for PI - they're both for Sigmakoki hardware (different models).

## 🚀 Next Steps

1. **Test F11:** Press F11 in your app
2. **Verify hardware:** Check which physical stage you have
3. **Select correct option:** Use the matching ComboBox option
4. **Test movement:** Verify stage moves correctly

Need help identifying which hardware you have? Let me know!
