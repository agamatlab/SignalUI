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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using OpenCvSharp;

namespace singalUI.ViewModels
{
    public partial class ConfigViewModel : ViewModelBase
    {
        private const double DefaultFocalLengthMm = 28.0;
        private const double DefaultPixelSizeMm = 4.4e-3;
        private const int DefaultImageWidth = 640;
        private const int DefaultImageHeight = 480;

        private PoseEstimateResult? _lastResult;
        private RotationCalibrationNativeResult? _lastRotationCalibResult;
        private const int PoseEstimateTimeoutMs = 180000;
        private const int RotationCalibTimeoutMs = 600000;
        private int _poseRunSequence = 0;
        private int _poseEstimationRunning = 0;
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

        [ObservableProperty]
        private bool _isRotationCalibrationMode;

        public bool ShowSixDofConfig => !IsRotationCalibrationMode;

        public bool ShowRotationCalibPanel => IsRotationCalibrationMode;

        partial void OnIsRotationCalibrationModeChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowSixDofConfig));
            OnPropertyChanged(nameof(ShowRotationCalibPanel));
            RefreshRunButtonText();
        }

        [ObservableProperty]
        private ObservableCollection<RotationCalibrationSample> _rotationSamples = new();

        [ObservableProperty]
        private double _manualThetaDegrees;

        [ObservableProperty]
        private string _rotationDatasetHint =
            "Add samples: uses live camera pose (PoseEst) + stage Rz (deg→rad) or manual θ. Need ≥12 points; ~360 recommended. CSV columns: theta_rad,rx,ry,rz,tx,ty,tz";

        #endregion

        #region Status Properties

        [ObservableProperty]
        private string _estimatorStatus = "Not Initialized";

        [ObservableProperty]
        private string _errorLog = "";

        #endregion

        public ConfigViewModel()
        {
            App.CalibrationAppModeChanged += OnGlobalCalibrationModeChanged;
            SyncCalibrationModeFromApp();
            LogToError("[ConfigViewModel] Constructor started");
            InitializeEstimator();
            LogToError("[ConfigViewModel] Constructor completed");
        }

        private void OnGlobalCalibrationModeChanged(object? sender, EventArgs e) =>
            SyncCalibrationModeFromApp();

        private void SyncCalibrationModeFromApp()
        {
            IsRotationCalibrationMode = App.CalibrationAppMode == CalibrationAppMode.RotationStage;
        }

        private void RefreshRunButtonText()
        {
            if (IsRotationCalibrationMode)
                RunPoseButtonText = IsPoseEstimationRunning ? "Running rotation calibration..." : "Run rotation calibration";
            else
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
            if (IsRotationCalibrationMode)
            {
                await RunRotationCalibration();
                return;
            }

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
                _lastRotationCalibResult = null;

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

        private async Task RunRotationCalibration()
        {
            int runId = Interlocked.Increment(ref _poseRunSequence);
            if (Interlocked.CompareExchange(ref _poseEstimationRunning, 1, 0) != 0)
            {
                LogToError($"[RunRotationCalib#{runId}] Skipped: previous run still active");
                return;
            }

            LogToError($"[RunRotationCalib#{runId}] Starting rotation calibration...");
            try
            {
                IsPoseEstimationRunning = true;
                ResultStatusMessage = "Rotation calibration running (NanoMeasCalib)...";
                ResultStatusColor = "#ff9800";

                if (RotationSamples.Count < 12)
                {
                    ResultStatusMessage = $"Need at least 12 samples (have {RotationSamples.Count}).";
                    ResultStatusColor = "#f44336";
                    HasResults = false;
                    return;
                }

                var ordered = RotationSamples.OrderBy(s => s.ThetaRad).ToList();
                int n = ordered.Count;
                var theta = new double[n];
                var meas = new double[6 * n];
                for (int i = 0; i < n; i++)
                {
                    theta[i] = ordered[i].ThetaRad;
                    meas[6 * i + 0] = ordered[i].Rx;
                    meas[6 * i + 1] = ordered[i].Ry;
                    meas[6 * i + 2] = ordered[i].Rz;
                    meas[6 * i + 3] = ordered[i].Tx;
                    meas[6 * i + 4] = ordered[i].Ty;
                    meas[6 * i + 5] = ordered[i].Tz;
                }

                var stopwatch = Stopwatch.StartNew();
                var calibTask = Task.Factory.StartNew(() => NanoMeasCalibNative.RunRotationCalibration(theta, meas, n),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                var completed = await Task.WhenAny(calibTask, Task.Delay(RotationCalibTimeoutMs));
                if (completed != calibTask)
                {
                    stopwatch.Stop();
                    ResultStatusMessage = $"Rotation calibration timed out after {RotationCalibTimeoutMs} ms";
                    ResultStatusColor = "#f44336";
                    HasResults = false;
                    LogToError($"[RunRotationCalib#{runId}] TIMEOUT after {stopwatch.ElapsedMilliseconds}ms");
                    return;
                }

                _lastRotationCalibResult = await calibTask;
                _lastResult = null;
                stopwatch.Stop();

                if (_lastRotationCalibResult.IsSuccess)
                {
                    ApplyDisplayFromTcWEuler(_lastRotationCalibResult.T_CW_Euler, _lastRotationCalibResult.Status);
                    ResultStatusMessage =
                        $"Rotation calib OK — showing T_CW (camera→world, rad/mm internal → deg/mm UI). {NanoMeasCalibNative.StatusMessage(_lastRotationCalibResult.Status)}";
                    ResultStatusColor = "#4caf50";
                    LogToError($"[RunRotationCalib#{runId}] OK in {stopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    HasResults = false;
                    ResultStatusMessage =
                        $"Rotation calib failed: {NanoMeasCalibNative.StatusMessage(_lastRotationCalibResult.Status)} (code {_lastRotationCalibResult.Status})";
                    ResultStatusColor = "#f44336";
                    LogToError($"[RunRotationCalib#{runId}] Failed status={_lastRotationCalibResult.Status}");
                }

                LastEstimationTime = $"Last run: {DateTime.Now:HH:mm:ss}";
            }
            catch (DllNotFoundException ex)
            {
                HasResults = false;
                ResultStatusMessage =
                    "RotCalibBridge.dll / NanoMeasCalib.dll not found. Build native/RotCalibBridge (VS) and place NanoMeasCalib.lib/.dll under PoseEstApi/RotPoseEstApi/lib.";
                ResultStatusColor = "#f44336";
                LogToError($"[RunRotationCalib] {ex.Message}");
            }
            catch (Exception ex)
            {
                HasResults = false;
                ResultStatusMessage = $"Rotation calib error: {ex.Message}";
                ResultStatusColor = "#f44336";
                LogToError($"[RunRotationCalib] {ex}");
            }
            finally
            {
                IsPoseEstimationRunning = false;
                Interlocked.Exchange(ref _poseEstimationRunning, 0);
            }
        }

        /// <summary>Runs PoseEst bridge on current live frame; throws on timeout or pipeline errors.</summary>
        private async Task<(PoseEstimateResult result, string imagePath)> EstimatePoseFromLiveFrameAsync(int runId)
        {
            var intrinsics = new CameraIntrinsics
            {
                Fx = Fx,
                Fy = Fy,
                Cx = Cx,
                Cy = Cy
            };

            var (imageData, imageRows, imageCols, frameSeq) = GetLatestCameraImageData();
            var imagePath = SaveFrameForPoseEstimation(imageData, imageRows, imageCols, frameSeq);

            LogToError($"[PoseFrame#{runId}] Image={imageCols}x{imageRows}, seq={frameSeq}, path={imagePath}");

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

        [RelayCommand]
        private async Task AddRotationSample()
        {
            int runId = Interlocked.Increment(ref _poseRunSequence);
            if (Interlocked.CompareExchange(ref _poseEstimationRunning, 1, 0) != 0)
            {
                LogToError($"[AddRotationSample#{runId}] Skipped: operation in progress");
                return;
            }

            try
            {
                IsPoseEstimationRunning = true;
                ResultStatusMessage = "Capturing pose for rotation sample...";
                ResultStatusColor = "#ff9800";

                double thetaRad = TryGetStageRotationThetaRad();
                if (double.IsNaN(thetaRad))
                    thetaRad = ManualThetaDegrees * Math.PI / 180.0;

                var (result, _) = await EstimatePoseFromLiveFrameAsync(runId);
                if (result.Status != libs.PoseStatus.Success)
                {
                    ResultStatusMessage = $"Sample not added — pose status {result.Status}: {result.StatusMessage}";
                    ResultStatusColor = "#ff9800";
                    LogToError($"[AddRotationSample#{runId}] Pose failed: {result.StatusMessage}");
                    return;
                }

                RotationSamples.Add(new RotationCalibrationSample
                {
                    ThetaRad = thetaRad,
                    Rx = result.Rx,
                    Ry = result.Ry,
                    Rz = result.Rz,
                    Tx = result.Tx,
                    Ty = result.Ty,
                    Tz = result.Tz,
                });

                ResultStatusMessage = $"Added sample #{RotationSamples.Count} at θ={thetaRad * 180 / Math.PI:F3}°";
                ResultStatusColor = "#4caf50";
                LogToError($"[AddRotationSample#{runId}] Added, N={RotationSamples.Count}");
            }
            catch (Exception ex)
            {
                ResultStatusMessage = $"Add sample failed: {ex.Message}";
                ResultStatusColor = "#f44336";
                LogToError($"[AddRotationSample#{runId}] {ex.Message}");
            }
            finally
            {
                IsPoseEstimationRunning = false;
                Interlocked.Exchange(ref _poseEstimationRunning, 0);
            }
        }

        [RelayCommand]
        private void ClearRotationSamples()
        {
            RotationSamples.Clear();
            _lastRotationCalibResult = null;
            LogToError("[RotationSamples] Cleared");
            ResultStatusMessage = "Rotation sample list cleared";
            ResultStatusColor = "#707070";
        }

        [RelayCommand]
        private async Task ImportRotationSamplesCsv()
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime d ||
                    d.MainWindow is not Avalonia.Controls.Window w)
                {
                    ResultStatusMessage = "Cannot open file dialog (no main window).";
                    ResultStatusColor = "#f44336";
                    return;
                }

                var files = await w.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import rotation samples CSV",
                    AllowMultiple = false,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new("CSV") { Patterns = new[] { "*.csv" } },
                        FilePickerFileTypes.All,
                    },
                });

                var path = files?.Count > 0 ? files[0].TryGetLocalPath() : null;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    ResultStatusMessage = "Import cancelled.";
                    ResultStatusColor = "#707070";
                    return;
                }

                var lines = await File.ReadAllLinesAsync(path);
                int added = 0;
                foreach (var line in lines)
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith('#'))
                        continue;
                    var parts = t.Split(new[] { ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 7)
                        continue;

                    if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var thetaRad))
                        continue;
                    var nums = new double[6];
                    var ok = true;
                    for (int k = 0; k < 6; k++)
                    {
                        if (!double.TryParse(parts[1 + k], NumberStyles.Float, CultureInfo.InvariantCulture, out nums[k]))
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (!ok) continue;

                    RotationSamples.Add(new RotationCalibrationSample
                    {
                        ThetaRad = thetaRad,
                        Rx = nums[0],
                        Ry = nums[1],
                        Rz = nums[2],
                        Tx = nums[3],
                        Ty = nums[4],
                        Tz = nums[5],
                    });
                    added++;
                }

                ResultStatusMessage = $"Imported {added} samples from CSV (total {RotationSamples.Count}).";
                ResultStatusColor = "#4caf50";
                LogToError($"[ImportRotationCsv] {added} rows from {path}");
            }
            catch (Exception ex)
            {
                ResultStatusMessage = $"CSV import failed: {ex.Message}";
                ResultStatusColor = "#f44336";
                LogToError($"[ImportRotationCsv] {ex}");
            }
        }

        [RelayCommand]
        private async Task GetError()
        {
            LogToError("[GetError] Fetching error information...");

            if (_lastResult == null && _lastRotationCalibResult == null)
            {
                ErrorLog = $"[{DateTime.Now:HH:mm:ss}] No estimation has been run yet.\n" +
                    $"Please run 'Run Pose Estimation' or rotation calibration first.\n\n" + ErrorLog;
            }
            else if (_lastRotationCalibResult != null)
            {
                var errorInfo = new System.Text.StringBuilder();
                errorInfo.AppendLine("=== Last rotation calibration ===");
                errorInfo.AppendLine($"Timestamp: {DateTime.Now:O}");
                errorInfo.AppendLine($"Native status: {_lastRotationCalibResult.Status} — {NanoMeasCalibNative.StatusMessage(_lastRotationCalibResult.Status)}");
                errorInfo.AppendLine($"Return code: {_lastRotationCalibResult.ReturnCode}");
                ErrorLog = errorInfo.ToString() + "\n\n" + ErrorLog;
                LogToError("[GetError] Rotation calibration info appended");
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
            _lastRotationCalibResult = null;
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

        /// <summary>Maps T_CW_euler [Rx,Ry,Rz rad; tx,ty,tz mm] into the same result fields as pose estimation.</summary>
        private void ApplyDisplayFromTcWEuler(double[] tCwEuler, int nativeStatus)
        {
            if (tCwEuler == null || tCwEuler.Length < 6)
            {
                HasResults = false;
                return;
            }

            HasResults = nativeStatus == 0;
            StoreRawPoseSix(
                tCwEuler[0] * 180 / Math.PI,
                tCwEuler[1] * 180 / Math.PI,
                tCwEuler[2] * 180 / Math.PI,
                tCwEuler[3],
                tCwEuler[4],
                tCwEuler[5]);
            RawPoseData =
                $"T_CW euler [rad,mm]: [{string.Join(", ", tCwEuler.Select(v => v.ToString("F6")))}] | native status {nativeStatus}";
        }

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

            var archiveDir = Path.Combine(AppContext.BaseDirectory, "Archive");
            if (!Directory.Exists(archiveDir))
            {
                throw new DirectoryNotFoundException($"Archive directory not found: {archiveDir}");
            }

            var imageBytes = new byte[imageData.Length];
            for (var i = 0; i < imageData.Length; i++)
            {
                imageBytes[i] = (byte)Math.Clamp((int)Math.Round(imageData[i]), 0, 255);
            }

            var imagePath = Path.Combine(
                AppContext.BaseDirectory,
                $"last_pose_input_seq{frameSeq}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            using (var imageMat = new Mat(rows, cols, MatType.CV_8UC1, imageBytes))
            {
                Cv2.ImWrite(imagePath, imageMat);
            }

            LogToError($"[PoseImage] Saved live camera frame seq={frameSeq} to {imagePath}");
            return imagePath;
        }

        /// <summary>Rz axis position from first connected stage that has Rz (PI reports degrees).</summary>
        private static double TryGetStageRotationThetaRad()
        {
            foreach (var w in StageManager.Wrappers)
            {
                if (!w.IsConnected || w.Controller == null)
                    continue;
                int idx = w.GetAxisIndex(AxisType.Rz);
                if (idx < 0)
                    continue;
                double p = w.Controller.GetPosition(idx);
                return p * Math.PI / 180.0;
            }

            return double.NaN;
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
            App.CalibrationAppModeChanged -= OnGlobalCalibrationModeChanged;
        }

        #endregion
    }
}
