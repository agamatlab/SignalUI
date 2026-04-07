using CommunityToolkit.Mvvm.ComponentModel;
using singalUI.Models;
using singalUI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace singalUI.Services;

/// <summary>
/// Wrapper that holds a stage configuration plus its actual controller instance
/// All stage-related data is kept together in one place
/// </summary>
public partial class StageWrapper : ObservableObject
{
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "singalui_stage.log");

    [ObservableProperty]
    private StageInstance _configuration;

    [ObservableProperty]
    private global::StageController? _controller;

    [ObservableProperty]
    private bool _isConnected;

    /// <summary>
    /// Unique ID for this stage
    /// </summary>
    public int Id => Configuration.Id;

    /// <summary>
    /// Hardware type (PI, Sigmakoki, etc.)
    /// </summary>
    public StageHardwareType HardwareType
    {
        get => Configuration.HardwareType;
        set => Configuration.HardwareType = value;
    }

    /// <summary>
    /// Sigma Koki controller model (if applicable)
    /// </summary>
    public SigmakokiControllerType SigmakokiController
    {
        get => Configuration.SigmakokiController;
        set => Configuration.SigmakokiController = value;
    }

    /// <summary>
    /// Connection status message
    /// </summary>
    public string ConnectionStatus
    {
        get => Configuration.ConnectionStatus;
        set => Configuration.ConnectionStatus = value;
    }

    /// <summary>
    /// Available axes from the controller
    /// </summary>
    public string[] AvailableAxes
    {
        get => Configuration.AvailableAxes;
        set => Configuration.AvailableAxes = value;
    }

    /// <summary>
    /// Get the axes this stage controls
    /// </summary>
    public AxisType[] EnabledAxes => Configuration.GetEnabledAxes();

    /// <summary>
    /// Get the number of axes this stage controls
    /// </summary>
    public int EnabledAxesCount => Configuration.EnabledAxesCount;

    /// <summary>
    /// Get display name
    /// </summary>
    public string DisplayName => Configuration.DisplayName;

    /// <summary>
    /// Create a new StageWrapper
    /// </summary>
    public static StageWrapper Create(int id, StageHardwareType hardwareType = StageHardwareType.None)
    {
        return new StageWrapper
        {
            Configuration = new StageInstance
            {
                Id = id,
                HardwareType = hardwareType,
                SigmakokiController = SigmakokiControllerType.HSC_103,
                ConnectionStatus = "Not connected"
            }
        };
    }

    /// <summary>
    /// Connect to this stage
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        if (HardwareType == StageHardwareType.None)
        {
            ConnectionStatus = "No hardware selected";
            return false;
        }

        try
        {
            ConnectionStatus = "Connecting...";
            Log($"Stage {Id}: Starting connection (Hardware: {HardwareType})...");

            // Create controller
            var controller = StageControllerFactory.CreateController(HardwareType, SigmakokiController);
            if (controller == null)
            {
                ConnectionStatus = "Failed to create controller";
                Log($"Stage {Id}: ERROR - Failed to create controller");
                return false;
            }

            Log($"Stage {Id}: Controller created, attempting connection...");

            // Connect
            await Task.Run(() => controller.Connect());

            IsConnected = controller.CheckConnected();
            if (IsConnected)
            {
                AvailableAxes = controller.GetAxesList();
                ConnectionStatus = $"Connected ({AvailableAxes.Length} axes)";
                Controller = controller;
                Log($"Stage {Id}: SUCCESS! Connected with {AvailableAxes.Length} axes: [{string.Join(", ", AvailableAxes)}]");
                Log($"Stage {Id}: EnabledAxes will be: [{string.Join(", ", EnabledAxes)}]");
                return true;
            }
            else
            {
                ConnectionStatus = "Connection failed";
                Log($"Stage {Id}: ERROR - CheckConnected() returned false");
                return false;
            }
        }
        catch (DllNotFoundException ex)
        {
            ConnectionStatus = $"DLL Error: {ex.Message}";
            IsConnected = false;
            Log($"Stage {Id}: DLL NOT FOUND - {ex.Message}");
            Log($"Stage {Id}: Make sure PI_GCS2_DLL_x64.dll is in the application folder!");
            return false;
        }
        catch (BadImageFormatException ex)
        {
            ConnectionStatus = $"Architecture Error: Wrong DLL version";
            IsConnected = false;
            Log($"Stage {Id}: BAD IMAGE FORMAT - {ex.Message}");
            Log($"Stage {Id}: You may be loading a 32-bit DLL in a 64-bit process (or vice versa)");
            return false;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            IsConnected = false;
            Log($"Stage {Id}: EXCEPTION - {ex.Message}");
            Log($"Stage {Id}: Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Log message to console and file
    /// </summary>
    private static void Log(string message)
    {
        string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] [StageWrapper] {message}";
        Console.WriteLine(logMessage);
        try
        {
            File.AppendAllText(LogFile, logMessage + "\n");
        }
        catch { }
    }

    /// <summary>
    /// Disconnect from this stage
    /// </summary>
    public void Disconnect()
    {
        if (!IsConnected) return;

        try
        {
            Controller?.Disconnect();
        }
        catch { }

        IsConnected = false;
        AvailableAxes = Array.Empty<string>();
        ConnectionStatus = "Disconnected";
        Controller = null;
    }

    /// <summary>
    /// Check if still connected
    /// </summary>
    public bool CheckConnection()
    {
        if (Controller == null || !IsConnected)
        {
            return false;
        }

        try
        {
            bool connected = Controller.CheckConnected();

            // Update status if changed
            if (connected != IsConnected)
            {
                IsConnected = connected;
                if (connected)
                {
                    ConnectionStatus = $"Connected ({AvailableAxes.Length} axes)";
                }
                else
                {
                    ConnectionStatus = "Connection lost";
                    AvailableAxes = Array.Empty<string>();
                    Log($"Stage {Id}: Connection lost (CheckConnected returned false)");
                }
            }

            return connected;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = "Connection error";
            AvailableAxes = Array.Empty<string>();
            Log($"Stage {Id}: Connection check failed - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get the controller index for a specific axis
    /// Returns -1 if axis not controlled by this stage
    /// </summary>
    public int GetAxisIndex(AxisType axis)
    {
        if (!IsConnected || Controller == null) return -1;

        var axes = EnabledAxes;
        for (int i = 0; i < axes.Length; i++)
        {
            if (axes[i] == axis)
            {
                return i; // 0-based index for the controller
            }
        }
        return -1;
    }

    /// <summary>
    /// Move an axis on this stage by the specified amount
    /// </summary>
    public void MoveAxis(AxisType axis, double amount)
    {
        if (!IsConnected || Controller == null)
        {
            throw new Exception($"Stage {Id} is not connected");
        }

        int axisIndex = GetAxisIndex(axis);
        if (axisIndex < 0)
        {
            throw new Exception($"Axis {axis} is not controlled by stage {Id}");
        }

        // Get position BEFORE move
        double posBefore = Controller.GetPosition(axisIndex);
        
        // For Sigmakoki, amount is already in steps (converted by caller)
        // For other controllers, amount is in mm
        if (Controller is SigmakokiController)
        {
            Log($"Stage {Id}: Moving {axis} (index {axisIndex}) by {amount:F0} steps, current pos: {posBefore:F4} mm");
        }
        else
        {
            Log($"Stage {Id}: Moving {axis} (index {axisIndex}) by {amount:F4} mm, current pos: {posBefore:F4} mm");
        }

        Controller.MoveRelative(axisIndex, amount);

        // Small delay to ensure position is updated (especially for Sigmakoki)
        if (Controller is SigmakokiController)
        {
            System.Threading.Thread.Sleep(100);
        }

        // Get position AFTER move to verify
        double posAfter = Controller.GetPosition(axisIndex);
        double actualMove = posAfter - posBefore;
        Log($"Stage {Id}: Move completed. New pos: {posAfter:F4} mm, actual delta: {actualMove:F4} mm");

        // Warning if movement doesn't match
        if (Math.Abs(actualMove) < Math.Abs(amount) * 0.9)
        {
            Log($"Stage {Id}: WARNING - Actual movement ({actualMove:F4}) less than requested ({amount:F4})!");
        }
    }

    /// <summary>
    /// Move an axis to an absolute position
    /// </summary>
    public void MoveAxisAbsolute(AxisType axis, double position)
    {
        if (!IsConnected || Controller == null)
        {
            throw new Exception($"Stage {Id} is not connected");
        }

        int axisIndex = GetAxisIndex(axis);
        if (axisIndex < 0)
        {
            throw new Exception($"Axis {axis} is not controlled by stage {Id}");
        }

        // Get position BEFORE move
        double posBefore = Controller.GetPosition(axisIndex);
        Log($"Stage {Id}: Moving {axis} (index {axisIndex}) to absolute position {position:F4} mm, current pos: {posBefore:F4} mm");

        // Use SigmakokiController's MoveAbsolute method
        if (Controller is SigmakokiController sigmakokiController)
        {
            sigmakokiController.MoveAbsolute(axisIndex, position);
        }
        else
        {
            // Fallback: calculate relative distance for other controllers
            double distance = position - posBefore;
            Controller.MoveRelative(axisIndex, distance);
        }

        // Get position AFTER move to verify
        double posAfter = Controller.GetPosition(axisIndex);
        Log($"Stage {Id}: Absolute move completed. New pos: {posAfter:F4} mm");
    }

    /// <summary>
    /// Check if this stage controls a specific axis
    /// </summary>
    public bool ControlsAxis(AxisType axis)
    {
        return EnabledAxes.Contains(axis);
    }

    /// <summary>
    /// Get current position of an axis
    /// </summary>
    /// <param name="axis">The axis to query</param>
    /// <returns>Current position, or 0 if not connected</returns>
    public double GetAxisPosition(AxisType axis)
    {
        if (!IsConnected || Controller == null) return 0.0;

        int axisIndex = GetAxisIndex(axis);
        if (axisIndex < 0) return 0.0;

        try
        {
            return Controller.GetPosition(axisIndex);
        }
        catch (Exception ex)
        {
            Log($"Stage {Id}: Error getting position for axis {axis}: {ex.Message}");
            return 0.0;
        }
    }

    /// <summary>
    /// Reference (home) all axes
    /// </summary>
    public void ReferenceAxes()
    {
        if (!IsConnected || Controller == null)
        {
            throw new Exception($"Stage {Id} is not connected");
        }

        Log($"Stage {Id}: Starting axis referencing...");
        Controller.ReferenceAxes();
        Log($"Stage {Id}: Axis referencing completed");
    }

    /// <summary>
    /// Check if all axes are referenced
    /// </summary>
    public bool AreAxesReferenced()
    {
        if (!IsConnected || Controller == null) return false;

        try
        {
            return Controller.AreAxesReferenced();
        }
        catch (Exception ex)
        {
            Log($"Stage {Id}: Error checking reference status: {ex.Message}");
            return false;
        }
    }
}
