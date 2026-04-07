# Pose Estimation Pipeline - Complete Setup

## ✅ What's Now Automated

### 1. Archive Files Auto-Copy
The application now automatically ensures Archive files are present:
- **Source:** `bin/Debug/net8.0/Archive/` (copied during build)
- **Destination:** `C:\Users\agama\Documents\SignalUI_Captures\Archive\`
- **Files:** 12+12_G.txt, 12+12_X.txt, 12+12_Y.txt
- **When:** On application startup (CalibrationSetupViewModel constructor)

### 2. Capture Directory Auto-Create
- **Location:** `C:\Users\agama\Documents\SignalUI_Captures\`
- **Created:** Automatically on startup

### 3. CSV Auto-Export
- **When:** After pose estimation sequence completes
- **Format:** `pose_results_YYYYMMDD_HHmmss.csv`
- **Opens:** Folder automatically opens after export

## 📋 Current Pipeline Parameters

```
Camera Intrinsics:
  Fx = 6363.64 pixels
  Fy = 6363.64 pixels
  Cx = 0.0 pixels (correct for this API)
  Cy = 0.0 pixels (correct for this API)

Camera Physical:
  Focal Length = 28.0 mm
  Pixel Size = 0.0044 mm (4.4 µm)

Pattern Configuration:
  Pitch X = 0.1 mm
  Pitch Y = 0.1 mm
  Components X = 11
  Components Y = 11

Algorithm Parameters:
  Window Size = 12.0
  Code Pitch Blocks = 6

Image Format:
  Size: 1600 x 1200 pixels
  Format: Grayscale JPEG
```

## 🔄 Complete Pipeline Flow

### 1. Sequence Start
```
User clicks "Start Sequence" with "Enable Pose Estimation Scan" checked
↓
CalibrationSetupViewModel.StartSequence()
↓
Checks Archive files exist (auto-copies if needed)
↓
Starts N worker threads (MaxConcurrentPoseEstimations)
```

### 2. For Each Movement Step
```
Move stage to position
↓
Wait StepPauseDurationMs (for settling)
↓
Capture image → step_XXXXX_timestamp.jpg
↓
Queue pose estimation task
↓
Worker thread picks up task
↓
Calls PoseEstApiBridge.dll
  ├─ Loads image
  ├─ Loads Archive files (12+12_G.txt, X, Y)
  ├─ Runs pose estimation algorithm
  └─ Returns 6DOF pose (Rx, Ry, Rz, Tx, Ty, Tz)
↓
Store result in _poseResults list
↓
Update graphs in real-time
```

### 3. Sequence Complete
```
Wait for all queued tasks to complete
↓
Stop worker threads
↓
Auto-export CSV with all results
↓
Open folder with results
↓
Send results to Analysis tab
```

## 📁 File Structure

```
C:\Users\agama\Documents\SignalUI_Captures\
├── Archive\
│   ├── 12+12_G.txt    (Pattern geometry - auto-copied)
│   ├── 12+12_X.txt    (X-axis codes - auto-copied)
│   └── 12+12_Y.txt    (Y-axis codes - auto-copied)
├── step_00001_20260405_143224_155.jpg
├── step_00002_20260405_143225_055.jpg
├── ...
└── pose_results_20260405_143231.csv
```

## 🔧 How It Works

### Archive File Management
```csharp
// On startup (CalibrationSetupViewModel constructor):
EnsureArchiveFiles()
  ├─ Create Archive directory if missing
  ├─ For each required file (12+12_G.txt, X, Y):
  │   ├─ Check if exists in capture directory
  │   ├─ If missing or outdated:
  │   │   └─ Copy from application directory
  └─ Log status
```

### Pose Estimation Call
```csharp
PoseEstApiBridgeClient.EstimatePoseFromImagePath(
    imagePath: "C:\\...\\step_00001_....jpg",
    intrinsicMatrix: [6363.64, 0, 0, 0, 6363.64, 0, 0, 0, 1],
    pixelSize: 0.0044,
    focalLength: 28.0,
    pitchX: 0.1,
    pitchY: 0.1,
    componentsX: 11,
    componentsY: 11,
    windowSize: 12.0,
    codePitchBlocks: 6
)
↓
PoseEstApiBridge.dll (C++)
  ├─ Load image as grayscale
  ├─ Load Archive files from image parent directory
  ├─ Call PoseEstApi MATLAB-generated code
  └─ Return pose [Rx, Ry, Rz, Tx, Ty, Tz]
```

## ✅ Verification Checklist

After starting the application:
1. ✅ Check logs for: `[Archive] Archive directory ready`
2. ✅ Verify files exist: `C:\Users\agama\Documents\SignalUI_Captures\Archive\*.txt`
3. ✅ Run pose estimation sequence
4. ✅ Check logs for: `[PoseEst] Step X SUCCESS: Tx=..., Ty=..., Tz=...`
5. ✅ Verify CSV exported with Success=True

## 🐛 Troubleshooting

### Pose Estimation Still Fails
**Check:**
1. Archive files present in capture directory?
   ```
   ls C:\Users\agama\Documents\SignalUI_Captures\Archive\
   ```
2. Images captured successfully?
   ```
   ls C:\Users\agama\Documents\SignalUI_Captures\step_*.jpg
   ```
3. Pattern visible in images?
   - Open an image and verify pattern is in focus
   - Pattern should fill most of the frame
4. Check application logs for detailed error

### Archive Files Not Copying
**Check:**
1. Source files exist in application directory?
   ```
   ls bin/Debug/net8.0/Archive/
   ```
2. Check logs for: `[Archive] WARNING: Source file not found`
3. Rebuild application to ensure files are copied during build

### CSV Shows Success=False
**Possible causes:**
1. Pattern not detected in image
2. Pattern configuration mismatch
3. Image quality issues (blur, lighting)
4. Archive files corrupted

## 📊 CSV Output Format

```csv
StepNumber,Timestamp,ImagePath,StageX,StageY,StageZ,StageRx,StageRy,StageRz,EstX,EstY,EstZ,EstRx,EstRy,EstRz,ErrorX,ErrorY,ErrorZ,ErrorRx,ErrorRy,ErrorRz,Success,ErrorMessage
1,2026-04-05 14:32:24.182,"C:\...\step_00001_....jpg",0.0,0.0,0.0,0.0,0.0,0.0,0.1,0.2,0.3,0.01,0.02,0.03,0.1,0.2,0.3,0.01,0.02,0.03,True,""
```

**Columns:**
- StepNumber: Sequential step in scan
- Timestamp: When image was captured
- ImagePath: Full path to captured image
- StageX/Y/Z: Commanded stage position (µm)
- StageRx/Ry/Rz: Commanded stage rotation (degrees)
- EstX/Y/Z: Estimated position from pose (µm)
- EstRx/Ry/Rz: Estimated rotation from pose (degrees)
- ErrorX/Y/Z: Position error (µm)
- ErrorRx/Ry/Rz: Rotation error (degrees)
- Success: True if pose estimation succeeded
- ErrorMessage: Error details if failed

## 🎯 Everything Should Now Work Automatically!

The pipeline is fully automated:
- ✅ Archive files auto-copy on startup
- ✅ Images captured at each step
- ✅ Parallel processing with configurable workers
- ✅ CSV auto-exported at end
- ✅ Results sent to Analysis tab
- ✅ Folder opens automatically

Just run your sequence and everything should work!
