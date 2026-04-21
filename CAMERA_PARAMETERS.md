# Camera Pipeline Parameters for Pose Estimation

## Current Default Values (from ConfigViewModel.cs)

### Camera Intrinsics
- **Fx (Focal length X in pixels):** 12727.3
- **Fy (Focal length Y in pixels):** 12727.3
- **Cx (Principal point X):** 0.0
- **Cy (Principal point Y):** 0.0

### Camera Physical Properties
- **Focal Length:** 56.0 mm
- **Pixel Size:** 0.0044 mm (4.4 µm)

### Pattern Configuration
- **Pitch X:** 0.1 mm
- **Pitch Y:** 0.1 mm
- **Components X:** 11
- **Components Y:** 11

### Algorithm Parameters
- **Window Size:** 10.0
- **Code Pitch Blocks:** 6

## How to View Current Values

1. **In the UI:** Go to the **Config** tab to see and modify these values
2. **In logs:** After the fix above, run a pose estimation sequence and check the logs for:
   ```
   [PoseEst] Parameters: Fx=..., Fy=..., Cx=..., Cy=...
   [PoseEst] Parameters: PixelSize=...mm, FocalLength=...mm
   [PoseEst] Parameters: PitchX=...mm, PitchY=...mm
   [PoseEst] Parameters: ComponentsX=..., ComponentsY=...
   [PoseEst] Parameters: WindowSize=..., CodePitchBlocks=...
   ```

## Parameter Calculation

### Focal Length in Pixels
The relationship between focal length in mm and pixels:
```
Fx = FocalLength / PixelSize
Fx = 28.0 mm / 0.0044 mm = 6363.64 pixels
```

### Principal Point (Cx, Cy)
Should be set to the image center:
```
Cx = ImageWidth / 2
Cy = ImageHeight / 2
```

**Current issue:** Cx and Cy are set to 0, which is incorrect!

## Common Issues

### 1. Principal Point at (0, 0)
**Problem:** Cx=0, Cy=0 means the optical center is at the top-left corner, which is wrong.

**Fix:** Set to image center. For a 2048x2048 image:
```
Cx = 1024
Cy = 1024
```

### 2. Pattern Not Matching
**Problem:** Pattern configuration doesn't match the physical pattern.

**Fix:** Verify:
- Pitch X/Y matches actual pattern spacing
- Components X/Y matches actual pattern grid size
- Pattern type is correct (Dot Grid for HMF coded target)

### 3. Camera Intrinsics Not Calibrated
**Problem:** Using default values instead of calibrated values.

**Fix:** Perform camera calibration to get accurate Fx, Fy, Cx, Cy values.

## How Parameters Are Used

When pose estimation runs, these parameters are passed to `PoseEstApiBridgeClient.EstimatePoseFromImagePath()`:

```csharp
PoseEstApiBridgeClient.EstimatePoseFromImagePath(
    imagePath: "C:\\...\\step_00001_....jpg",
    intrinsicMatrix: [Fx, 0, Cx, 0, Fy, Cy, 0, 0, 1],  // 3x3 matrix
    pixelSize: 0.0044,
    focalLength: 28.0,
    pitchX: 0.1,
    pitchY: 0.1,
    componentsX: 11,
    componentsY: 11,
    windowSize: 12.0,
    codePitchBlocks: 6
);
```

## Recommended Actions

1. **Fix Principal Point:**
   - Go to Config tab
   - Set Cx = ImageWidth / 2
   - Set Cy = ImageHeight / 2

2. **Verify Pattern Configuration:**
   - Measure actual pattern pitch with ruler
   - Count actual pattern grid size
   - Update Config tab values

3. **Check Captured Images:**
   - Open: `C:\Users\agama\Documents\SignalUI_Captures\`
   - Verify pattern is visible and in focus
   - Check if pattern fills most of the image

4. **Run Test:**
   - After fixing parameters, run sequence again
   - Check logs for parameter values
   - Verify pose estimation succeeds
