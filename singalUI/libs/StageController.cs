using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PI;



  public abstract class StageController {
    public abstract bool CheckConnected();
    public abstract void MoveRelative(int axes, double amount);
    public abstract string[] GetAxesList();
    public abstract void Connect();
    public abstract void Disconnect();
    /// <summary>
    /// Get current position of an axis
    /// </summary>
    /// <param name="axisIndex">0-based axis index</param>
    /// <returns>
    /// Current position:
    /// - PI Controller: nanometers (nm) for linear axes, microradians (µrad) for rotation axes
    /// - SigmaKoki: micrometers (µm) for linear axes, degrees (°) for rotation axes
    /// </returns>
    public abstract double GetPosition(int axisIndex);

    /// <summary>
    /// Get minimum travel limit for an axis
    /// </summary>
    public abstract double GetMinTravel(int axisIndex);

    /// <summary>
    /// Get maximum travel limit for an axis
    /// </summary>
    public abstract double GetMaxTravel(int axisIndex);

    /// <summary>
    /// Reference (home) all axes
    /// </summary>
    public abstract void ReferenceAxes();

    /// <summary>
    /// Check if all axes are referenced
    /// </summary>
    public abstract bool AreAxesReferenced();
  }

/// <summary>
/// Serial stages use <see cref="System.IO.Ports.SerialPort"/>; on macOS/Linux the runtime throws
/// <see cref="PlatformNotSupportedException"/> ("only supported on Windows"). Fail fast with a clear message.
/// </summary>
internal static class StageSerialPortPlatform
{
    public static void RequireWindowsForComPort(string hardwareDisplayName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                $"{hardwareDisplayName} requires Windows: COM/RS-232 uses System.IO.Ports, which is only supported on Windows in this build. Run the published app on a Windows PC to connect this stage.");
    }
}

/// <summary>
/// SigmaKoki controller implementation
/// Units: MICROMETERS (µm) for linear axes, DEGREES (°) for rotation axes
/// All position and movement values are in these units.
/// </summary>
public class SigmakokiController : StageController {

    private string[] axes = Array.Empty<string>();
    private SKSampleClass.Controller_Type _controllerType = SKSampleClass.Controller_Type.HSC_103;
    private System.IO.Ports.SerialPort? _serialPort;
    private readonly object _lock = new();
    private string _portName = "COM3"; // Default port
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "singalui_sigmakoki.log");
    private const int QMinIntervalMs = 1500;
    private long _lastQTimestampMs = 0;
    private string? _lastQResponse;

    // Steps per mm conversion factor (adjust based on your stage model)
    // Common values: 1000, 2000, 5000 steps/mm depending on stage
    private const double StepsPerMm = 1000.0;

    // Flag to prevent polling during moves
    private bool _isMoving = false;
    public bool IsMoving => _isMoving;

    public SigmakokiController(SKSampleClass.Controller_Type controllerType = SKSampleClass.Controller_Type.HSC_103)
    {
        _controllerType = controllerType;
    }

    public SigmakokiController()
    {
        _controllerType = SKSampleClass.Controller_Type.HSC_103;
    }

    public void SetPort(string portName)
    {
        _portName = portName;
        if (_serialPort != null)
        {
            _serialPort.PortName = portName;
        }
    }

    public void SetControllerType(SKSampleClass.Controller_Type type)
    {
        _controllerType = type;
    }

    /// <summary>
    /// Set movement speed for an axis
    /// </summary>
    /// <param name="axisIndex">0-based index (0 = first axis)</param>
    /// <param name="slow">Starting speed</param>
    /// <param name="fast">Maximum speed</param>
    /// <param name="rate">Acceleration rate (0-1000)</param>
    public void SetSpeed(int axisIndex, int slow, int fast, int rate = 200)
    {
        if (axes.Length == 0)
        {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }
        if (axisIndex < 0 || axisIndex >= axes.Length)
        {
            throw new Exception($"Invalid axis index {axisIndex}. Available: 0-{axes.Length-1}");
        }

        // HIT MODE format: D:axisNumber slow fast rate (concatenated, no commas)
        // Example: D:1200020000200 for axis 1
        int axisNumber = axisIndex + 1;
        string cmd = $"D:{axisNumber}{slow}{fast}{rate}";
        SendCommand(cmd);
        Log($"Speed set for axis {axisNumber}: slow={slow}, fast={fast}, rate={rate}");
    }

    public override void Connect()
    {
        lock (_lock)
        {
            try
            {
                StageSerialPortPlatform.RequireWindowsForComPort("Sigma Koki (serial)");
                _serialPort = new System.IO.Ports.SerialPort();
                _serialPort.PortName = _portName;
                _serialPort.BaudRate = 38400;  // HSC-103 requires 38400!
                _serialPort.Parity = System.IO.Ports.Parity.None;
                _serialPort.DataBits = 8;
                _serialPort.StopBits = System.IO.Ports.StopBits.One;
                _serialPort.ReadTimeout = 5000;
                _serialPort.WriteTimeout = 5000;
                _serialPort.RtsEnable = true;   // Required for HSC-103
                _serialPort.NewLine = "\r\n";   // Required for SIGMA KOKI protocol

                _serialPort.Open();
                Log($"[Sigmakoki] Connected to {_serialPort.PortName} at {_serialPort.BaudRate} baud");

                // Initialize axes based on controller type
                InitializeAxes();

                // Note: Speed setting may not work on all controllers
                // The stage will use default speed if speed command fails
                try
                {
                    SetSpeed(0, 2000, 20000, 200);
                    if (axes.Length > 1) SetSpeed(1, 2000, 20000, 200);
                    if (axes.Length > 2) SetSpeed(2, 2000, 20000, 200);
                }
                catch (Exception ex)
                {
                    Log($"[Sigmakoki] Warning: Could not set speed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Connection failed: {ex.Message}");
            }
        }
    }

    private static void Log(string message)
    {
        string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.WriteLine(logMessage);
        try
        {
            File.AppendAllText(LogFile, logMessage + "\n");
        }
        catch { }
    }

    private string SendCommand(string command)
    {
        lock (_lock)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                throw new Exception("Not connected");
            }

            if (command.Trim() == "Q:")
            {
                long now = Environment.TickCount64;
                long elapsed = now - _lastQTimestampMs;
                if (_lastQResponse != null && elapsed >= 0 && elapsed < QMinIntervalMs)
                {
                    Log($"[Sigmakoki] Q: rate-limited ({elapsed}ms), returning cached response");
                    return _lastQResponse;
                }
            }

            // Check CTS before sending (optional - some cables don't have CTS)
            if (_serialPort.CtsHolding != true)
            {
                Log($"[Sigmakoki] WARNING: CTS not ready for command {command}, proceeding anyway...");
                // Don't throw - just proceed. Some USB-Serial adapters don't handle CTS correctly.
            }

            _serialPort.DiscardInBuffer();
            Log($"[Sigmakoki] Sending: {command}");
            _serialPort.WriteLine(command);
            System.Threading.Thread.Sleep(200);  // SDK uses 200ms wait

            string response = _serialPort.ReadLine();
            Log($"[Sigmakoki] CMD: {command.Trim()} -> RESP: {response.Trim()}");
            if (command.Trim() == "Q:")
            {
                _lastQTimestampMs = Environment.TickCount64;
                _lastQResponse = response;
            }
            return response;
        }
    }

    private string SendCommandAndWait(string command, int waitMs = 200)
    {
        lock (_lock)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                throw new Exception("Not connected");
            }

            // Check CTS before sending (required by SIGMA KOKI controllers)
            if (_serialPort.CtsHolding != true)
            {
                Log("[Sigmakoki] WARNING: CTS not ready, waiting...");
                System.Threading.Thread.Sleep(100);
                if (_serialPort.CtsHolding != true)
                {
                    throw new Exception("CTS not ready - controller may not be responding");
                }
            }

            _serialPort.DiscardInBuffer();
            _serialPort.WriteLine(command);
            System.Threading.Thread.Sleep(waitMs);

            string response = _serialPort.ReadLine();
            Log($"[Sigmakoki] CMD: {command.Trim()} -> RESP: {response.Trim()}");
            return response;
        }
    }

    private void InitializeAxes()
    {
        // SIGMA KOKI axes - use standard axis names for compatibility with UI
        int maxAxes = GetMaxAxes();
        axes = new string[maxAxes];
        
        // Map to standard axis names: X, Y, Z, U (4th axis if available)
        string[] standardNames = { "X", "Y", "Z", "U" };
        for (int i = 0; i < maxAxes; i++)
        {
            axes[i] = i < standardNames.Length ? standardNames[i] : (i + 1).ToString();
        }
        Log($"[Sigmakoki] Axes initialized: [{string.Join(", ", axes)}]");
    }

    private int GetMaxAxes()
    {
        // Return number of axes based on controller type
        return _controllerType switch
        {
            SKSampleClass.Controller_Type.GIP_101B => 1,
            SKSampleClass.Controller_Type.GSC01 => 1,
            SKSampleClass.Controller_Type.SHOT_302GS => 2,
            SKSampleClass.Controller_Type.HSC_103 => 3,
            SKSampleClass.Controller_Type.SHOT_304GS => 4,
            SKSampleClass.Controller_Type.Hit_MV => 4,
            SKSampleClass.Controller_Type.PGC_04 => 4,
            _ => 3
        };
    }

    public override string[] GetAxesList()
    {
        if (axes.Length == 0)
        {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }
        return axes;
    }

    public override bool CheckConnected()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return false;

        try
        {
            // Query position to verify connection
            string response = SendCommand("Q:");
            return !string.IsNullOrEmpty(response);
        }
        catch (Exception ex)
        {
            Log($"[Sigmakoki] CheckConnected exception: {ex.Message}");
            return false;
        }
    }

    public override void Disconnect()
    {
        lock (_lock)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
            }
            axes = Array.Empty<string>();
            Log("[Sigmakoki] Disconnected from the stage.");
        }
    }

    public override void MoveRelative(int index, double amount)
    {
        Log($"Move Relative is called: index = [{index}], amount = {amount}");
        
        if (axes.Length == 0)
        {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }
        if (index < 0 || index >= axes.Length)
        {
            throw new Exception($"Invalid axis index {index}. Available: 0-{axes.Length-1}");
        }

        _isMoving = true;
        try
        {
            // Use raw amount as steps (UI provides step count)
            int steps = (int)Math.Round(amount);

            // Build command in HIT MODE format (HSC-103, Hit_MV, PGC-04):
            // Axis 1: M:+100 or M:-100
            // Axis 2: M:,+100 or M:,-100
            // Axis 3: M:,,+100 or M:,,-100
            // Axis 4: M:,,,+100 or M:,,,-100
            string direction = steps >= 0 ? "+" : "-";
            int absValue = Math.Abs(steps);

            // Build comma prefix for the axis (index 0 = axis 1 = no commas)
            string commaPrefix = new string(',', index);
            string cmd = $"M:{commaPrefix}{direction}{absValue}";

            Log($"[Sigmakoki] MoveRelative axis {index + 1} by {amount} (steps={steps})");

            string response = SendCommand(cmd);
            if (!response.Contains("OK") && !response.Contains("ok"))
            {
                throw new Exception($"Move failed: {response}");
            }

            // Wait for movement to complete
            WaitReady(index);
        }
        finally
        {
            _isMoving = false;
        }
    }

    /// <summary>
    /// Wait for the stage to be ready (not moving)
    /// </summary>
    private void WaitReady(int axisIndex, int timeoutMs = 30000)
    {
        int elapsed = 0;
        int pollInterval = 50;

        while (elapsed < timeoutMs)
        {
            try
            {
                // Use !: command to check motion status for Hit mode
                // Response format: "status1,status2,status3" where 0=ready, 1=moving
                string response = SendCommandNoLog("!:");
                string[] parts = response.Split(',');

                // Check if the specific axis is ready
                if (axisIndex < parts.Length)
                {
                    string status = parts[axisIndex].Trim();
                    if (status == "0")
                    {
                        Log($"[Sigmakoki] Axis {axisIndex + 1} ready after {elapsed}ms");
                        // Small delay to ensure position register is updated
                        System.Threading.Thread.Sleep(50);
                        return;
                    }
                }
                // If we can't parse, check if all are ready
                else if (parts.All(p => p.Trim() == "0"))
                {
                    Log($"[Sigmakoki] All axes ready after {elapsed}ms");
                    System.Threading.Thread.Sleep(50);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"[Sigmakoki] WaitReady error: {ex.Message}");
            }

            System.Threading.Thread.Sleep(pollInterval);
            elapsed += pollInterval;
        }

        Log($"[Sigmakoki] WaitReady timeout after {timeoutMs}ms");
    }

    /// <summary>
    /// Send command without logging (for polling)
    /// </summary>
    private string SendCommandNoLog(string command)
    {
        lock (_lock)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                throw new Exception("Not connected");
            }

            _serialPort.DiscardInBuffer();
            _serialPort.WriteLine(command);
            System.Threading.Thread.Sleep(50);

            return _serialPort.ReadLine();
        }
    }

    /// <summary>
    /// Move to absolute position
    /// </summary>
    public void MoveAbsolute(int index, double position)
    {
        if (axes.Length == 0)
        {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }
        if (index < 0 || index >= axes.Length)
        {
            throw new Exception($"Invalid axis index {index}. Available: 0-{axes.Length-1}");
        }

        _isMoving = true;
        try
        {
            Log($"Move Absolute is called: index = [{index}], position = {position}");
            // Convert mm to steps
            int steps = (int)(position * StepsPerMm);

            // Build command in HIT MODE format:
            // Axis 1: A:+1000 or A:-1000
            // Axis 2: A:,+1000 or A:,-1000
            // Axis 3: A:,,+1000 or A:,,-1000
            string direction = steps >= 0 ? "+" : "-";
            int absValue = Math.Abs(steps);

            string commaPrefix = new string(',', index);
            string cmd = $"A:{commaPrefix}{direction}{absValue}";

            Log($"[Sigmakoki] MoveAbsolute axis {index + 1} to {position}mm ({steps} steps) Command: [{cmd}]");

            string response = SendCommand(cmd);
            if (!response.Contains("OK") && !response.Contains("ok"))
            {
                throw new Exception($"Move failed: {response}");
            }

            // Wait for movement to complete
            WaitReady(index);
        }
        finally
        {
            _isMoving = false;
        }
    }

    /// <summary>
    /// JOG move - continuous movement
    /// </summary>
    public void Jog(int index, bool positive)
    {
        if (axes.Length == 0)
        {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }
        if (index < 0 || index >= axes.Length)
        {
            throw new Exception($"Invalid axis index {index}. Available: 0-{axes.Length-1}");
        }

        // Build command in HIT MODE format:
        // Axis 1: J:+ or J:-
        // Axis 2: J:,+ or J:,-
        // Axis 3: J:,,+ or J:,,-
        string direction = positive ? "+" : "-";

        string commaPrefix = new string(',', index);
        string cmd = $"J:{commaPrefix}{direction}";

        Log($"[Sigmakoki] JOG axis {index + 1} direction {(positive ? "+" : "-")}");

        SendCommand(cmd);
    }

    /// <summary>
    /// Stop stage (deceleration stop)
    /// </summary>
    public void Stop(int index)
    {
        if (axes.Length == 0) return;

        // Build command in HIT MODE format:
        // Axis 1: L:1
        // Axis 2: L:,1
        // Axis 3: L:,,1
        string commaPrefix = new string(',', index);
        string cmd = $"L:{commaPrefix}1";

        Log($"[Sigmakoki] Stop axis {index + 1}");
        SendCommand(cmd);
    }

    /// <summary>
    /// Emergency stop all axes
    /// </summary>
    public void EmergencyStop()
    {
        string cmd = "L:E";
        Log("[Sigmakoki] Emergency stop all axes");
        SendCommand(cmd);
    }

    public override double GetPosition(int axisIndex)
    {
        if (axes.Length == 0)
        {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }
        if (axisIndex < 0 || axisIndex >= axes.Length)
        {
            throw new Exception($"Invalid axis index {axisIndex}. Available: 0-{axes.Length-1}");
        }

        // Skip polling if a move is in progress to avoid conflicts
        if (_isMoving)
        {
            return 0.0; // Return 0 during moves - actual position will be read after move completes
        }

        try
        {
            // Q: returns positions in steps like "100,200,300"
            string response = SendCommand("Q:");
            string[] parts = response.Split(',');

            if (axisIndex < parts.Length)
            {
                if (int.TryParse(parts[axisIndex].Trim(), out int steps))
                {
                    // Convert steps to mm
                    return steps / StepsPerMm;
                }
            }
            return 0.0;
        }
        catch (Exception ex)
        {
            Log($"Warning: Could not get position for axis {axisIndex}: {ex.Message}");
            return 0.0;
        }
    }

    /// <summary>
    /// Get all axis positions at once
    /// </summary>
    public double[] GetAllPositions()
    {
        if (axes.Length == 0)
        {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }

        try
        {
            string response = SendCommand("Q:");
            string[] parts = response.Split(',');

            double[] positions = new double[axes.Length];
            for (int i = 0; i < axes.Length && i < parts.Length; i++)
            {
                if (int.TryParse(parts[i].Trim(), out int steps))
                {
                    // Convert steps to mm
                    positions[i] = steps / StepsPerMm;
                }
            }
            return positions;
        }
        catch (Exception ex)
        {
            Log($"Warning: Could not get positions: {ex.Message}");
            return new double[axes.Length];
        }
    }

    public override double GetMinTravel(int axisIndex)
    {
        // Sigma Koki stages - return a reasonable default (in steps)
        return -1000000.0;
    }

    public override double GetMaxTravel(int axisIndex)
    {
        // Sigma Koki stages - return a reasonable default (in steps)
        return 1000000.0;
    }

    public override void ReferenceAxes()
    {
        // Home all axes - H:1 for Hit mode (homes axis 1, or all axes)
        string cmd = "H:1";
        Log("[Sigmakoki] Homing all axes...");

        string response = SendCommandAndWait(cmd, 5000); // Wait 5 seconds for homing

        if (response.Contains("OK") || response.Contains("ok"))
        {
            Log("[Sigmakoki] Homing completed successfully");
        }
        else
        {
            Log($"[Sigmakoki] Homing response: {response}");
        }
    }

    /// <summary>
    /// Home a specific axis
    /// </summary>
    public void HomeAxis(int index)
    {
        if (axes.Length == 0)
        {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }
        if (index < 0 || index >= axes.Length)
        {
            throw new Exception($"Invalid axis index {index}. Available: 0-{axes.Length-1}");
        }

        // Build command in HIT MODE format:
        // Axis 1: H:1
        // Axis 2: H:,1
        // Axis 3: H:,,1
        string commaPrefix = new string(',', index);
        string cmd = $"H:{commaPrefix}1";

        Log($"[Sigmakoki] Homing axis {index + 1}...");

        string response = SendCommandAndWait(cmd, 5000);
        Log($"[Sigmakoki] Home response: {response}");
    }

    public override bool AreAxesReferenced()
    {
        // Check if axes are referenced using !: command
        try
        {
            string response = SendCommand("!:");
            // Response format: "0,0,0" where 0 = ready, 1 = moving
            string[] parts = response.Split(',');
            foreach (var part in parts)
            {
                if (part.Trim() == "1")
                    return false; // Still moving
            }
            return true;
        }
        catch
        {
            return axes.Length > 0;
        }
    }

}

