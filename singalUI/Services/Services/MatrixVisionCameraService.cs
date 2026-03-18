using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using mv.impact.acquire;
using mv.impact.acquire.GenICam;
using OpenCvSharp;

namespace singalUI.Services
{
    /// <summary>
    /// Service for controlling Matrix Vision GigE cameras using mvIMPACT Acquire
    /// Optimized for real-time performance matching Python/Harvesters behavior
    /// </summary>
    public class MatrixVisionCameraService : IDisposable
    {
        private const int RequestCount = 6;
        private Device? _device;
        private FunctionInterface? _functionInterface;
        private CancellationTokenSource? _captureCts;
        private Task? _captureTask;
        private bool _isAcquiring;
        private readonly object _lock = new();

        // Double-buffering: capture writes to back, UI reads from front
        // Swap happens atomically under lock after each frame
        private byte[]? _frontBuffer;  // UI reads this (stable)
        private byte[]? _backBuffer;   // Capture writes this
        private int _frontWidth;
        private int _frontHeight;
        private long _frontSeq;
        private readonly object _bufferLock = new();

        public event Action<long>? FrameUpdated;  // Notify UI new frame available (seq only)
        public event Action<string>? StatusChanged;
        public event Action<bool>? ConnectionChanged;

        public bool IsConnected { get; private set; }
        public string CameraSerial { get; set; } = "";  // Empty = use first available camera
        public int ImageWidth { get; private set; }
        public int ImageHeight { get; private set; }

        /// <summary>
        /// Get a snapshot of the latest frame buffer for UI rendering.
        /// Returns (buffer, width, height, seq) tuple under lock.
        /// Buffer is stable (front buffer) and won't be overwritten by capture thread.
        /// </summary>
        public (byte[]? buffer, int width, int height, long seq) GetLatestFrameSnapshot()
        {
            lock (_bufferLock)
            {
                return (_frontBuffer, _frontWidth, _frontHeight, _frontSeq);
            }
        }

        /// <summary>
        /// Initialize the camera service and connect to the camera
        /// </summary>
        public bool Initialize()
        {
            try
            {
                StatusChanged?.Invoke("Initializing camera manager...");
                Console.WriteLine("=== [MatrixVision] Working Directory: " + Environment.CurrentDirectory);
                Console.WriteLine("=== [MatrixVision] Current Directory: " + Directory.GetCurrentDirectory());

                // Check if mvGenTLProducer.cti exists in current directory
                string ctiPath = Path.Combine(Environment.CurrentDirectory, "mvGenTLProducer.cti");
                bool ctiExists = File.Exists(ctiPath);
                Console.WriteLine($"=== [MatrixVision] CTI file exists: {ctiExists} ({ctiPath}) ===");

                // List all CTI files in current directory
                string[] ctiFiles = Directory.GetFiles(Environment.CurrentDirectory, "*.cti");
                Console.WriteLine($"=== [MatrixVision] CTI files in directory: {string.Join(", ", ctiFiles)} ===");

                // Log MVIMPACT_ACQUIRE environment variable
                string? mvPath = Environment.GetEnvironmentVariable("MVIMPACT_ACQUIRE");
                Console.WriteLine($"=== [MatrixVision] MVIMPACT_ACQUIRE: {mvPath ?? "NOT SET"}");

                Console.WriteLine("=== [MatrixVision] Updating device list ===");
                DeviceManager.updateDeviceList();

                Console.WriteLine($"=== [MatrixVision] Device count: {DeviceManager.deviceCount} ===");
                if (DeviceManager.deviceCount == 0)
                {
                    StatusChanged?.Invoke("No devices found - Check camera connection and network");
                    Console.WriteLine("=== [MatrixVision] ERROR: No devices found! ===");
                    Console.WriteLine("=== [MatrixVision] Troubleshooting: ===");
                    Console.WriteLine("  1. Check if camera is powered on");
                    Console.WriteLine("  2. Check Ethernet cable connection");
                    Console.WriteLine("  3. Run wxPropView.exe to verify camera detection");
                    Console.WriteLine("  4. Check network adapter configuration for GigE");
                    Console.WriteLine("  5. Ensure mvGenTLProducer.cti is in the working directory");
                    Console.WriteLine("  6. Check that Matrix Vision DLLs are accessible");
                    return false;
                }

                // Log all detected devices
                for (int i = 0; i < DeviceManager.deviceCount; i++)
                {
                    var d = DeviceManager.getDevice(i);
                    try
                    {
                        // GenICam control classes require GenICam interface layout.
                        ConfigureGenICamInterfaceLayout(d);
                        d.open();
                        Console.WriteLine($"=== [MatrixVision] Device {i}: Serial={d.serial.read()}, Family={d.family.read()}, Product={d.product.read()} ===");
                        d.close();
                    }
                    catch { }
                }

                // Try to find camera by serial number
                bool found = false;
                for (int i = 0; i < DeviceManager.deviceCount; i++)
                {
                    var d = DeviceManager.getDevice(i);
                    try
                    {
                        ConfigureGenICamInterfaceLayout(d);
                        Console.WriteLine($"=== [MatrixVision] Attempting to open device {i} ===");
                        d.open();
                        if (d.serial.read() == CameraSerial)
                        {
                            _device = d;
                            found = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"=== [MatrixVision] ERROR opening device {i}: {ex.Message} ===");
                    }
                    finally
                    {
                        if (!found)
                        {
                            try { d.close(); } catch { }
                        }
                    }
                }

                // Fallback to first device if not found by serial
                if (!found)
                {
                    _device = DeviceManager.getDevice(0);
                    if (_device == null)
                    {
                        StatusChanged?.Invoke("Failed to get device");
                        return false;
                    }

                    try
                    {
                        ConfigureGenICamInterfaceLayout(_device);
                        Console.WriteLine("=== [MatrixVision] Attempting to open device ===");
                        _device.open();
                        Console.WriteLine("=== [MatrixVision] Device opened successfully ===");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"=== [MatrixVision] ERROR opening device: {ex.Message} ===");
                        Console.WriteLine($"=== [MatrixVision] Stack trace: {ex.StackTrace} ===");
                        StatusChanged?.Invoke($"Failed to open device: {ex.Message}");
                        return false;
                    }
                }

                StatusChanged?.Invoke($"Connected: {_device?.family.read()} / {_device?.product.read()}");

                // Initialize function interface
                _functionInterface = new FunctionInterface(_device);

                // Apply GenICam settings (matching Python version)
                ApplyGenICamSettings();

                IsConnected = true;
                ConnectionChanged?.Invoke(true);
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Apply GenICam settings matching Python/Harvesters version
        /// Python: nm = ia.remote_device.node_map
        /// </summary>
        private void ApplyGenICamSettings()
        {
            if (_device == null)
            {
                Console.WriteLine("=== [MatrixVision] ERROR: _device is null in ApplyGenICamSettings ===");
                return;
            }

            Console.WriteLine("=== Applying GenICam settings (Python equivalent) ===");

            try
            {
                // Use multiple in-flight requests to avoid idle gaps between frames.
                // Single-buffer mode can cap throughput significantly.
                // C# mvIMPACT: SystemSettings.requestCount
                var sysSettings = new SystemSettings(_device);
                if (sysSettings.requestCount.isValid)
                {
                    sysSettings.requestCount.write(RequestCount);
                    int actualCount = sysSettings.requestCount.read();
                    Console.WriteLine($"num_buffers = {RequestCount} (requestCount verified: {actualCount})");
                }

                // Python: if hasattr(nm, "PixelFormat"): nm.PixelFormat.value = "Mono8"
                var imageFormatCtrl = new ImageFormatControl(_device);
                if (imageFormatCtrl.pixelFormat.isValid)
                {
                    try
                    {
                        imageFormatCtrl.pixelFormat.write((long)TImageBufferPixelFormat.ibpfMono8);
                        Console.WriteLine("PixelFormat = Mono8");
                    }
                    catch { }
                }

                // Python: for n in ["ExposureAuto", "GainAuto"]: getattr(nm, n).value = "Off"
                var acqCtrl = new AcquisitionControl(_device);
                if (acqCtrl.exposureAuto.isValid)
                {
                    try
                    {
                        acqCtrl.exposureAuto.write(0);  // 0 = "Off"
                        Console.WriteLine("ExposureAuto = Off");
                    }
                    catch { }
                }

                var analogCtrl = new AnalogControl(_device);
                if (analogCtrl.gainAuto.isValid)
                {
                    try
                    {
                        analogCtrl.gainAuto.write(0);  // 0 = "Off"
                        Console.WriteLine("GainAuto = Off");
                    }
                    catch { }
                }

                // Python: nm.ExposureTime.value = 20000.0
                if (acqCtrl.exposureTime.isValid)
                {
                    try
                    {
                        acqCtrl.exposureTime.write(20000.0);
                        Console.WriteLine("ExposureTime = 20000.0");
                    }
                    catch { }
                }

                // Python: nm.Gain.value = 0.0
                if (analogCtrl.gain.isValid)
                {
                    try
                    {
                        analogCtrl.gain.write(0.0);
                        Console.WriteLine("Gain = 0.0");
                    }
                    catch { }
                }

                // Python: nm.GevSCPSPacketSize.value = 1500
                // Python: nm.GevSCPD.value = 0
                // Python: nm.GevSCCFGUnconditionalStreaming.value = False
                var tlCtrl = new TransportLayerControl(_device);
                if (tlCtrl.gevSCPSPacketSize.isValid)
                {
                    try
                    {
                        tlCtrl.gevSCPSPacketSize.write(1500);
                        Console.WriteLine("GevSCPSPacketSize = 1500");
                    }
                    catch { }
                }

                if (tlCtrl.gevSCPD.isValid)
                {
                    try
                    {
                        tlCtrl.gevSCPD.write(0);
                        Console.WriteLine("GevSCPD = 0");
                    }
                    catch { }
                }

                if (tlCtrl.gevSCCFGUnconditionalStreaming.isValid)
                {
                    try
                    {
                        tlCtrl.gevSCCFGUnconditionalStreaming.write(TBoolean.bFalse);
                        Console.WriteLine("GevSCCFGUnconditionalStreaming = False");
                    }
                    catch { }
                }

                // Python: nm.AcquisitionFrameRateEnable.value = True
                // Python: nm.AcquisitionFrameRate.value = 30.0
                if (acqCtrl.acquisitionFrameRateEnable.isValid)
                {
                    try
                    {
                        acqCtrl.acquisitionFrameRateEnable.write(TBoolean.bTrue);
                        Console.WriteLine("AcquisitionFrameRateEnable = True");
                    }
                    catch { }
                }

                if (acqCtrl.acquisitionFrameRate.isValid)
                {
                    try
                    {
                        acqCtrl.acquisitionFrameRate.write(30.0);
                        Console.WriteLine("AcquisitionFrameRate = 30.0");
                    }
                    catch { }
                }

                Console.WriteLine("=== GenICam settings applied ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== Warning: Some GenICam settings failed: {ex.Message} ===");
            }
        }

        /// <summary>
        /// Set exposure time in microseconds
        /// </summary>
        public void SetExposure(double exposureUs)
        {
            if (_device == null) return;
            try
            {
                lock (_lock)
                {
                    var acqCtrl = new AcquisitionControl(_device);
                    if (acqCtrl.exposureAuto.isValid)
                    {
                        try { acqCtrl.exposureAuto.write(0); } catch { }
                    }

                    if (acqCtrl.exposureTime.isValid)
                    {
                        acqCtrl.exposureTime.write(exposureUs);
                        double applied = acqCtrl.exposureTime.read();
                        StatusChanged?.Invoke($"Exposure applied: {applied:F1} us");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Exposure apply failed: {ex.Message}");
            }
        }

        private static void ConfigureGenICamInterfaceLayout(Device device)
        {
            try
            {
                // Keep this best-effort to avoid blocking connection if a model doesn't expose it.
                device.interfaceLayout.write(TDeviceInterfaceLayout.dilGenICam);
            }
            catch
            {
                // Ignore and continue; write attempts may still work on some devices.
            }
        }

        /// <summary>
        /// Set gain in dB
        /// </summary>
        public void SetGain(double gain)
        {
            if (_device == null) return;
            try
            {
                lock (_lock)
                {
                    var analogCtrl = new AnalogControl(_device);
                    if (analogCtrl.gainAuto.isValid)
                    {
                        try { analogCtrl.gainAuto.write(0); } catch { }
                    }

                    if (analogCtrl.gain.isValid)
                    {
                        analogCtrl.gain.write(gain);
                        double applied = analogCtrl.gain.read();
                        StatusChanged?.Invoke($"Gain applied: {applied:F2} dB");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Gain apply failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Set frame rate
        /// </summary>
        public void SetFrameRate(double fps)
        {
            if (_device == null) return;
            try
            {
                lock (_lock)
                {
                    var acqCtrl = new AcquisitionControl(_device);
                    if (acqCtrl.acquisitionFrameRateEnable.isValid)
                    {
                        try { acqCtrl.acquisitionFrameRateEnable.write(TBoolean.bTrue); } catch { }
                    }

                    if (acqCtrl.acquisitionFrameRate.isValid)
                    {
                        acqCtrl.acquisitionFrameRate.write(fps);
                        double applied = acqCtrl.acquisitionFrameRate.read();
                        StatusChanged?.Invoke($"FPS applied: {applied:F2}");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"FPS apply failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Read back currently applied camera parameters from device nodes.
        /// </summary>
        public bool TryReadAppliedParameters(out double exposureUs, out double gainDb, out double fps)
        {
            exposureUs = double.NaN;
            gainDb = double.NaN;
            fps = double.NaN;

            if (_device == null) return false;

            try
            {
                lock (_lock)
                {
                    bool any = false;

                    var acqCtrl = new AcquisitionControl(_device);
                    if (acqCtrl.exposureTime.isValid)
                    {
                        exposureUs = acqCtrl.exposureTime.read();
                        any = true;
                    }

                    if (acqCtrl.acquisitionFrameRate.isValid)
                    {
                        fps = acqCtrl.acquisitionFrameRate.read();
                        any = true;
                    }

                    var analogCtrl = new AnalogControl(_device);
                    if (analogCtrl.gain.isValid)
                    {
                        gainDb = analogCtrl.gain.read();
                        any = true;
                    }

                    return any;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Readback failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start continuous image acquisition
        /// </summary>
        public void StartAcquisition()
        {
            lock (_lock)
            {
                if (_isAcquiring || _functionInterface == null)
                    return;

                _isAcquiring = true;
                _captureCts = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(_captureCts.Token));
                StatusChanged?.Invoke("Acquisition started");
            }
        }

        /// <summary>
        /// Stop continuous image acquisition
        /// </summary>
        public void StopAcquisition()
        {
            lock (_lock)
            {
                if (!_isAcquiring)
                    return;

                _isAcquiring = false;
                _captureCts?.Cancel();
                _captureTask?.Wait(1000);
                _captureCts?.Dispose();
                _captureCts = null;

                if (_functionInterface != null)
                {
                    _functionInterface.acquisitionStop();
                }

                StatusChanged?.Invoke("Acquisition stopped");
            }
        }

        /// <summary>
        /// Main capture loop - runs on background thread
        /// Optimized for latest-frame-only semantics (no buffering lag)
        /// </summary>
        private void CaptureLoop(CancellationToken cancellationToken)
        {
            // CRITICAL: Capture local reference before loop starts
            // _functionInterface should never be null after acquisition starts
            var fi = _functionInterface;
            if (fi == null)
            {
                Console.WriteLine("=== CAPTURE ERROR: FunctionInterface is null ===");
                StatusChanged?.Invoke("Cannot start capture: FunctionInterface is null");
                return;
            }

            int frameCount = 0;
            _frontSeq = 0;
            const int TIMEOUT_MS = 200;  // Python: timeout=0.2

            try
            {
                Console.WriteLine("=== CAPTURE: Starting acquisition ===");
                StatusChanged?.Invoke("Starting acquisition...");

                // Python: ia.start()
                fi.acquisitionStart();

                // Pre-queue several requests so camera can stream without host-side gaps.
                for (int i = 0; i < RequestCount; i++)
                {
                    fi.imageRequestSingle();
                }

                Console.WriteLine("=== CAPTURE: Loop started (LATEST-FRAME-ONLY mode) ===");
                StatusChanged?.Invoke("Capturing...");

                while (!cancellationToken.IsCancellationRequested && _isAcquiring)
                {
                    // Python: with ia.fetch(timeout=0.2) as buffer
                    int requestNr = fi.imageRequestWaitFor(TIMEOUT_MS);

                    if (requestNr >= 0 && fi.isRequestNrValid(requestNr))
                    {
                        // Drain backlog first: keep ONLY the most recent request.
                        // Older requests are stale for UI and must be discarded quickly.
                        while (true)
                        {
                            int nrDrain = fi.imageRequestWaitFor(0);
                            if (nrDrain < 0) break;

                            // Discard older request and keep the newest one for processing.
                            DiscardRequest(fi, requestNr);
                            requestNr = nrDrain;
                        }

                        // Process only the latest frame this cycle.
                        HandleRequest(fi, requestNr);

                        frameCount++;
                        if (frameCount % 30 == 0)
                        {
                            Console.WriteLine($"=== CAPTURE: Frame {frameCount} (seq: {_frontSeq}) ===");
                            StatusChanged?.Invoke($"Frames: {frameCount}");
                        }
                    }
                    else if (requestNr < 0)
                    {
                        // Timeout: DO NOT call imageRequestSingle()
                        // This prevents pipeline inflation
                        // Just continue waiting
                    }
                }

                fi.acquisitionStop();
                Console.WriteLine($"=== CAPTURE: Stopped. Total frames: {frameCount}, final seq: {_frontSeq} ===");
                StatusChanged?.Invoke($"Stopped. Frames: {frameCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== CAPTURE ERROR: {ex.Message} ===");
                Console.WriteLine(ex.StackTrace);
                StatusChanged?.Invoke($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle a single camera request - extract image data using double-buffer.
        /// CRITICAL: Every request MUST be unlocked and requeued exactly once.
        /// </summary>
        /// <param name="fi">FunctionInterface instance (non-null, captured from capture loop)</param>
        /// <param name="requestNr">Request number to process</param>
        private void HandleRequest(FunctionInterface fi, int requestNr)
        {
            var req = fi.getRequest(requestNr);

            if (req.isOK)
            {
                int w = req.imageWidth.read();
                int h = req.imageHeight.read();
                IntPtr pData = req.imageData.read();
                int dataSize = req.imageSize.read();

                // Double-buffer: write to back buffer, then swap atomically
                lock (_bufferLock)
                {
                    // CRITICAL: Allocate BOTH buffers if first frame or size changed
                    if (_frontBuffer == null || _frontBuffer.Length != dataSize ||
                        _backBuffer == null || _backBuffer.Length != dataSize)
                    {
                        _frontBuffer = new byte[dataSize];
                        _backBuffer = new byte[dataSize];
                    }

                    // FAST memory copy using Marshal.Copy (much faster than byte-by-byte loop)
                    Marshal.Copy(pData, _backBuffer, 0, dataSize);

                    // Atomic swap: back becomes front (UI reads from stable front buffer)
                    (_frontBuffer, _backBuffer) = (_backBuffer, _frontBuffer);

                    // Update metadata under SAME lock as swap
                    _frontWidth = w;
                    _frontHeight = h;
                    _frontSeq++;
                }

                ImageWidth = w;
                ImageHeight = h;

                // Notify UI that new frame is available (no data passed, just seq)
                FrameUpdated?.Invoke(_frontSeq);
            }

            // CRITICAL: ALWAYS unlock and requeue exactly one request
            // This happens even when !req.isOK to prevent pipeline deadlock
            // NO null-conditional operators - fi is guaranteed non-null
            fi.imageRequestUnlock(requestNr);
            fi.imageRequestSingle();
        }

        /// <summary>
        /// Discard a stale camera request as fast as possible.
        /// CRITICAL: Request still must be unlocked and requeued.
        /// </summary>
        private static void DiscardRequest(FunctionInterface fi, int requestNr)
        {
            fi.imageRequestUnlock(requestNr);
            fi.imageRequestSingle();
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            StopAcquisition();

            _functionInterface?.Dispose();
            _functionInterface = null;

            _device?.close();
            _device = null;

            IsConnected = false;
            ConnectionChanged?.Invoke(false);
        }
    }
}
