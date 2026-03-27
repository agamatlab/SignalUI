using System;

namespace singalUI.Services;

/// <summary>
/// Non-Windows fallback so the Avalonia UI can run on macOS/Linux without Matrix Vision SDK binaries.
/// Windows keeps using MatrixVisionCameraService.cs unchanged.
/// </summary>
public class MatrixVisionCameraService : IDisposable
{
    private const string UnsupportedMessage =
        "Matrix Vision camera support is only available on Windows with mvIMPACT Acquire installed.";

    public event Action<long>? FrameUpdated;
    public event Action<string>? StatusChanged;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected { get; private set; }
    public string CameraSerial { get; set; } = "";
    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }

    public (byte[]? buffer, int width, int height, long seq) GetLatestFrameSnapshot()
    {
        return (null, 0, 0, 0);
    }

    public bool Initialize()
    {
        IsConnected = false;
        StatusChanged?.Invoke(UnsupportedMessage);
        ConnectionChanged?.Invoke(false);
        return false;
    }

    public void SetExposure(double exposureUs)
    {
        StatusChanged?.Invoke(UnsupportedMessage);
    }

    public void SetGain(double gain)
    {
        StatusChanged?.Invoke(UnsupportedMessage);
    }

    public void SetFrameRate(double fps)
    {
        StatusChanged?.Invoke(UnsupportedMessage);
    }

    public bool TryReadAppliedParameters(out double exposureUs, out double gainDb, out double fps)
    {
        exposureUs = double.NaN;
        gainDb = double.NaN;
        fps = double.NaN;
        return false;
    }

    public void StartAcquisition()
    {
        StatusChanged?.Invoke(UnsupportedMessage);
    }

    public void StopAcquisition()
    {
    }

    public void Dispose()
    {
        IsConnected = false;
        ConnectionChanged?.Invoke(false);
    }
}
