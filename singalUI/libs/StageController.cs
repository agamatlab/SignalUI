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
        // SIGMA KOKI axes are numbered 1, 2, 3 based on controller type (HSC-103)
        int maxAxes = GetMaxAxes();
        axes = new string[maxAxes];
        for (int i = 0; i < maxAxes; i++)
        {
            axes[i] = (i + 1).ToString();
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
                    // Convert steps to µm (micrometers)
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

public class PIController : StageController {
    internal int ControllerId = -1;
    int[] channels = Array.Empty<int>();
    string[] channelStrings = Array.Empty<string>();
    private double[] minTravel = Array.Empty<double>();  // Min travel limits (mm)
    private double[] maxTravel = Array.Empty<double>();  // Max travel limits (mm)
    private bool isE712Controller = false;
    private readonly object motionLock = new object();
    private const int PI_RESULT_FAILURE = 0;
    private const int PI_TRUE = 1;
    private const int PI_FALSE = 0;
    private const int NUMBEROFSTEPS = 2000;

    /// <summary>
    /// PI controllers use NANOMETERS (nm) for linear axes and MICRORADIANS (µrad) for rotation axes.
    /// All position and movement values are in these units.
    /// </summary>


    public override void Connect()
    {
        const int maxRetries = 5;
        const int retryDelayMs = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Console.WriteLine($"[PI] Starting connection process (attempt {attempt}/{maxRetries})...");
                var connectedUsbController = new StringBuilder(1024);

                Console.WriteLine("[PI] Calling GCS2.EnumerUSB...");
                var noDevices = GCS2.EnumerateUSB(connectedUsbController, 1024, "");

                Console.WriteLine("[PI] Found: " + noDevices + " USB controllers");
                Console.WriteLine("[PI] Controller list: " + connectedUsbController.ToString().Replace("\n", ", "));

                if (noDevices == 0)
                {
                    Console.WriteLine($"[PI] No controllers found on attempt {attempt}");
                    if (attempt < maxRetries)
                    {
                        Console.WriteLine($"[PI] Retrying in {retryDelayMs}ms...");
                        System.Threading.Thread.Sleep(retryDelayMs);
                        continue;
                    }
                    throw new Exception("No PI USB controllers found after " + maxRetries + " attempts. Please check:\n" +
                        "1. PI controller is connected via USB\n" +
                        "2. PI_GCS2_DLL.dll is in the application directory\n" +
                        "3. Required PI drivers are installed\n" +
                        "4. The PI controller is powered on");
                }

                var deviceName = connectedUsbController
                    .ToString()
                    .Split(new[] {Environment.NewLine}, StringSplitOptions.None)
                    [0];

                Console.WriteLine($"[PI] Attempting to connect to: {deviceName}");
                ControllerId = GCS2.ConnectUSB(deviceName);
                Console.WriteLine($"[PI] ConnectUSB returned ID: {ControllerId}");

                if (0 <= ControllerId){
                    PrintControllerIdentification();
                    PrintNamesOfConnectedChannels();  // CRITICAL: Initialize axes!
                    InitializeAxes();  // Enable axes and turn on servo
                    Console.WriteLine($"[PI] Successfully connected! Axes available: {channelStrings.Length}");
                    return;
                }

                Console.WriteLine($"[PI] ERROR: Connection failed with ID: {ControllerId}");
                throw new Exception($"Connection to USB failed (Error code: {ControllerId}). Please ensure PI_GCS2_DLL.dll and its dependencies are in the application directory.");
            }
            catch (DllNotFoundException ex)
            {
                Console.WriteLine($"[PI] ERROR: DLL not found - {ex.Message}");
                throw new Exception("PI_GCS2_DLL.dll or its dependencies not found.\n" +
                    "Please ensure the following files are in the application directory:\n" +
                    "- PI_GCS2_DLL.dll\n" +
                    "- PI_GCS2_DLL_64.dll (for 64-bit applications)\n" +
                    "Original error: " + ex.Message);
            }
            catch (Exception ex)
            {
                if (attempt < maxRetries && !ex.Message.Contains("DLL"))
                {
                    Console.WriteLine($"[PI] Attempt {attempt} failed: {ex.Message}");
                    Console.WriteLine($"[PI] Retrying in {retryDelayMs}ms...");
                    System.Threading.Thread.Sleep(retryDelayMs);
                    continue;
                }
                Console.WriteLine($"[PI] ERROR: {ex.Message}");
                throw new Exception("PI connection failed: " + ex.Message);
            }
        }
    }
    
    private void PrintControllerIdentification(){

            var controllerIdentification = new StringBuilder(1024);

            if (GCS2.qIDN(ControllerId, controllerIdentification, controllerIdentification.Capacity) ==
                PI_RESULT_FAILURE)
            {
                throw (new Exception("qIDN failed. Exiting."));
            }

            string idn = controllerIdentification.ToString();
            isE712Controller = idn.Contains("E-712", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine("qIDN returned: " + idn);
            Console.WriteLine($"[PI] Detected controller family: {(isE712Controller ? "E-712" : "Generic GCS2")}");
    }

    private void PrintNamesOfConnectedChannels()
    {
        StringBuilder axesBuffer = new StringBuilder(1024);

        if (PI.GCS2.qSAI(ControllerId, axesBuffer, axesBuffer.Capacity) == 0)
        {
            throw (new Exception("Could not query names from connected channels."));
        }

        channelStrings = axesBuffer
            .ToString()
            .Split(new[] { ' ', '\t', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        var listChannels = new List<int>();
        for (int index = 0; index < channelStrings.Count(); ++index)
        {
            listChannels.Add(index + 1);
            Console.WriteLine("Name of channel number " + index + ": " + channelStrings[index].Trim());
        }

        channels = listChannels.ToArray();

        if (isE712Controller && channelStrings.All(a => int.TryParse(a, out _)))
        {
            Console.WriteLine("[PI] WARNING: E-712 reported numeric axes [1..n].");
            Console.WriteLine("[PI] WARNING: This typically indicates startup/kinematics are not initialized on the controller.");
            Console.WriteLine("[PI] WARNING: Initialize controller + stage in PIMikroMove and save startup state, then reconnect.");
        }

        // Query travel limits for each axis
        QueryTravelLimits();

        Console.WriteLine();
    }

    /// <summary>
    /// Query travel limits (TMN/TMX) from controller for each axis
    /// </summary>
    private void QueryTravelLimits()
    {
        minTravel = new double[channels.Length];
        maxTravel = new double[channels.Length];

        Console.WriteLine("[PI] Querying travel limits...");

        for (int i = 0; i < channels.Length; i++)
        {
            string axis = channelStrings[i].Trim();

            try
            {
                double[] minVal = new double[1];
                double[] maxVal = new double[1];

                if (GCS2.qTMN(ControllerId, axis, minVal) != PI_RESULT_FAILURE &&
                    GCS2.qTMX(ControllerId, axis, maxVal) != PI_RESULT_FAILURE)
                {
                    minTravel[i] = minVal[0];
                    maxTravel[i] = maxVal[0];
                    Console.WriteLine($"[PI] Axis {axis} travel limits: [{minVal[0]:F4} to {maxVal[0]:F4}] mm or deg");
                }
                else
                {
                    // Use safe defaults if query fails
                    minTravel[i] = -50.0;  // Default: -50mm
                    maxTravel[i] = 50.0;   // Default: +50mm
                    Console.WriteLine($"[PI] Axis {axis} could not query limits, using defaults: [-50 to +50]");
                }
            }
            catch (Exception ex)
            {
                minTravel[i] = -50.0;
                maxTravel[i] = 50.0;
                Console.WriteLine($"[PI] Axis {axis} limit query error: {ex.Message}, using defaults");
            }
        }
    }

    private void InitializeAxes()
    {
        Console.WriteLine("[PI] Initializing axes (enabling and turning on servo)...");

        try
        {
            for (int i = 0; i < channelStrings.Length; i++)
            {
                string axis = channelStrings[i].Trim();

                // Check if axis is turned on
                int[] isOn = { 0 };
                GCS2.qONT(ControllerId, axis, isOn);
                Console.WriteLine($"[PI] Axis {axis} turned on: {isOn[0]}");

                // E-712 reference sample does not use EAX/qEAX; those commands are not supported there.
                if (!isE712Controller)
                {
                    int[] isEnabled = { 0 };
                    GCS2.qEAX(ControllerId, axis, isEnabled);
                    Console.WriteLine($"[PI] Axis {axis} enabled: {isEnabled[0]}");

                    if (isEnabled[0] == PI_FALSE)
                    {
                        Console.WriteLine($"[PI] Enabling axis {axis}...");
                        int[] enableArray = { PI_TRUE };
                        int eaxResult = GCS2.EAX(ControllerId, axis, enableArray);
                        if (eaxResult != PI_RESULT_FAILURE)
                        {
                            Console.WriteLine($"[PI] Axis {axis} enabled successfully");
                            System.Threading.Thread.Sleep(50);
                        }
                        else
                        {
                            Console.WriteLine($"[PI] WARNING: Failed to enable axis {axis}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[PI] Axis {axis}: skipping EAX/qEAX for E-712");
                }

                // Check if servo is available and on (for closed-loop stages)
                int[] servoOn = { 0 };
                int qSvoResult = GCS2.qSVO(ControllerId, axis, servoOn);

                if (qSvoResult != PI_RESULT_FAILURE)
                {
                    Console.WriteLine($"[PI] Axis {axis} servo on: {servoOn[0]}");

                    // Turn on servo if it's available and off
                    if (servoOn[0] == PI_FALSE)
                    {
                        Console.WriteLine($"[PI] Turning on servo for axis {axis}...");
                        int[] servoOnArray = { PI_TRUE };
                        int svoResult = GCS2.SVO(ControllerId, axis, servoOnArray);
                        if (svoResult != PI_RESULT_FAILURE)
                        {
                            Console.WriteLine($"[PI] Servo turned on for axis {axis}");
                            System.Threading.Thread.Sleep(100);
                        }
                    }
                }

                int[] refState = { 0 };
                if (GCS2.qFRF(ControllerId, axis, refState) != PI_RESULT_FAILURE)
                {
                    Console.WriteLine($"[PI] Axis {axis} referenced: {refState[0]}");
                }
            }

            Console.WriteLine("[PI] Axis initialization complete\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PI] WARNING: Axis initialization had issues: {ex.Message}");
        }
    }
    
    public override string[] GetAxesList(){
        if (ControllerId == -1){
            throw new Exception("Device has not been connected yet.");
        }
        if (channelStrings.Length == 0) {
            throw new Exception("Axes not initialized. This shouldn't happen after Connect().");
        }
        return channelStrings;
    }


    public override bool CheckConnected()
    {
        if (ControllerId < 0)
            return false;

        // Query the PI controller to verify connection is alive
        try
        {
            int connected = GCS2.IsConnected(ControllerId);
            if (connected == 1)
                return true;

            // Only log if not connected, don't destroy state
            // The wrapper will handle disconnection
            Console.WriteLine($"[PI] CheckConnected: IsConnected returned {connected}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PI] CheckConnected exception: {ex.Message}");
            return false;
        }
    }

    public override void Disconnect()
    {
        if (ControllerId >= 0)
        {
            GCS2.CloseConnection(ControllerId);
            ControllerId = -1;
        }
        channelStrings = Array.Empty<string>();
        channels = Array.Empty<int>();

        Console.WriteLine("Close connection.");
    }


    private void WaitForMotionDone(int currentIndex)
    {
        int[] isMoving = {PI_TRUE};

        while (isMoving[0] == PI_TRUE)
        {
            if (GCS2.IsMoving(ControllerId, channelStrings[currentIndex], isMoving) == PI_RESULT_FAILURE)
            {
                throw new Exception("Unable to retrieve motion state.");
            }

            System.Threading.Thread.Sleep(200);
        }
    }

    public override void MoveRelative(int index, double amount){
        lock (motionLock)
        {
        if (channels.Length == 0 || channelStrings.Length == 0) {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }
        if (index < 0 || index >= channels.Length) {
            throw new Exception($"Invalid axis index {index}. Available: 0-{channels.Length-1}");
        }

        string channelName = channelStrings[index];

        if (!isE712Controller)
        {
            bool anyNotEnabled = false;
            foreach (var axis in channelStrings)
            {
                int[] en = { 0 };
                GCS2.qEAX(ControllerId, axis.Trim(), en);
                if (en[0] == 0) { anyNotEnabled = true; break; }
            }

            if (anyNotEnabled)
            {
                Console.WriteLine($"[PI] Attempting to enable axes...");
                foreach (var axis in channelStrings)
                {
                    int[] en = { PI_TRUE };
                    int eaxResult = GCS2.EAX(ControllerId, axis.Trim(), en);
                    if (eaxResult == 0)
                    {
                        int errCode = GCS2.GetError(ControllerId);
                        StringBuilder errMsg = new StringBuilder(256);
                        GCS2.TranslateError(errCode, errMsg, errMsg.Capacity);
                        Console.WriteLine($"[PI] EAX({axis.Trim()}) failed: {errCode} - {errMsg}");
                    }
                }

                System.Threading.Thread.Sleep(300);

                Console.WriteLine($"[PI] Final enable status:");
                foreach (var axis in channelStrings)
                {
                    int[] en = { 0 };
                    GCS2.qEAX(ControllerId, axis.Trim(), en);
                    Console.WriteLine($"[PI]   {axis.Trim()}: Enabled={en[0]}");
                }
            }
        }
        else
        {
            int[] refState = { 0 };
            if (GCS2.qFRF(ControllerId, channelName, refState) != PI_RESULT_FAILURE && refState[0] == PI_FALSE)
            {
                Console.WriteLine($"[PI] WARNING: Axis {channelName} is not referenced yet (qFRF=0).");
            }
        }

        // Check axis state before moving
        int[] servoOn = { 0 };
        int[] isOn = { 0 };
        GCS2.qSVO(ControllerId, channelName, servoOn);
        GCS2.qONT(ControllerId, channelName, isOn);
        if (isE712Controller)
        {
            Console.WriteLine($"[PI] Axis {channelName} state - ServoOn={servoOn[0]}, On={isOn[0]} (EAX not used on E-712)");
        }
        else
        {
            int[] isEnabled = { 0 };
            GCS2.qEAX(ControllerId, channelName, isEnabled);
            Console.WriteLine($"[PI] Axis {channelName} state - ServoOn={servoOn[0]}, Enabled={isEnabled[0]}, On={isOn[0]}");
        }

        // Turn on servo if off
        if (servoOn[0] == PI_FALSE)
        {
            Console.WriteLine($"[PI] Turning on servo for {channelName}...");
            int[] servoOnArray = { PI_TRUE };
            int svoResult = GCS2.SVO(ControllerId, channelName, servoOnArray);
            Console.WriteLine($"[PI] SVO result: {svoResult}");
            System.Threading.Thread.Sleep(200);
        }

        // Get current position before move
        double[] currentPos = new double[1];
        if (GCS2.qPOS(ControllerId, channelName, currentPos) == PI_RESULT_FAILURE)
        {
            Console.WriteLine($"[PI] WARNING: Could not get current position, assuming 0");
            currentPos[0] = 0;
        }
        Console.WriteLine($"[PI] Current position: {currentPos[0]:F4} mm");

        // Calculate target position (absolute)
        double targetPosition = currentPos[0] + amount;

        // Boundary check
        if (index < minTravel.Length && index < maxTravel.Length)
        {
            if (targetPosition < minTravel[index])
            {
                targetPosition = minTravel[index];
                Console.WriteLine($"[PI] Clamping to min limit: {minTravel[index]:F4}");
            }
            else if (targetPosition > maxTravel[index])
            {
                targetPosition = maxTravel[index];
                Console.WriteLine($"[PI] Clamping to max limit: {maxTravel[index]:F4}");
            }
        }

        double[] moveArray = new double[] { targetPosition };

        Console.WriteLine($"[PI] MOV command: {channelName}, target={targetPosition:F4} mm");

        // Use MOV (absolute) instead of MVR (relative) - more reliable
        int result = GCS2.MOV(ControllerId, channelName, moveArray);
        Console.WriteLine($"[PI] MOV result: {result} (1=success, 0=failure)");

        if (result == PI_RESULT_FAILURE)
        {
            int errorCode = GCS2.GetError(ControllerId);
            StringBuilder errorMsg = new StringBuilder(1024);
            GCS2.TranslateError(errorCode, errorMsg, errorMsg.Capacity);
            Console.WriteLine($"[PI] MOV failed! Error {errorCode}: {errorMsg}");

            // Check if still connected
            int connected = GCS2.IsConnected(ControllerId);
            Console.WriteLine($"[PI] Controller connected: {connected}");
            return;
        }

        // Wait for motion to complete
        Console.WriteLine($"[PI] Waiting for motion to complete...");
        WaitForMotionDone(index);

        // Read back position
        double[] newPos = new double[1];
        if (GCS2.qPOS(ControllerId, channelName, newPos) != PI_RESULT_FAILURE)
        {
            double actualMove = newPos[0] - currentPos[0];
            Console.WriteLine($"[PI] Move completed. Position: {currentPos[0]:F4} -> {newPos[0]:F4} mm (delta: {actualMove:F4} mm)");
        }
        }
    }

    public override double GetPosition(int axisIndex)
    {
        if (channels.Length == 0 || channelStrings.Length == 0)
        {
            throw new Exception("Axes not initialized. Call Connect() first.");
        }
        if (axisIndex < 0 || axisIndex >= channels.Length)
        {
            throw new Exception($"Invalid axis index {axisIndex}. Available: 0-{channels.Length-1}");
        }

        double[] position = new double[1];
        if (GCS2.qPOS(ControllerId, channelStrings[axisIndex], position) == PI_RESULT_FAILURE)
        {
            Console.WriteLine($"Warning: Could not get position for axis {axisIndex}");
            return 0.0;
        }

        // Position is returned in nm for linear axes, µrad for rotation (PI controller)
        return position[0];
    }

    public override double GetMinTravel(int axisIndex)
    {
        if (axisIndex < 0 || axisIndex >= minTravel.Length)
        {
            return -50.0; // Default: -50mm
        }
        return minTravel[axisIndex];
    }

    public override double GetMaxTravel(int axisIndex)
    {
        if (axisIndex < 0 || axisIndex >= maxTravel.Length)
        {
            return 50.0; // Default: +50mm
        }
        return maxTravel[axisIndex];
    }

    public override void ReferenceAxes()
    {
        lock (motionLock)
        {
        Console.WriteLine("[PI] Starting axis referencing (following official PI sample)...");

        // Reference each axis individually (as shown in official PI sample)
        foreach (var axis in channelStrings)
        {
            string axisName = axis.Trim();
            Console.WriteLine($"[PI] Processing axis: {axisName}");

            // Step 1: Check if already referenced
            int[] refState = { -1 };
            if (GCS2.qFRF(ControllerId, axisName, refState) != PI_RESULT_FAILURE)
            {
                if (refState[0] == 1)
                {
                    Console.WriteLine($"[PI] Axis {axisName} is already referenced.");
                    continue;
                }
            }

            if (!isE712Controller)
            {
                // Step 2: Enable axis with EAX
                Console.WriteLine($"[PI] Enabling axis {axisName} with EAX...");
                int[] enableValue = { 1 };
                if (GCS2.EAX(ControllerId, axisName, enableValue) == PI_RESULT_FAILURE)
                {
                    Console.WriteLine($"[PI] WARNING: Could not enable axis {axisName}");
                }

                // Step 3: Set Axis Mode with SAM
                Console.WriteLine($"[PI] Setting axis mode with SAM(0) for {axisName}...");
                if (GCS2.SAM(ControllerId, axisName, 0) == PI_RESULT_FAILURE)
                {
                    Console.WriteLine($"[PI] WARNING: SAM failed for axis {axisName}");
                    int errorCode = GCS2.GetError(ControllerId);
                    StringBuilder errorMsg = new StringBuilder(1024);
                    GCS2.TranslateError(errorCode, errorMsg, errorMsg.Capacity);
                    Console.WriteLine($"[PI] Error: {errorCode} - {errorMsg}");
                }
            }
            else
            {
                // E-712 reference sample turns servo off before FRF.
                Console.WriteLine($"[PI] E-712: setting servo off before FRF for axis {axisName}...");
                int[] servoOff = { PI_FALSE };
                if (GCS2.SVO(ControllerId, axisName, servoOff) == PI_RESULT_FAILURE)
                {
                    Console.WriteLine($"[PI] WARNING: SVO off failed for axis {axisName}");
                }
            }

            System.Threading.Thread.Sleep(100);

            // Step 4: Execute FRF reference move
            Console.WriteLine($"[PI] Starting FRF reference move for {axisName}...");
            int frfResult = GCS2.FRF(ControllerId, axisName);
            Console.WriteLine($"[PI] FRF result for {axisName}: {frfResult}");

            if (frfResult != PI_RESULT_FAILURE)
            {
                // Step 5: Wait for controller ready (from official sample)
                Console.WriteLine($"[PI] Waiting for reference to complete...");
                int isReady = 0;
                int elapsed = 0;
                int maxTries = 120; // 120 seconds timeout

                while (isReady != 1 && elapsed < maxTries)
                {
                    if (GCS2.IsControllerReady(ControllerId, ref isReady) == PI_RESULT_FAILURE)
                    {
                        Console.WriteLine($"[PI] WARNING: Could not get controller ready state");
                    }

                    System.Threading.Thread.Sleep(1000);
                    elapsed++;

                    if (elapsed % 10 == 0)
                    {
                        Console.WriteLine($"[PI] Still referencing... ({elapsed}s)");
                    }
                }

                // Step 6: Verify referencing was successful
                if (GCS2.qFRF(ControllerId, axisName, refState) != PI_RESULT_FAILURE)
                {
                    if (refState[0] == 1)
                    {
                        Console.WriteLine($"[PI] Successfully referenced axis {axisName}");
                    }
                    else
                    {
                        Console.WriteLine($"[PI] WARNING: Axis {axisName} referencing may have failed (state={refState[0]})");
                    }
                }
            }
            else if (!isE712Controller)
            {
                int errorCode = GCS2.GetError(ControllerId);
                StringBuilder errorMsg = new StringBuilder(1024);
                GCS2.TranslateError(errorCode, errorMsg, errorMsg.Capacity);
                Console.WriteLine($"[PI] FRF failed for axis {axisName}: Error {errorCode} - {errorMsg}");

                // Try alternative: INI command
                Console.WriteLine($"[PI] Trying INI command for axis {axisName}...");
                int iniResult = GCS2.INI(ControllerId, axisName);
                Console.WriteLine($"[PI] INI result: {iniResult}");
            }
            else
            {
                int errorCode = GCS2.GetError(ControllerId);
                StringBuilder errorMsg = new StringBuilder(1024);
                GCS2.TranslateError(errorCode, errorMsg, errorMsg.Capacity);
                Console.WriteLine($"[PI] E-712 FRF failed for axis {axisName}: Error {errorCode} - {errorMsg}");
            }
        }

        // Print final status
        Console.WriteLine("[PI] === Final Axis Status ===");
        foreach (var axis in channelStrings)
        {
            string axisName = axis.Trim();
            int[] isRef = { 0 };
            int[] servoOn = { 0 };
            GCS2.qFRF(ControllerId, axisName, isRef);
            GCS2.qSVO(ControllerId, axisName, servoOn);
            if (isE712Controller)
            {
                Console.WriteLine($"[PI] Axis {axisName}: Referenced={isRef[0]}, Servo={servoOn[0]}");
            }
            else
            {
                int[] isEnabled = { 0 };
                GCS2.qEAX(ControllerId, axisName, isEnabled);
                Console.WriteLine($"[PI] Axis {axisName}: Referenced={isRef[0]}, Enabled={isEnabled[0]}, Servo={servoOn[0]}");
            }
        }
        Console.WriteLine("[PI] ReferenceAxes completed");
        }
    }

    public override bool AreAxesReferenced()
    {
        if (channels.Length == 0 || channelStrings.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < channelStrings.Length; i++)
        {
            string axis = channelStrings[i].Trim();
            int[] isReferenced = { 0 };
            if (GCS2.qFRF(ControllerId, axis, isReferenced) == PI_RESULT_FAILURE || isReferenced[0] == PI_FALSE)
            {
                return false;
            }
        }

        return true;
    }

}
