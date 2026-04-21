using System;
using System.Threading;
using Avalonia.Threading;
using singalUI.Models;

namespace singalUI.Services;

/// <summary>
/// Shared stage-position source for all view models.
/// Values are stored as mm for linear axes and degrees for rotational axes.
/// </summary>
public static class StagePositionService
{
    private static readonly object Sync = new();
    private static Timer? _pollTimer;
    private static bool _started;

    public static PositionData Current { get; } = new();

    public static event Action? PositionChanged;

    public static void Start()
    {
        lock (Sync)
        {
            if (_started)
            {
                return;
            }

            _pollTimer = new Timer(_ => PollAndPublish(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
            _started = true;
        }
    }

    public static void Stop()
    {
        lock (Sync)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            _started = false;
        }
    }

    public static void RefreshNow()
    {
        PollAndPublish();
    }

    private static void PollAndPublish()
    {
        try
        {
            double x = 0, y = 0, z = 0, rx = 0, ry = 0, rz = 0;

            foreach (var wrapper in StageManager.Wrappers)
            {
                if (!wrapper.IsConnected || wrapper.Controller == null)
                {
                    continue;
                }

                foreach (var axis in wrapper.EnabledAxes)
                {
                    double pos = wrapper.GetAxisPosition(axis);
                    switch (axis)
                    {
                        case AxisType.X: x = pos; break;
                        case AxisType.Y: y = pos; break;
                        case AxisType.Z: z = pos; break;
                        case AxisType.Rx: rx = pos; break;
                        case AxisType.Ry: ry = pos; break;
                        case AxisType.Rz: rz = pos; break;
                    }
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                Current.X = x;
                Current.Y = y;
                Current.Z = z;
                Current.Rx = rx;
                Current.Ry = ry;
                Current.Rz = rz;
                PositionChanged?.Invoke();
            });
        }
        catch
        {
            // Keep polling even if one controller read fails.
        }
    }
}
