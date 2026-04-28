using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using singalUI.libs;
using PI;

namespace PIController.StageController;

public class PIController : global::StageController {
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
    /// PI controllers use MILLIMETERS for linear axes and DEGREES for rotation axes.
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

        // Position is returned in mm for linear axes, degrees for rotation
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
