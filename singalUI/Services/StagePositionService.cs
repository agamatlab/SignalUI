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
                    Console.WriteLine($"[StagePositionService] Skipping wrapper {wrapper.Id} - IsConnected={wrapper.IsConnected}, Controller={wrapper.Controller != null}");
                    continue;
                }

                Console.WriteLine($"[StagePositionService] Reading from wrapper {wrapper.Id}, AvailableAxes: [{string.Join(", ", wrapper.AvailableAxes)}]");

                // Read from all available axes, not just enabled ones
                foreach (var axisName in wrapper.AvailableAxes)
                {
                    try
                    {
                        // Convert axis name string to AxisType
                        AxisType axis = axisName.ToUpper() switch
                        {
                            "X" => AxisType.X,
                            "Y" => AxisType.Y,
                            "Z" => AxisType.Z,
                            "RX" => AxisType.Rx,
                            "RY" => AxisType.Ry,
                            "RZ" => AxisType.Rz,
                            _ => AxisType.X // Default, will be skipped
                        };

                        double pos = wrapper.GetAxisPosition(axis);
                        Console.WriteLine($"[StagePositionService] Axis {axisName} -> {axis} = {pos}");
                        
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
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[StagePositionService] Error reading axis {axisName}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"[StagePositionService] Final positions: X={x}, Y={y}, Z={z}, Rx={rx}, Ry={ry}, Rz={rz}");

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
        catch (Exception ex)
        {
            Console.WriteLine($"[StagePositionService] Error in PollAndPublish: {ex.Message}");
        }
    }
}
