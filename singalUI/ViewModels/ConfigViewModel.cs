using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using singalUI.libs;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace singalUI.ViewModels
{
    public partial class ConfigViewModel : ViewModelBase
    {
        // Static instance for cross-ViewModel communication
        public static ConfigViewModel? Instance { get; private set; }

        private const double DefaultFocalLengthMm = 28.0;
        private const double DefaultPixelSizeMm = 4.4e-3;
        private const int DefaultImageWidth = 640;
        private const int DefaultImageHeight = 480;

        private PoseEstimateResult? _lastResult;
        private const int PoseEstimateTimeoutMs = 180000;
        private int _poseRunSequence = 0;
        private int _poseEstimationRunning = 0;
        private readonly DispatcherTimer _cameraResolutionTimer;
        private readonly string _errorLogFile = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "pose_estimation_errors.log");

        private string _systemErrorLogFile = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "singalui_errors.log");

        // Error log for UI display
        private const int MaxErrorLogLines = 100;

        #region Pattern Analysis Properties

        [ObservableProperty]
        private int _selectedPatternType = 0; // 0 = CheckerBoard, 1 = HMF Coded Target

        [ObservableProperty]
        private double _pitchX = 0.1;

        [ObservableProperty]
        private double _pitchY = 0.1;

        [ObservableProperty]
        private double _componentsX = 11.0;

        [ObservableProperty]
        private double _componentsY = 11.0;

        #endregion

        #region Camera Intrinsics Properties

        [ObservableProperty]
        private double _focalLength = DefaultFocalLengthMm;

        [ObservableProperty]
        private double _pixelSize = DefaultPixelSizeMm; // 5.5um

        [ObservableProperty]
        private double _fx = 6363.64;

        [ObservableProperty]
        private double _fy = 6363.64;

        [ObservableProperty]
        private double _cx = 0.0;

        [ObservableProperty]
        private double _cy = 0.0;

        #endregion

        #region Algorithm Parameters

        [ObservableProperty]
        private double _windowSize = 12.0;

        [ObservableProperty]
        private double _codePitchBlocks = 6.0;

        #endregion

        #region Test Image Configuration

        [ObservableProperty]
        private int _imageWidth = DefaultImageWidth;

        [ObservableProperty]
        private int _imageHeight = DefaultImageHeight;

        [ObservableProperty]
        private string _cameraResolutionStatus = "No connected camera resolution detected";

        #endregion

        #region Result Properties

        [ObservableProperty]
        private bool _hasResults;

        [ObservableProperty]
        private string _resultStatusMessage = "No estimation run yet";

        [ObservableProperty]
        private string _resultStatusColor = "#707070";

        [ObservableProperty]
        private double _rx;

        [ObservableProperty]
        private double _ry;

        [ObservableProperty]
        private double _rz;

        [ObservableProperty]
        private double _tx;

        [ObservableProperty]
        private double _ty;

        [ObservableProperty]
        private double _tz;

        [ObservableProperty]
        private string _rawPoseData = "No data available";

        [ObservableProperty]
        private string _lastEstimationTime = "";

        [ObservableProperty]
        private bool _isPoseEstimationRunning;

        [ObservableProperty]
        private string _runPoseButtonText = "Run Pose Estimation";

        #endregion

        #region Status Properties

        [ObservableProperty]
        private string _estimatorStatus = "Not Initialized";

        [ObservableProperty]
        private string _errorLog = "";

        #endregion

        public ConfigViewModel()
        {
            Instance = this; // Set static instance for cross-ViewModel access
            LogToError("[ConfigViewModel] Constructor started");
            InitializeEstimator();
            _cameraResolutionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _cameraResolutionTimer.Tick += (_, _) => SyncCameraResolutionFromConnectedCamera();
            _cameraResolutionTimer.Start();
            SyncCameraResolutionFromConnectedCamera();
            LogToError("[ConfigViewModel] Constructor completed");
        }

        private void InitializeEstimator()
        {
            try
            {
                EstimatorStatus = "Ready";
                ResultStatusMessage = "Ready to estimate pose";
                ResultStatusColor = "#4caf50";

                LogToError("[ConfigViewModel] PoseEstApi bridge ready");
            }
            catch (InvalidOperationException ex)
            {
                EstimatorStatus = "Initialization Failed";
                ResultStatusMessage = $"Failed: {ex.Message}";
                ResultStatusColor = "#f44336";
                LogToError($"[ConfigViewModel] Initialization error: {ex.Message}");
            }
            catch (Exception ex)
            {
                EstimatorStatus = "Error";
                ResultStatusMessage = $"Error: {ex.Message}";
                ResultStatusColor = "#f44336";
                LogToError($"[ConfigViewModel] Unexpected error: {ex.Message}");
            }
        }

        private void SyncCameraResolutionFromConnectedCamera()
        {
            try
            {
                var cameraService = App.CameraService;
                if (cameraService == null || !cameraService.IsConnected)
                {
                    CameraResolutionStatus = "No connected camera resolution detected";
                    return;
                }

                var width = cameraService.ImageWidth;
                var height = cameraService.ImageHeight;

                if (width <= 0 || height <= 0)
                {
                    var (_, snapshotWidth, snapshotHeight, _) = cameraService.GetLatestFrameSnapshot();
                    width = snapshotWidth;
                    height = snapshotHeight;
                }

                if (width > 0 && height > 0)
                {
                    ImageWidth = width;
                    ImageHeight = height;
                    CameraResolutionStatus = $"Connected camera: {width} x {height}";
                }
                else
                {
                    CameraResolutionStatus = "Camera connected, waiting for resolution...";
                }
            }
            catch (Exception ex)
            {
                CameraResolutionStatus = $"Camera resolution read failed: {ex.Message}";
            }
        }

        private void LogError(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logEntry = $"[{timestamp}] {message}";

                // Prepend to error log string
                ErrorLog = logEntry + "\n" + ErrorLog;

                // Keep only last MaxErrorLogLines lines
                var lines = ErrorLog.Split('\n');
                if (lines.Length > MaxErrorLogLines)
                {
                    ErrorLog = string.Join("\n", lines.Take(MaxErrorLogLines));
                }

                Console.WriteLine($"[ErrorLog] {logEntry}");
            }
            catch
            {
                Console.WriteLine($"[ErrorLog] Failed to write error log: {message}");
            }

            // Also write to file
            try
            {
                System.IO.File.AppendAllText(_systemErrorLogFile, message + System.Environment.NewLine);
            }
            catch { }
        }

        public void ClearErrorLog()
        {
            try
            {
                ErrorLog = "";
                Console.WriteLine("[ErrorLog] Error log cleared");
                System.IO.File.Delete(_systemErrorLogFile);
            }
            catch { }
        }

        #region Commands

        [RelayCommand]
        private async Task RunPoseEstimation()
        {
            int runId = Interlocked.Increment(ref _poseRunSequence);
            if (Interlocked.CompareExchange(ref _poseEstimationRunning, 1, 0) != 0)
            {
                LogToError($"[RunPoseEstimation#{runId}] Skipped: previous run still active");
                return;
            }

            LogToError($"[RunPoseEstimation#{runId}] Starting pose estimation...");

            try
            {
                IsPoseEstimationRunning = true;
                RunPoseButtonText = "Running Pose Estimation...";
                ResultStatusMessage = "Pose estimation running. This may take around 30-60 seconds.";
                ResultStatusColor = "#ff9800";

                var stopwatch = Stopwatch.StartNew();

                // Create camera intrinsics from UI values
                var intrinsics = new CameraIntrinsics
                {
                    Fx = Fx,
                    Fy = Fy,
                    Cx = Cx,
                    Cy = Cy
                };

                var (imageData, imageRows, imageCols, frameSeq) = GetLatestCameraImageData();
                var imagePath = SaveFrameForPoseEstimation(imageData, imageRows, imageCols, frameSeq);

                LogToError($"[RunPoseEstimation#{runId}] Running with: " +
                    $"Image={imageCols}x{imageRows}, " +
                    $"FrameSeq={frameSeq}, " +
                    $"Focal={FocalLength}mm, " +
                    $"PixelSize={PixelSize}mm, " +
                    $"ImagePath={imagePath}, " +
                    $"Components={ComponentsX}x{ComponentsY}");

                LogToError($"[RunPoseEstimation#{runId}] Dispatching native pose call (timeout={PoseEstimateTimeoutMs}ms)");

                var poseTask = Task.Factory.StartNew(() => PoseEstApiBridgeClient.EstimatePoseFromImagePath(
                    imagePath: imagePath,
                    intrinsicMatrix: intrinsics.ToArray(),
                    pixelSize: PixelSize,
                    focalLength: FocalLength,
                    pitchX: PitchX,
                    pitchY: PitchY,
                    componentsX: ComponentsX,
                    componentsY: ComponentsY,
                    windowSize: WindowSize,
                    codePitchBlocks: CodePitchBlocks
                ), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                var completed = await Task.WhenAny(poseTask, Task.Delay(PoseEstimateTimeoutMs));
                if (completed != poseTask)
                {
                    stopwatch.Stop();
                    ResultStatusMessage = $"Pose estimation timed out after {PoseEstimateTimeoutMs} ms";
                    ResultStatusColor = "#f44336";
                    HasResults = false;
                    LogToError($"[RunPoseEstimation#{runId}] TIMEOUT after {stopwatch.ElapsedMilliseconds}ms (native call still running)");
                    return;
                }

                _lastResult = await poseTask;
                stopwatch.Stop();

                // Update UI with results
                UpdateResults(_lastResult);
                LastEstimationTime = $"Last run: {DateTime.Now:HH:mm:ss}";
                PrintPoseResultToDebug(_lastResult, stopwatch.ElapsedMilliseconds, imagePath);

                LogToError($"[RunPoseEstimation#{runId}] Completed in {stopwatch.ElapsedMilliseconds}ms with status: {_lastResult.Status}");
            }
            catch (InvalidOperationException ex)
            {
                ResultStatusMessage = $"Error: {ex.Message}";
                ResultStatusColor = "#f44336";
                HasResults = false;
                LogToError($"[RunPoseEstimation#{runId}] InvalidOperationException: {ex.Message}");
            }
            catch (Exception ex)
            {
                ResultStatusMessage = $"Unexpected error: {ex.Message}";
                ResultStatusColor = "#f44336";
                HasResults = false;
                LogToError($"[RunPoseEstimation#{runId}] Exception: {ex.Message}");
            }
            finally
            {
                IsPoseEstimationRunning = false;
                RunPoseButtonText = "Run Pose Estimation";
                Interlocked.Exchange(ref _poseEstimationRunning, 0);
                LogToError($"[RunPoseEstimation#{runId}] End");
            }
        }

        [RelayCommand]
        private async Task GetError()
        {
            LogToError("[GetError] Fetching error information...");

            if (_lastResult == null)
            {
                ErrorLog = $"[{DateTime.Now:HH:mm:ss}] No estimation has been run yet.\n" +
                    $"Please run 'Run Pose Estimation' first.\n\n" + ErrorLog;
            }
            else
            {
                var errorInfo = new System.Text.StringBuilder();
                errorInfo.AppendLine($"=== Error Information ===");
                errorInfo.AppendLine($"Timestamp: {DateTime.Now:O}");
                errorInfo.AppendLine();
                errorInfo.AppendLine($"Last Result Status: {_lastResult.Status}");
                errorInfo.AppendLine($"Status Message: {_lastResult.StatusMessage}");
                errorInfo.AppendLine();
                errorInfo.AppendLine($"Possible Error Causes:");
                errorInfo.AppendLine($"  - Target not visible in image");
                errorInfo.AppendLine($"  - Poor lighting/contrast");
                errorInfo.AppendLine($"  - Target too far or too close");
                errorInfo.AppendLine($"  - Incorrect focal length/pixel size");
                errorInfo.AppendLine($"  - HMF data doesn't match actual target");
                errorInfo.AppendLine($"  - Pattern occlusion or blur");
                errorInfo.AppendLine($"  - Inaccurate camera intrinsics");
                errorInfo.AppendLine();
                errorInfo.AppendLine($"Configuration:");
                errorInfo.AppendLine($"  - Image: {ImageWidth}x{ImageHeight}");
                errorInfo.AppendLine($"  - Focal Length: {FocalLength} mm");
                errorInfo.AppendLine($"  - Pixel Size: {PixelSize} mm");
                errorInfo.AppendLine($"  - Components: {ComponentsX}x{ComponentsY}");
                errorInfo.AppendLine($"  - Pitch: {PitchX}x{PitchY} mm");
                errorInfo.AppendLine();

                if (_lastResult.Status != libs.PoseStatus.Success)
                {
                    errorInfo.AppendLine($"Recommendation:");
                    if (_lastResult.Status == libs.PoseStatus.CoarseFail)
                    {
                        errorInfo.AppendLine($"  Check image quality and target visibility.");
                        errorInfo.AppendLine($"  Ensure proper lighting and focus.");
                    }
                    else if (_lastResult.Status == libs.PoseStatus.DecodeFail)
                    {
                        errorInfo.AppendLine($"  Verify HMF pattern configuration matches target.");
                        errorInfo.AppendLine($"  Check camera calibration (intrinsics).");
                    }
                }
                else
                {
                    errorInfo.AppendLine($"No errors - estimation was successful!");
                }

                ErrorLog = errorInfo.ToString() + "\n\n" + ErrorLog;
                LogToError("[GetError] Error information generated");
            }

        }

        [RelayCommand]
        private void ResetDefaults()
        {
            LogToError("[ResetDefaults] Resetting to default values");

            // Pattern Analysis defaults
            SelectedPatternType = 0;
            PitchX = 0.1;
            PitchY = 0.1;
            ComponentsX = 11.0;
            ComponentsY = 11.0;

            // Camera Intrinsics defaults
            FocalLength = DefaultFocalLengthMm;
            PixelSize = DefaultPixelSizeMm;
            Fx = 6363.64;
            Fy = 6363.64;
            Cx = 0.0;
            Cy = 0.0;

            // Algorithm defaults
            WindowSize = 12.0;
            CodePitchBlocks = 6.0;

            // Test image defaults
            ImageWidth = DefaultImageWidth;
            ImageHeight = DefaultImageHeight;

            ResultStatusMessage = "Reset to defaults";
            LogToError($"[ResetDefaults] Values reset: Fx={Fx:F3}, Fy={Fy:F3}, Cx={Cx:F1}, Cy={Cy:F1}");
        }

        [RelayCommand]
        private void ClearResults()
        {
            LogToError("[ClearResults] Clearing results");

            HasResults = false;
            _lastResult = null;
            ResultStatusMessage = "Results cleared";
            ResultStatusColor = "#707070";
            Rx = 0;
            Ry = 0;
            Rz = 0;
            Tx = 0;
            Ty = 0;
            Tz = 0;
            RawPoseData = "No data available";

            LogToError("[ClearResults] Results cleared");
        }

        [RelayCommand]
        private void LoadDefaultPreset()
        {
            LogToError("[LoadDefaultPreset] Loading default calibration preset");

            ResetDefaults();

            ResultStatusMessage = "Default preset loaded";
            LogToError("[LoadDefaultPreset] Default preset loaded");
        }

        [RelayCommand]
        private void LoadHighPrecisionPreset()
        {
            LogToError("[LoadHighPrecisionPreset] Loading high precision preset");

            // High precision settings
            PitchX = 1.0;
            PitchY = 1.0;
            ComponentsX = 15.0;
            ComponentsY = 15.0;
            FocalLength = 25.0;
            PixelSize = 0.0035; // 3.5um - smaller pixels
            Fx = FocalLength / PixelSize;
            Fy = FocalLength / PixelSize;
            Cx = 1280.0;
            Cy = 1024.0;
            WindowSize = 7.0;
            CodePitchBlocks = 6.0;
            ImageWidth = 2560;
            ImageHeight = 2048;

            ResultStatusMessage = "High precision preset loaded";
            LogToError($"[LoadHighPrecisionPreset] High precision preset loaded: Fx={Fx:F3}, Fy={Fy:F3}");
        }

        [RelayCommand]
        private async Task SaveConfiguration()
        {
            LogToError("[SaveConfiguration] Saving configuration...");

            try
            {
                // Create configuration summary
                var config = new System.Text.StringBuilder();
                config.AppendLine("=== Pose Estimation Configuration ===");
                config.AppendLine($"Saved: {DateTime.Now:O}");
                config.AppendLine();
                config.AppendLine("Pattern Analysis:");
                config.AppendLine($"  Pattern Type: {SelectedPatternType}");
                config.AppendLine($"  Pitch: {PitchX} x {PitchY} mm");
                config.AppendLine($"  Components: {ComponentsX} x {ComponentsY}");
                config.AppendLine();
                config.AppendLine("Camera Intrinsics:");
                config.AppendLine($"  Focal Length: {FocalLength} mm");
                config.AppendLine($"  Pixel Size: {PixelSize} mm");
                config.AppendLine($"  Fx, Fy: {Fx}, {Fy}");
                config.AppendLine($"  Cx, Cy: {Cx}, {Cy}");
                config.AppendLine();
                config.AppendLine("Algorithm Parameters:");
                config.AppendLine($"  Window Size: {WindowSize}");
                config.AppendLine($"  Code Pitch Blocks: {CodePitchBlocks}");
                config.AppendLine();
                config.AppendLine("Test Image:");
                config.AppendLine($"  Resolution: {ImageWidth} x {ImageHeight}");

                string configPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"pose_config_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                await System.IO.File.WriteAllTextAsync(configPath, config.ToString());

                ErrorLog = $"[SaveConfiguration] Configuration saved to:\n{configPath}\n\n" + ErrorLog;
                LogToError($"[SaveConfiguration] Saved to {configPath}");
            }
            catch (Exception ex)
            {
                ErrorLog = $"[SaveConfiguration] Error saving configuration: {ex.Message}\n\n" + ErrorLog;
                LogToError($"[SaveConfiguration] Error: {ex.Message}");
            }

        }

        #endregion

        #region Helper Methods

        private void UpdateResults(PoseEstimateResult result)
        {
            HasResults = true;

            if (result.Status == libs.PoseStatus.Success)
            {
                ResultStatusMessage = $"Success: {result.StatusMessage}";
                ResultStatusColor = "#4caf50";
            }
            else if (result.Status == libs.PoseStatus.CoarseFail)
            {
                ResultStatusMessage = $"Coarse Fail: {result.StatusMessage}";
                ResultStatusColor = "#ff9800";
            }
            else if (result.Status == libs.PoseStatus.DecodeFail)
            {
                ResultStatusMessage = $"Decode Fail: {result.StatusMessage}";
                ResultStatusColor = "#ff9800";
            }
            else
            {
                ResultStatusMessage = $"Unknown: {result.StatusMessage}";
                ResultStatusColor = "#f44336";
            }

            // Update rotation values (convert to degrees for display)
            Rx = result.Rx * 180 / Math.PI;
            Ry = result.Ry * 180 / Math.PI;
            Rz = result.Rz * 180 / Math.PI;

            // Update translation values (already in mm)
            Tx = result.Tx;
            Ty = result.Ty;
            Tz = result.Tz;

            // Update raw data display
            RawPoseData = $"[{string.Join(", ", result.PoseData.Select(d => d.ToString("F4")))}]";
        }

        private (double[] imageData, int rows, int cols, long frameSeq) GetLatestCameraImageData()
        {
            var cameraService = App.CameraService;
            if (cameraService == null || !cameraService.IsConnected)
            {
                throw new InvalidOperationException("Camera is not connected.");
            }

            var (buffer, width, height, seq) = cameraService.GetLatestFrameSnapshot();
            if (buffer == null || width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("No camera frame available yet.");
            }

            if (buffer.Length < width * height)
            {
                throw new InvalidOperationException(
                    $"Camera frame buffer is too small. Got {buffer.Length}, expected at least {width * height}.");
            }

            var imageData = new double[width * height];
            for (int i = 0; i < imageData.Length; i++)
            {
                imageData[i] = buffer[i];
            }

            ImageWidth = width;
            ImageHeight = height;

            LogToError($"[CameraFrame] Using live frame seq={seq}, size={width}x{height}");
            return (imageData, height, width, seq);
        }

        private string SaveFrameForPoseEstimation(double[] imageData, int rows, int cols, long frameSeq)
        {
            if (imageData.Length != rows * cols)
            {
                throw new InvalidOperationException(
                    $"Image data size mismatch. Expected {rows * cols}, got {imageData.Length}.");
            }

            var sessionName = singalUI.Services.SharedConfigParametersStore.Instance.SessionName;
            if (string.IsNullOrWhiteSpace(sessionName))
            {
                sessionName = "session";
            }

            var archiveDir = Path.Combine(AppContext.BaseDirectory, "Archive", sessionName);
            if (!Directory.Exists(archiveDir))
            {
                Directory.CreateDirectory(archiveDir);
            }

            var imageBytes = new byte[imageData.Length];
            for (var i = 0; i < imageData.Length; i++)
            {
                imageBytes[i] = (byte)Math.Clamp((int)Math.Round(imageData[i]), 0, 255);
            }

            var imagePath = Path.Combine(
                archiveDir,
                $"{sessionName}_pose_input_seq{frameSeq}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            using (var imageMat = new Mat(rows, cols, MatType.CV_8UC1, imageBytes))
            {
                Cv2.ImWrite(imagePath, imageMat);
            }

            LogToError($"[PoseImage] Saved live camera frame seq={frameSeq} to {imagePath}");
            return imagePath;
        }

        private void PrintPoseResultToDebug(PoseEstimateResult result, long elapsedMs, string imagePath)
        {
            var message =
                $"[PoseEstimation] status={result.Status} ({result.StatusMessage}), " +
                $"elapsedMs={elapsedMs}, image={imagePath}, " +
                $"Rx={result.Rx}, Ry={result.Ry}, Rz={result.Rz}, " +
                $"Tx={result.Tx}, Ty={result.Ty}, Tz={result.Tz}";
            Debug.WriteLine(message);
            Console.WriteLine(message);
            LogToError(message);
        }

        private void LogToError(string message)
        {
            try
            {
                string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                ErrorLog = logMessage + "\n" + ErrorLog;

                // Also write to file
                System.IO.File.AppendAllText(_errorLogFile, logMessage + "\n");
            }
            catch
            {
                // Ignore logging errors
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
        }

        #endregion
    }
}
