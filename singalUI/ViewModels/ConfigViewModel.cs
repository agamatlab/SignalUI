using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using singalUI.libs;
using singalUI.Models;
using singalUI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using OpenCvSharp;

namespace singalUI.ViewModels
{
    public partial class ConfigViewModel : ViewModelBase
    {
        // Static instance for cross-ViewModel communication
        public static ConfigViewModel? Instance { get; private set; }

        private const double DefaultFocalLengthMm = 28.0;
        private const double DefaultPixelSizeMm = 4.4e-3;
        private const int DefaultImageWidth = 1600;
        private const int DefaultImageHeight = 1200;

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

        /// <summary>Latest absolute pose for dashboard (deg / mm), before zero or raw toggle.</summary>
        private double _rawRxDeg, _rawRyDeg, _rawRzDeg, _rawTxMm, _rawTyMm, _rawTzMm;

        /// <summary>Snapshot when user selects Set zero (relative) mode.</summary>
        private double _zeroRxDeg, _zeroRyDeg, _zeroRzDeg, _zeroTxMm, _zeroTyMm, _zeroTzMm;

        #region Pattern Analysis Properties

        /// <summary>Archive stem is <c>round(WindowSize)+round(WindowSize)</c> (e.g. 10+10); bridge copies to <c>hmf_temp_*</c> then <c>12+12_*</c>. Components X/Y do not select matrix files.</summary>
        public const double HmfBundledComponents = 206.0;

        [ObservableProperty]
        private int _selectedPatternType = 0; // 0 = CheckerBoard, 1 = HMF Coded Target

        /// <summary>Camera tab checkerboard preset: 0 = 40×40 mm WS10, 1 = 100×100 mm WS12 (persisted in Archive txt).</summary>
        [ObservableProperty]
        private int _cameraPatternPresetIndex;

        [ObservableProperty]
        private double _pitchX = 0.1;

        [ObservableProperty]
        private double _pitchY = 0.1;

        [ObservableProperty]
        private double _componentsX = 11.0;

        [ObservableProperty]
        private double _componentsY = 11.0;

        partial void OnSelectedPatternTypeChanged(int value)
        {
            if (value == 1)
            {
                ComponentsX = HmfBundledComponents;
                ComponentsY = HmfBundledComponents;
                LogToError($"[Pattern] HMF: set Components X/Y to {HmfBundledComponents} (10+10 code bundle).");
            }
        }

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
        private double _windowSize = 10.0;

        [ObservableProperty]
        private double _codePitchBlocks = 6.0;

        #endregion

        #region HMF File Paths

        [ObservableProperty]
        private string _hmfGPath = "";

        [ObservableProperty]
        private string _hmfXPath = "";

        [ObservableProperty]
        private string _hmfYPath = "";

        [ObservableProperty]
        private bool _useCustomHmfPaths = false;

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

        /// <summary>True = show absolute pose; false = show Δ vs snapshot taken when switching to Set zero.</summary>
        [ObservableProperty]
        private bool _displayPoseAsRaw = true;

        partial void OnDisplayPoseAsRawChanged(bool value)
        {
            if (!value)
                CapturePoseZeroBaseline();
            RefreshDisplayedPoseValues();
            OnPropertyChanged(nameof(PoseDeltaHint));
        }

        /// <summary>Explains relative mode under the toggle.</summary>
        public string PoseDeltaHint =>
            DisplayPoseAsRaw
                ? ""
                : "Values are Δ (raw − snapshot at the moment Set zero was selected). Raw string below stays absolute.";

        [ObservableProperty]
        private string _rawPoseData = "No data available";

        [ObservableProperty]
        private string _lastEstimationTime = "";

        [ObservableProperty]
        private bool _isPoseEstimationRunning;

        partial void OnIsPoseEstimationRunningChanged(bool value) => RefreshRunButtonText();

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
            DllDefaultParametersArchive.LoadOrInitialize(this);
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

        /// <summary>Applies the same numeric defaults as <see cref="ResetDefaults"/> (used by Archive seeding).</summary>
        public void ApplyDefaultDllParameters()
        {
            CameraPatternPresetIndex = 0;
            SelectedPatternType = 0;
            PitchX = 0.1;
            PitchY = 0.1;
            ComponentsX = 11.0;
            ComponentsY = 11.0;
            FocalLength = DefaultFocalLengthMm;
            PixelSize = DefaultPixelSizeMm;
            Fx = 6363.64;
            Fy = 6363.64;
            Cx = 0.0;
            Cy = 0.0;
            WindowSize = 10.0;
            CodePitchBlocks = 6.0;
            ImageWidth = DefaultImageWidth;
            ImageHeight = DefaultImageHeight;
        }

        private void RefreshRunButtonText()
        {
            RunPoseButtonText = IsPoseEstimationRunning ? "Running Pose Estimation..." : "Run Pose Estimation";
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

                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? logEntry : $"{ErrorLog}\n{logEntry}";

                // Keep only last MaxErrorLogLines lines
                var lines = ErrorLog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > MaxErrorLogLines)
                {
                    ErrorLog = string.Join("\n", lines.Skip(lines.Length - MaxErrorLogLines));
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
                ResultStatusMessage = "Pose estimation running. This may take around 30-60 seconds.";
                ResultStatusColor = "#ff9800";

                var stopwatch = Stopwatch.StartNew();
                var (result, imagePath) = await EstimatePoseFromLiveFrameAsync(runId);
                stopwatch.Stop();

                _lastResult = result;

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
                Interlocked.Exchange(ref _poseEstimationRunning, 0);
                LogToError($"[RunPoseEstimation#{runId}] End");
            }
        }

        /// <summary>Runs PoseEst bridge on an existing grayscale PNG (does not touch live camera or pose mutex).</summary>
        private async Task<(PoseEstimateResult result, string imagePath)> EstimatePoseFromImagePathAsync(int runId, string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new FileNotFoundException("Pose image not found.", imagePath);

            var intrinsics = new CameraIntrinsics
            {
                Fx = Fx,
                Fy = Fy,
                Cx = Cx,
                Cy = Cy
            };

            LogToError($"[PoseFrame#{runId}] File={imagePath}");

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
                LogToError($"[PoseFrame#{runId}] TIMEOUT after {PoseEstimateTimeoutMs}ms");
                throw new InvalidOperationException($"Pose estimation timed out after {PoseEstimateTimeoutMs} ms.");
            }

            var result = await poseTask;
            return (result, imagePath);
        }

        /// <summary>Runs PoseEst bridge on current live frame; throws on timeout or pipeline errors.</summary>
        private async Task<(PoseEstimateResult result, string imagePath)> EstimatePoseFromLiveFrameAsync(int runId)
        {
            var (imageData, imageRows, imageCols, frameSeq) = GetLatestCameraImageData();
            var imagePath = SaveFrameForPoseEstimation(imageData, imageRows, imageCols, frameSeq);
            LogToError($"[PoseFrame#{runId}] Image={imageCols}x{imageRows}, seq={frameSeq}, path={imagePath}");
            return await EstimatePoseFromImagePathAsync(runId, imagePath);
        }

        [RelayCommand]
        private async Task GetError()
        {
            LogToError("[GetError] Fetching error information...");

            if (_lastResult == null)
            {
                var noRunMessage = $"[{DateTime.Now:HH:mm:ss}] No estimation has been run yet.\n" +
                    "Please run 'Run Pose Estimation' first.";
                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? noRunMessage : $"{ErrorLog}\n\n{noRunMessage}";
            }
            else
            {
                var errorInfo = new System.Text.StringBuilder();
                errorInfo.AppendLine($"=== Error Information ===");
                errorInfo.AppendLine($"Timestamp: {DateTime.Now:O}");
                errorInfo.AppendLine();
                errorInfo.AppendLine($"Last Result Status: {_lastResult!.Status}");
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

                if (_lastResult!.Status != libs.PoseStatus.Success)
                {
                    errorInfo.AppendLine($"Recommendation:");
                    if (_lastResult.Status == libs.PoseStatus.CoarseFail)
                    {
                        errorInfo.AppendLine($"  Check image quality and target visibility.");
                        errorInfo.AppendLine($"  Ensure proper lighting and focus.");
                    }
                    else if (_lastResult.Status == libs.PoseStatus.DecodeFail)
                    {
                        errorInfo.AppendLine($"  HMF: Archive stem = round(WindowSize)+round(WindowSize) (e.g. 10+10, 12+12); bridge uses hmf_temp_*.");
                        errorInfo.AppendLine($"  For that bundle, Components X/Y should be {HmfBundledComponents} (auto-set when you pick Dot Grid).");
                        errorInfo.AppendLine($"  Verify pitch/window/code blocks and intrinsics match the physical target.");
                    }
                }
                else
                {
                    errorInfo.AppendLine($"No errors - estimation was successful!");
                }

                var details = errorInfo.ToString().TrimEnd();
                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? details : $"{ErrorLog}\n\n{details}";
                LogToError("[GetError] Error information generated");
            }

        }

        [RelayCommand]
        private void ResetDefaults()
        {
            LogToError("[ResetDefaults] Resetting to default values");
            ApplyDefaultDllParameters();
            ResultStatusMessage = "Reset to defaults";
            LogToError($"[ResetDefaults] Values reset: Fx={Fx:F3}, Fy={Fy:F3}, Cx={Cx:F1}, Cy={Cy:F1}");
            _ = DllDefaultParametersArchive.TrySave(this);
        }

        [RelayCommand]
        private void ClearResults()
        {
            LogToError("[ClearResults] Clearing results");

            HasResults = false;
            _lastResult = null;
            ResultStatusMessage = "Results cleared";
            ResultStatusColor = "#707070";
            _rawRxDeg = _rawRyDeg = _rawRzDeg = _rawTxMm = _rawTyMm = _rawTzMm = 0;
            _zeroRxDeg = _zeroRyDeg = _zeroRzDeg = _zeroTxMm = _zeroTyMm = _zeroTzMm = 0;
            RefreshDisplayedPoseValues();
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

                var savedMsg = $"[SaveConfiguration] Configuration saved to:\n{configPath}";
                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? savedMsg : $"{ErrorLog}\n\n{savedMsg}";
                LogToError($"[SaveConfiguration] Saved to {configPath}");
            }
            catch (Exception ex)
            {
                var saveErr = $"[SaveConfiguration] Error saving configuration: {ex.Message}";
                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? saveErr : $"{ErrorLog}\n\n{saveErr}";
                LogToError($"[SaveConfiguration] Error: {ex.Message}");
            }

        }

        [RelayCommand]
        private async Task BrowseHmfGFile()
        {
            try
            {
                var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select HMF G File (12+12_G.txt)",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("Text Files")
                        {
                            Patterns = new[] { "*.txt" }
                        },
                        new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                };

                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    var result = await mainWindow.StorageProvider.OpenFilePickerAsync(dialog);
                    if (result.Count > 0)
                    {
                        HmfGPath = result[0].Path.LocalPath;
                        LogToError($"[BrowseHmfGFile] Selected: {HmfGPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToError($"[BrowseHmfGFile] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task BrowseHmfXFile()
        {
            try
            {
                var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select HMF X File (12+12_X.txt)",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("Text Files")
                        {
                            Patterns = new[] { "*.txt" }
                        },
                        new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                };

                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    var result = await mainWindow.StorageProvider.OpenFilePickerAsync(dialog);
                    if (result.Count > 0)
                    {
                        HmfXPath = result[0].Path.LocalPath;
                        LogToError($"[BrowseHmfXFile] Selected: {HmfXPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToError($"[BrowseHmfXFile] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task BrowseHmfYFile()
        {
            try
            {
                var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select HMF Y File (12+12_Y.txt)",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("Text Files")
                        {
                            Patterns = new[] { "*.txt" }
                        },
                        new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                };

                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow != null)
                {
                    var result = await mainWindow.StorageProvider.OpenFilePickerAsync(dialog);
                    if (result.Count > 0)
                    {
                        HmfYPath = result[0].Path.LocalPath;
                        LogToError($"[BrowseHmfYFile] Selected: {HmfYPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToError($"[BrowseHmfYFile] Error: {ex.Message}");
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

            StoreRawPoseSix(
                result.Rx * 180 / Math.PI,
                result.Ry * 180 / Math.PI,
                result.Rz * 180 / Math.PI,
                result.Tx,
                result.Ty,
                result.Tz);

            RawPoseData = $"[{string.Join(", ", result.PoseData.Select(d => d.ToString("F4")))}]";
        }

        private void CapturePoseZeroBaseline()
        {
            _zeroRxDeg = _rawRxDeg;
            _zeroRyDeg = _rawRyDeg;
            _zeroRzDeg = _rawRzDeg;
            _zeroTxMm = _rawTxMm;
            _zeroTyMm = _rawTyMm;
            _zeroTzMm = _rawTzMm;
        }

        private void StoreRawPoseSix(double rxDeg, double ryDeg, double rzDeg, double txMm, double tyMm, double tzMm)
        {
            _rawRxDeg = rxDeg;
            _rawRyDeg = ryDeg;
            _rawRzDeg = rzDeg;
            _rawTxMm = txMm;
            _rawTyMm = tyMm;
            _rawTzMm = tzMm;
            RefreshDisplayedPoseValues();
        }

        private void RefreshDisplayedPoseValues()
        {
            if (DisplayPoseAsRaw)
            {
                Rx = _rawRxDeg;
                Ry = _rawRyDeg;
                Rz = _rawRzDeg;
                Tx = _rawTxMm;
                Ty = _rawTyMm;
                Tz = _rawTzMm;
            }
            else
            {
                Rx = _rawRxDeg - _zeroRxDeg;
                Ry = _rawRyDeg - _zeroRyDeg;
                Rz = _rawRzDeg - _zeroRzDeg;
                Tx = _rawTxMm - _zeroTxMm;
                Ty = _rawTyMm - _zeroTyMm;
                Tz = _rawTzMm - _zeroTzMm;
            }
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
                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? logMessage : $"{ErrorLog}\n{logMessage}";

                // Also write to file
                System.IO.File.AppendAllText(_errorLogFile, logMessage + "\n");
            }
            catch
            {
                // Ignore logging errors
            }
        }

        #endregion
    }
}
