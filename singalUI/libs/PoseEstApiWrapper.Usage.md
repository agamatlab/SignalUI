# PoseEstApi Wrapper Usage Guide

## Overview

The PoseEstApi wrapper provides a C# interface to the native 6DoF pose estimation library. It estimates the position (translation) and orientation (rotation) of a coded target in 3D space from a 2D image.

**Key Features:**
- 6DoF pose estimation (3 rotations + 3 translations)
- HMF (Hierarchical Multi-Frequency) coded target detection
- Camera intrinsic calibration support
- P/Invoke interop with native C++ library

## Quick Start

```csharp
using singalUI.libs;

// Create and initialize
using var estimator = new PoseEstimator();
estimator.Initialize();

// Set up camera intrinsics
var intrinsics = new CameraIntrinsics
{
    Fx = 500.0,  // Focal length X
    Fy = 500.0,  // Focal length Y
    Cx = 320.0,  // Principal point X
    Cy = 240.0   // Principal point Y
};

// Set algorithm parameters
var parameters = new AlgorithmParams
{
    PitchX = 1.2,
    PitchY = 1.2,
    ComponentsX = 11.0,
    ComponentsY = 11.0,
    WindowSize = 5.0,
    CodePitchBlocks = 4.0
};

// Run pose estimation
var result = estimator.EstimatePose(
    imageData: imageArray,
    imageRows: 480,
    imageCols: 640,
    intrinsicMatrix: intrinsics.ToArray(),
    pixelSize: 1.0,
    focalLength: 500.0,
    hmfGData: hmfGArray,
    hmfGRows: 11,
    hmfGCols: 11,
    hmfXData: hmfXArray,
    hmfYData: hmfYArray,
    pitchX: parameters.PitchX,
    pitchY: parameters.PitchY,
    componentsX: parameters.ComponentsX,
    componentsY: parameters.ComponentsY,
    windowSize: parameters.WindowSize,
    codePitchBlocks: parameters.CodePitchBlocks
);

Console.WriteLine(result.ToString());
```

---

## API Reference

### PoseEstimator Class

#### `Initialize()`
Initializes the native pose estimation library. Must be called before any other operations.

```csharp
public void Initialize()
```

**Throws:** `InvalidOperationException` if initialization fails.

---

#### `EstimatePose(...)`
Performs 6DoF pose estimation on the provided image data.

```csharp
public PoseEstimateResult EstimatePose(
    double[] imageData,      // Raw image pixel data (flattened 2D array)
    int imageRows,           // Image height in pixels
    int imageCols,           // Image width in pixels
    double[] intrinsicMatrix,// 3x3 camera intrinsic matrix (row-major, 9 elements)
    double pixelSize,        // Size of camera sensor pixel (in same units as translation output)
    double focalLength,      // Camera focal length
    double[] hmfGData,       // HMF G matrix data (flattened 2D array)
    int hmfGRows,            // HMF G matrix rows
    int hmfGCols,            // HMF G matrix columns
    double[] hmfXData,       // HMF X coordinate lookup table
    double[] hmfYData,       // HMF Y coordinate lookup table
    double pitchX,           // Horizontal pitch of coded pattern
    double pitchY,           // Vertical pitch of coded pattern
    double componentsX,      // Number of horizontal components
    double componentsY,      // Number of vertical components
    double windowSize,       // Processing window size
    double codePitchBlocks   // Code pitch in blocks
)
```

**Returns:** `PoseEstimateResult` containing pose data and status

**Throws:** `InvalidOperationException` if library not initialized

---

#### `Shutdown()`
Releases native library resources. Automatically called by `Dispose()`.

```csharp
public void Shutdown()
```

---

### CameraIntrinsics Class

Represents the camera's intrinsic parameters (3x3 matrix).

```csharp
public class CameraIntrinsics
{
    public double Fx { get; set; }  // Focal length X (pixels)
    public double Fy { get; set; }  // Focal length Y (pixels)
    public double Cx { get; set; }  // Principal point X (pixels)
    public double Cy { get; set; }  // Principal point Y (pixels)

    // Convert to 3x3 row-major array for API call
    public double[] ToArray()
}
```

**Default Values:**
```csharp
Fx = 500.0, Fy = 500.0, Cx = 320.0, Cy = 240.0
```

**Intrinsic Matrix Format:**
```
| Fx  0   Cx  |
| 0   Fy  Cy  |
| 0   0   1   |
```

---

### AlgorithmParams Class

Encapsulates algorithm parameters for the HMF coded target.

```csharp
public class AlgorithmParams
{
    public double PitchX { get; set; }          // Horizontal pattern pitch
    public double PitchY { get; set; }          // Vertical pattern pitch
    public double ComponentsX { get; set; }     // Horizontal component count
    public double ComponentsY { get; set; }     // Vertical component count
    public double WindowSize { get; set; }      // Processing window size
    public double CodePitchBlocks { get; set; } // Code pitch in blocks
}
```

**Default Values:**
```csharp
PitchX = 1.2, PitchY = 1.2
ComponentsX = 11.0, ComponentsY = 11.0
WindowSize = 5.0, CodePitchBlocks = 4.0
```

---

### PoseEstimateResult Class

Contains the results of pose estimation.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Status` | `PoseStatus` | Success/failure status |
| `StatusMessage` | `string` | Human-readable status description |
| `PoseData` | `double[]` | Raw pose data array (6 elements) |
| `Rx` | `double` | Rotation around X axis (radians) |
| `Ry` | `double` | Rotation around Y axis (radians) |
| `Rz` | `double` | Rotation around Z axis (radians) |
| `Tx` | `double` | Translation X (millimeters) |
| `Ty` | `double` | Translation Y (millimeters) |
| `Tz` | `double` | Translation Z (millimeters) |

#### PoseStatus Enum

| Value | Meaning |
|-------|---------|
| `Success = 1` | Pose estimation successful |
| `CoarseFail = -1` | IpDFT coarse stage failed |
| `DecodeFail = -3` | HMF decoding failed, relative pose used |

---

## Complete Example

```csharp
using System;
using singalUI.libs;

public class PoseEstimationExample
{
    public void RunPoseEstimation()
    {
        // 1. Initialize the estimator
        using var estimator = new PoseEstimator();
        try
        {
            estimator.Initialize();
            Console.WriteLine("PoseEstApi initialized successfully.");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Initialization failed: {ex.Message}");
            return;
        }

        // 2. Prepare image data (example: 640x480 grayscale image)
        int imageWidth = 640;
        int imageHeight = 480;
        double[] imageData = LoadOrGenerateImageData(imageWidth, imageHeight);

        // 3. Set up camera intrinsics (calibrate your camera for accurate results)
        var intrinsics = new CameraIntrinsics
        {
            Fx = 520.5,   // Calibrate these values
            Fy = 519.8,
            Cx = 320.0,
            Cy = 240.0
        };

        // 4. Prepare HMF data (coded target pattern information)
        int hmfSize = 11;
        double[] hmfG = GenerateHmfGData(hmfSize);
        double[] hmfX = GenerateHmfXData(hmfSize);
        double[] hmfY = GenerateHmfYData(hmfSize);

        // 5. Configure algorithm parameters
        var parameters = new AlgorithmParams
        {
            PitchX = 1.2,           // Pattern physical spacing
            PitchY = 1.2,
            ComponentsX = 11.0,     // 11x11 component grid
            ComponentsY = 11.0,
            WindowSize = 5.0,
            CodePitchBlocks = 4.0
        };

        // 6. Run pose estimation
        var result = estimator.EstimatePose(
            imageData: imageData,
            imageRows: imageHeight,
            imageCols: imageWidth,
            intrinsicMatrix: intrinsics.ToArray(),
            pixelSize: 0.0055,      // 5.5um pixel size (adjust for your sensor)
            focalLength: 16.0,      // 16mm lens (adjust for your setup)
            hmfGData: hmfG,
            hmfGRows: hmfSize,
            hmfGCols: hmfSize,
            hmfXData: hmfX,
            hmfYData: hmfY,
            pitchX: parameters.PitchX,
            pitchY: parameters.PitchY,
            componentsX: parameters.ComponentsX,
            componentsY: parameters.ComponentsY,
            windowSize: parameters.WindowSize,
            codePitchBlocks: parameters.CodePitchBlocks
        );

        // 7. Process results
        if (result.Status == PoseStatus.Success)
        {
            Console.WriteLine("=== Pose Estimation Successful ===");
            Console.WriteLine($"Rotation (degrees):");
            Console.WriteLine($"  Rx: {result.Rx * 180 / Math.PI:F2}");
            Console.WriteLine($"  Ry: {result.Ry * 180 / Math.PI:F2}");
            Console.WriteLine($"  Rz: {result.Rz * 180 / Math.PI:F2}");
            Console.WriteLine($"Translation (mm):");
            Console.WriteLine($"  Tx: {result.Tx:F2}");
            Console.WriteLine($"  Ty: {result.Ty:F2}");
            Console.WriteLine($"  Tz: {result.Tz:F2}");
        }
        else
        {
            Console.WriteLine($"Pose estimation failed: {result.StatusMessage}");
        }
    }

    private double[] LoadOrGenerateImageData(int width, int height)
    {
        // Load your actual image data here
        // For testing, create a dummy array
        return new double[width * height];
    }

    private double[] GenerateHmfGData(int size)
    {
        // Generate HMF G matrix for your coded target
        // This represents the pattern's frequency components
        return new double[size * size];
    }

    private double[] GenerateHmfXData(int size)
    {
        // X coordinate lookup for HMF decoding
        return new double[size];
    }

    private double[] GenerateHmfYData(int size)
        => new double[size];
}
```

---

## HMF Data Preparation

The HMF (Hierarchical Multi-Frequency) coded target requires three data structures:

### HMF G Matrix
A 2D array representing the frequency components of the coded pattern.
- **Size:** `ComponentsY × ComponentsX`
- **Format:** Flattened row-major (all rows concatenated)
- **Content:** Complex magnitude values for each frequency component

### HMF X/Y Lookup Tables
1D arrays for coordinate mapping during HMF decoding.
- **Size:** Matches component count (typically 11-15 elements)
- **Purpose:** Maps decoded frequencies to spatial coordinates

**Example for 11×11 pattern:**
```csharp
int components = 11;
double[] hmfG = new double[components * components];  // 121 elements
double[] hmfX = new double[components];               // 11 elements
double[] hmfY = new double[components];               // 11 elements

// Fill with your pattern data
for (int i = 0; i < components; i++)
{
    hmfX[i] = i * pitchX;  // X coordinates
    hmfY[i] = i * pitchY;  // Y coordinates
}
```

---

## Error Handling

```csharp
try
{
    estimator.Initialize();
}
catch (InvalidOperationException ex)
{
    // Library failed to initialize
    // Check that required DLLs are present:
    // - PoseEstApiWrapper.dll
    // - PoseEstApi.dll
    // - libiomp5md.dll (Intel OpenMP runtime)
}

try
{
    var result = estimator.EstimatePose(...);

    if (result.Status != PoseStatus.Success)
    {
        switch (result.Status)
        {
            case PoseStatus.CoarseFail:
                // Target not detected in coarse search
                // Check: image quality, lighting, target visibility
                break;

            case PoseStatus.DecodeFail:
                // Fine detection failed, using fallback
                // May still have approximate pose data
                break;
        }
    }
}
catch (InvalidOperationException ex)
{
    // Called before Initialize()
}
```

---

## Best Practices

### 1. Always Initialize First
```csharp
var estimator = new PoseEstimator();
estimator.Initialize();  // Must call before EstimatePose
```

### 2. Use Using Statement for Cleanup
```csharp
using var estimator = new PoseEstimator();
// Automatically calls Shutdown() when done
```

### 3. Calibrate Camera Intrinsics
For accurate pose estimation, calibrate your camera:
```csharp
var intrinsics = new CameraIntrinsics
{
    Fx = calibratedFx,  // From camera calibration
    Fy = calibratedFy,
    Cx = calibratedCx,
    Cy = calibratedCy
};
```

### 4. Match Parameters to Physical Target
Ensure algorithm parameters match your actual coded target:
```csharp
var parameters = new AlgorithmParams
{
    PitchX = actualTargetPitchX,      // Measure your target
    PitchY = actualTargetPitchY,
    ComponentsX = actualComponentCount,
    ComponentsY = actualComponentCount,
    // ...
};
```

### 5. Check Results Status
Always verify success before using pose data:
```csharp
var result = estimator.EstimatePose(...);
if (result.Status == PoseStatus.Success)
{
    // Safe to use result.Rx, result.Tx, etc.
}
```

---

## Coordinate System

### Rotation
- **Rx**: Rotation around X axis (pitch)
- **Ry**: Rotation around Y axis (yaw)
- **Rz**: Rotation around Z axis (roll)
- Units: radians (convert with `* 180 / Math.PI` for degrees)

### Translation
- **Tx**: Translation along X axis (millimeters)
- **Ty**: Translation along Y axis (millimeters)
- **Tz**: Translation along Z axis (depth, millimeters)

### Coordinate Conventions
- **Origin**: Camera optical center
- **Z axis**: Optical axis (pointing forward from camera)
- **X axis**: Right (in image plane)
- **Y axis**: Down (in image plane, typical computer vision convention)

---

## Troubleshooting

### DLL Load Failures
**Error:** `Unable to load DLL 'PoseEstApiWrapper.dll'`

**Solutions:**
1. Verify DLLs are in output directory (next to .exe)
2. Check `libiomp5md.dll` is present (Intel OpenMP runtime)
3. Ensure architecture matches (x64 vs x86)

### Initialization Failures
**Error:** `Failed to initialize PoseEstApi library`

**Solutions:**
1. Check native library dependencies are installed
2. Verify Intel runtime libraries are available
3. Check for conflicting DLL versions

### CoarseFail Status
**Error:** `Coarse Fail (IpDFT stage failed)`

**Causes:**
- Target not visible in image
- Poor lighting/contrast
- Target too far or too close
- Incorrect focal length/pixel size

### DecodeFail Status
**Error:** `Decode Fail (HMF decoding failed)`

**Causes:**
- HMF data doesn't match actual target
- Pattern occlusion or blur
- Inaccurate camera intrinsics

---

## Thread Safety

The `PoseEstimator` class is **not thread-safe**. Use separate instances per thread or synchronize access:

```csharp
// Option 1: Separate instances
var estimator1 = new PoseEstimator();
estimator1.Initialize();

var estimator2 = new PoseEstimator();
estimator2.Initialize();

// Option 2: Synchronized access
private readonly object _lock = new object();

lock (_lock)
{
    var result = estimator.EstimatePose(...);
}
```

---

## Performance Considerations

- **Initialization** is relatively expensive - initialize once, reuse
- **EstimatePose** allocates native memory internally - using `NativeArray` handles cleanup
- Dispose of estimator when done to free native resources
- For high-frequency calls, consider keeping one long-lived instance

---

## Dependencies

### Required DLLs (in output directory):
| DLL | Purpose |
|-----|---------|
| `PoseEstApiWrapper.dll` | C wrapper interface |
| `PoseEstApi.dll` | Main pose estimation library |
| `libiomp5md.dll` | Intel OpenMP runtime (parallel processing) |

### .NET Dependencies:
- .NET 8.0 or later
- System.Runtime.InteropServices (for P/Invoke)
