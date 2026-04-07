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
        private const double DefaultFocalLengthMm = 28.0;
        private const double DefaultPixelSizeMm = 4.4e-3;
        private const int DefaultImageWidth = 1600;
        private const int DefaultImageHeight = 1200;

        private PoseEstimateResult? _lastResult;
        private const int PoseEstimateTimeoutMs = 180000;
        private const int RotationCalibTimeoutMs = 600000;
        private const int RotationNanoMeasMinFrames = 12;
        private int _poseRunSequence = 0;
        private int _poseEstimationRunning = 0;
        private readonly object _rotationCaptureFileLock = new();
        private IReadOnlyList<NanoMeasFrameTrackResult>? _lastNanoMeasRotationFrames;
        private string? _lastNanoMeasRotationSessionDir;
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
            "Start rotation capture saves frames under .\\storage\\session_* (manifest.csv + frame_###.png). Rz moves to 0° first, then steps. Run rotation calibration runs NanoMeas.dll (ProcessFrame_Track_U8) on the newest session with ≥12 frames; results go to the error log.";

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
                _lastNanoMeasRotationFrames = null;
                _lastNanoMeasRotationSessionDir = null;

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

            LogToError($"[RunRotationCalib#{runId}] Starting NanoMeas.dll on newest rotation session...");
            try
            {
                IsPoseEstimationRunning = true;
                ResultStatusMessage = "Rotation calibration running (NanoMeas.dll)...";
                ResultStatusColor = "#ff9800";
                HasResults = false;
                _lastResult = null;

                string? sessionDir = FindNewestRotationSessionDirectory(RotationNanoMeasMinFrames);
                if (string.IsNullOrEmpty(sessionDir))
                {
                    ResultStatusMessage =
                        $"No session under .\\storage\\session_* with ≥{RotationNanoMeasMinFrames} frames in manifest.csv.";
                    ResultStatusColor = "#f44336";
                    LogToError($"[RunRotationCalib#{runId}] {ResultStatusMessage}");
                    return;
                }

                if (!TryBuildNanoMeasHmfSequences(out var seqX, out var seqY, out string? hmfErr))
                {
                    ResultStatusMessage = hmfErr ?? "Failed to load HMF sequences.";
                    ResultStatusColor = "#f44336";
                    LogToError($"[RunRotationCalib#{runId}] {ResultStatusMessage}");
                    return;
                }

                int windowRounded = (int)Math.Round(WindowSize);
                LogToError(
                    $"[RunRotationCalib#{runId}] HMF Archive\\{windowRounded}+{windowRounded}_{{X,Y}}.txt " +
                    $"(seq len_x={seqX.Length} len_y={seqY.Length}), grid.window_size={windowRounded}");

                if (!TryLoadRotationSessionFramesForNanoMeas(sessionDir, RotationNanoMeasMinFrames, out var frames, out string? loadErr))
                {
                    ResultStatusMessage = loadErr ?? "Failed to load session PNGs.";
                    ResultStatusColor = "#f44336";
                    LogToError($"[RunRotationCalib#{runId}] {ResultStatusMessage}");
                    return;
                }

                var cam = NanoMeasSdkNative.BuildCameraParams(Fx, Fy, Cx, Cy, PixelSize, PixelSize, FocalLength);
                var grid = NanoMeasSdkNative.BuildGridParams(
                    PitchX,
                    PitchY,
                    (int)Math.Round(CodePitchBlocks),
                    (int)Math.Round(WindowSize));

                try
                {
                    NanoMeasSdkNative.EnsureNanoMeasNativeReady();
                    LogToError($"[RunRotationCalib#{runId}] NanoMeas.dll loaded from: {NanoMeasSdkNative.GetLoadedNanoMeasDllPath()}");
                }
                catch (Exception loadEx)
                {
                    ResultStatusMessage = loadEx.Message;
                    ResultStatusColor = "#f44336";
                    LogToError($"[RunRotationCalib#{runId}] {loadEx}");
                    return;
                }

                var stopwatch = Stopwatch.StartNew();
                string? ver = null;
                var calibTask = Task.Factory.StartNew(() =>
                {
                    ver = NanoMeasSdkNative.MarshalUtf8String(NanoMeasSdkNative.NanoMeas_GetVersion());
                    return NanoMeasSdkNative.RunProcessFrameTrackU8Sequence(
                        frames,
                        seqX,
                        seqY,
                        cam,
                        grid,
                        winOrder: 3,
                        refreshPeriod: 10,
                        residualThreshold: 3.0);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                var completed = await Task.WhenAny(calibTask, Task.Delay(RotationCalibTimeoutMs));
                if (completed != calibTask)
                {
                    stopwatch.Stop();
                    ResultStatusMessage = $"Rotation calibration timed out after {RotationCalibTimeoutMs} ms";
                    ResultStatusColor = "#f44336";
                    LogToError($"[RunRotationCalib#{runId}] TIMEOUT after {stopwatch.ElapsedMilliseconds}ms");
                    return;
                }

                var results = await calibTask;
                stopwatch.Stop();

                _lastNanoMeasRotationFrames = results;
                _lastNanoMeasRotationSessionDir = sessionDir;

                await Dispatcher.UIThread.InvokeAsync(() =>
                    singalUI.App.SharedAnalysisViewModel.ApplyRotationCalibrationPoses3D(results));

                LogToError($"[RunRotationCalib#{runId}] Session={sessionDir}");
                LogToError($"[RunRotationCalib#{runId}] {ver ?? "NanoMeas_GetVersion: (null)"}");
                foreach (var r in results)
                {
                    string poseStr = FormatPose6(r.Pose);
                    string poseAbsStr = FormatPose6(r.PoseAbs);
                    LogToError(
                        $"[RunRotationCalib#{runId}] {r.FileName} status={r.Status} decode={r.DecodeSuccess} " +
                        $"pose=[{poseStr}] pose_abs=[{poseAbsStr}] refine_ms={r.RefineMs:G5} err={(r.LastError ?? "").Trim()}");
                }

                int okCount = results.Count(r => r.Status == NanoMeasSdkNative.NanoMeasOk);
                ResultStatusMessage =
                    okCount == results.Count
                        ? $"NanoMeas OK — {results.Count} frames in {stopwatch.ElapsedMilliseconds} ms (see error log)."
                        : $"NanoMeas finished — {okCount}/{results.Count} frames OK (see error log).";
                ResultStatusColor = okCount == results.Count ? "#4caf50" : "#ff9800";
                LastEstimationTime = $"Last run: {DateTime.Now:HH:mm:ss}";
                LogToError($"[RunRotationCalib#{runId}] Done in {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (DllNotFoundException ex)
            {
                HasResults = false;
                ResultStatusMessage =
                    "NanoMeas.dll not found (or fftw3/MKL deps). Copy the SDK bundle next to the executable; see PoseEstApi/NanoMeas_SDK_secret/README.md.";
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

        private static string FormatPose6(IReadOnlyList<double> pose)
        {
            if (pose == null || pose.Count < 6)
                return "";
            return string.Join(
                ", ",
                pose.Take(6).Select(v => v.ToString("G9", CultureInfo.InvariantCulture)));
        }

        /// <summary>Chooses the session folder with the most recently updated manifest that lists at least <paramref name="minFrames"/> existing PNGs.</summary>
        private static string? FindNewestRotationSessionDirectory(int minFrames)
        {
            var root = Path.Combine(AppContext.BaseDirectory, "storage");
            if (!Directory.Exists(root))
                return null;

            string? best = null;
            var bestUtc = DateTime.MinValue;

            foreach (var dir in Directory.EnumerateDirectories(root, "session_*"))
            {
                var manifest = Path.Combine(dir, "manifest.csv");
                if (!File.Exists(manifest))
                    continue;
                if (!TryCountManifestRowsWithExistingFiles(dir, minFrames, out int okRows) || okRows < minFrames)
                    continue;

                var t = File.GetLastWriteTimeUtc(manifest);
                if (t >= bestUtc)
                {
                    bestUtc = t;
                    best = dir;
                }
            }

            return best;
        }

        private static bool TryCountManifestRowsWithExistingFiles(string sessionDir, int minNeeded, out int count)
        {
            count = 0;
            if (!TryReadManifestRows(sessionDir, out var rows, out _))
                return false;

            int ok = rows.Count(t =>
            {
                var p = Path.Combine(sessionDir, t.file);
                return File.Exists(p);
            });
            count = ok;
            return ok >= minNeeded;
        }

        private static bool TryReadManifestRows(string sessionDir, out List<(int index, string file)> rows, out string? error)
        {
            rows = new List<(int, string)>();
            error = null;
            var path = Path.Combine(sessionDir, "manifest.csv");
            if (!File.Exists(path))
            {
                error = "manifest.csv missing.";
                return false;
            }

            try
            {
                var lines = File.ReadAllLines(path);
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.Length == 0)
                        continue;
                    if (line.StartsWith("index", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parts = line.Split(',');
                    if (parts.Length < 2)
                        continue;
                    if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                        continue;
                    string file = parts[^1].Trim();
                    if (file.Length == 0)
                        continue;
                    rows.Add((idx, file));
                }

                rows.Sort((a, b) => a.index.CompareTo(b.index));
                return rows.Count > 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryLoadRotationSessionFramesForNanoMeas(
            string sessionDir,
            int frameCount,
            out List<NanoMeasFrameInput> frames,
            out string? error)
        {
            frames = new List<NanoMeasFrameInput>();
            error = null;

            if (!TryReadManifestRows(sessionDir, out var rows, out error))
                return false;

            var ordered = rows.Where(t => File.Exists(Path.Combine(sessionDir, t.file))).OrderBy(t => t.index).ToList();
            if (ordered.Count < frameCount)
            {
                error = $"Manifest lists only {ordered.Count} existing PNG(s); need {frameCount}.";
                return false;
            }

            for (int i = 0; i < frameCount; i++)
            {
                var (idx, fn) = ordered[i];
                var full = Path.Combine(sessionDir, fn);
                try
                {
                    using var mat = Cv2.ImRead(full, ImreadModes.Grayscale);
                    if (mat.Empty() || mat.Type() != MatType.CV_8UC1)
                    {
                        error = $"{fn}: not a grayscale 8-bit image.";
                        return false;
                    }

                    int h = mat.Height;
                    int w = mat.Width;
                    if (!mat.GetArray(out byte[]? pixels) || pixels == null || pixels.Length != h * w)
                    {
                        error = $"{fn}: could not read pixel buffer.";
                        return false;
                    }

                    frames.Add(new NanoMeasFrameInput(idx, fn, pixels, h, w));
                }
                catch (Exception ex)
                {
                    error = $"{fn}: {ex.Message}";
                    return false;
                }
            }

            int h0 = frames[0].Height;
            int w0 = frames[0].Width;
            if (frames.Any(f => f.Height != h0 || f.Width != w0))
            {
                error = "Frames have inconsistent image size.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Loads comma-separated 0/1 HMF lines from <c>Archive\{n}+{n}_Y.txt</c> / <c>_X.txt</c> where
        /// <c>n = round(WindowSize)</c> (e.g. 12 → 12+12_*.txt, 10 → 10+10_*.txt). Per nanomeas_api.h:
        /// seq_x = physical-Y file; seq_y = physical-X file.
        /// </summary>
        private bool TryBuildNanoMeasHmfSequences(out int[] seqX, out int[] seqY, out string? error)
        {
            seqX = Array.Empty<int>();
            seqY = Array.Empty<int>();
            error = null;
            int n = (int)Math.Round(WindowSize);
            if (n < 1)
            {
                error = "Window size must round to a positive integer for HMF file selection.";
                return false;
            }

            string stem = $"{n}+{n}";
            var baseDir = AppContext.BaseDirectory;
            var pathY = Path.Combine(baseDir, "Archive", $"{stem}_Y.txt");
            var pathX = Path.Combine(baseDir, "Archive", $"{stem}_X.txt");

            if (!File.Exists(pathY) || !File.Exists(pathX))
            {
                error =
                    $"HMF text not found for window size {WindowSize} (stem {stem}). " +
                    $"Expected next to the executable: Archive\\{stem}_Y.txt and Archive\\{stem}_X.txt.";
                return false;
            }

            try
            {
                seqX = ParseCommaSeparatedBinaryInts(pathY);
                seqY = ParseCommaSeparatedBinaryInts(pathX);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static int[] ParseCommaSeparatedBinaryInts(string path)
        {
            var text = File.ReadAllText(path).Replace("\r", "").Replace("\n", "");
            var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var arr = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v is not (0 or 1))
                    throw new InvalidDataException($"{path}: expected 0/1 comma list at token {i}: '{parts[i]}'");
                arr[i] = v;
            }

            return arr;
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

        /// <summary>Creates <c>storage/session_*</c> under the app base directory (next to the executable when published).</summary>
        public string CreateRotationCaptureSessionDirectory()
        {
            var root = Path.Combine(AppContext.BaseDirectory, "storage");
            Directory.CreateDirectory(root);
            var session = Path.Combine(root, $"session_{DateTime.Now:yyyyMMdd_HHmmss_fff}");
            Directory.CreateDirectory(session);
            File.WriteAllText(
                Path.Combine(session, "README.txt"),
                "Raw frames from Setup → Start rotation capture.\r\n" +
                "Location: .\\storage\\session_* (app base directory).\r\n" +
                "- manifest.csv: index, theta (stage Rz or manual), filename\r\n" +
                "- frame_NNN.png: grayscale captures\r\n" +
                "Run rotation calibration (Configure tab) runs NanoMeas.dll on the newest session with ≥12 frames.\r\n");
            LogToError($"[RotationCapture] Session folder: {session}");
            return session;
        }

        /// <summary>Saves current live frame into the session folder; never calls PoseEst.</summary>
        public (bool ok, string savedPath, string message) TrySaveRotationCaptureFrame(string sessionDirectory, int oneBasedIndex)
        {
            try
            {
                var (imageData, rows, cols, frameSeq) = GetLatestCameraImageData();
                if (imageData.Length != rows * cols)
                    return (false, "", $"Image size mismatch {imageData.Length} vs {rows * cols}");

                var fileName = $"frame_{oneBasedIndex:000}.png";
                var savedPath = Path.Combine(sessionDirectory, fileName);

                var imageBytes = new byte[imageData.Length];
                for (var i = 0; i < imageData.Length; i++)
                    imageBytes[i] = (byte)Math.Clamp((int)Math.Round(imageData[i]), 0, 255);

                using (var imageMat = new Mat(rows, cols, MatType.CV_8UC1, imageBytes))
                    Cv2.ImWrite(savedPath, imageMat);

                double thetaRad = TryGetStageRotationThetaRad();
                lock (_rotationCaptureFileLock)
                {
                    var manifestPath = Path.Combine(sessionDirectory, "manifest.csv");
                    if (!File.Exists(manifestPath))
                        File.WriteAllText(manifestPath, "index,theta_deg,theta_rad,file\n");

                    double thetaDeg = double.IsNaN(thetaRad) ? ManualThetaDegrees : thetaRad * 180.0 / Math.PI;
                    double thetaRadOut = double.IsNaN(thetaRad) ? ManualThetaDegrees * Math.PI / 180.0 : thetaRad;
                    File.AppendAllText(
                        manifestPath,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0},{1:F6},{2:F8},{3}\n",
                            oneBasedIndex,
                            thetaDeg,
                            thetaRadOut,
                            fileName));
                }

                LogToError($"[RotationCapture] Saved {fileName} seq={frameSeq}");
                return (true, savedPath, "ok");
            }
            catch (Exception ex)
            {
                LogToError($"[RotationCapture] frame {oneBasedIndex} save failed: {ex.Message}");
                return (false, "", ex.Message);
            }
        }

        private async Task<(bool ok, string message)> AddRotationSampleCoreAsync(int runId)
        {
            double thetaRad = TryGetStageRotationThetaRad();
            if (double.IsNaN(thetaRad))
                thetaRad = ManualThetaDegrees * Math.PI / 180.0;

            var (result, _) = await EstimatePoseFromLiveFrameAsync(runId);
            if (result.Status != libs.PoseStatus.Success)
            {
                string m = $"Pose status {result.Status}: {result.StatusMessage}";
                LogToError($"[AddRotationSample#{runId}] Pose failed: {result.StatusMessage}");
                return (false, m);
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

            string okMsg = $"Added sample #{RotationSamples.Count} at θ={thetaRad * 180 / Math.PI:F3}°";
            return (true, okMsg);
        }

        /// <summary>Called from Setup rotation sequence after each Rz step (same semantics as Add sample).</summary>
        public async Task<(bool ok, string message)> TryAddRotationSampleForSequenceAsync()
        {
            int runId = Interlocked.Increment(ref _poseRunSequence);
            if (Interlocked.CompareExchange(ref _poseEstimationRunning, 1, 0) != 0)
                return (false, "Another pose operation is in progress.");

            try
            {
                IsPoseEstimationRunning = true;
                var (ok, msg) = await AddRotationSampleCoreAsync(runId);
                if (ok)
                {
                    ResultStatusMessage = msg;
                    ResultStatusColor = "#4caf50";
                    LogToError($"[RotationSeqCapture#{runId}] {msg}");
                }
                else
                {
                    ResultStatusMessage = $"Capture failed: {msg}";
                    ResultStatusColor = "#ff9800";
                    LogToError($"[RotationSeqCapture#{runId}] {msg}");
                }

                return (ok, msg);
            }
            catch (Exception ex)
            {
                ResultStatusMessage = $"Capture failed: {ex.Message}";
                ResultStatusColor = "#f44336";
                LogToError($"[RotationSeqCapture#{runId}] {ex.Message}");
                return (false, ex.Message);
            }
            finally
            {
                IsPoseEstimationRunning = false;
                Interlocked.Exchange(ref _poseEstimationRunning, 0);
            }
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

                var (ok, msg) = await AddRotationSampleCoreAsync(runId);
                if (!ok)
                {
                    ResultStatusMessage = $"Sample not added — {msg}";
                    ResultStatusColor = "#ff9800";
                    return;
                }

                ResultStatusMessage = msg;
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
            _lastNanoMeasRotationFrames = null;
            _lastNanoMeasRotationSessionDir = null;
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

            bool hasNanoRot = _lastNanoMeasRotationFrames is { Count: > 0 };
            if (_lastResult == null && !hasNanoRot)
            {
                var noRunMessage = $"[{DateTime.Now:HH:mm:ss}] No estimation has been run yet.\n" +
                    "Please run 'Run Pose Estimation' or rotation calibration first.";
                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? noRunMessage : $"{ErrorLog}\n\n{noRunMessage}";
            }
            else if (hasNanoRot)
            {
                var errorInfo = new System.Text.StringBuilder();
                errorInfo.AppendLine("=== Last rotation calibration (NanoMeas.dll) ===");
                errorInfo.AppendLine($"Timestamp: {DateTime.Now:O}");
                errorInfo.AppendLine($"Session: {_lastNanoMeasRotationSessionDir ?? "(unknown)"}");
                foreach (var r in _lastNanoMeasRotationFrames!)
                {
                    errorInfo.AppendLine(
                        $"{r.FileName}  status={r.Status}  decode={r.DecodeSuccess}  " +
                        $"pose=[{FormatPose6(r.Pose)}]  pose_abs=[{FormatPose6(r.PoseAbs)}]");
                }

                var rotationInfo = errorInfo.ToString().TrimEnd();
                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? rotationInfo : $"{ErrorLog}\n\n{rotationInfo}";
                LogToError("[GetError] NanoMeas rotation summary appended");
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
            WindowSize = 10.0;
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
            _lastNanoMeasRotationFrames = null;
            _lastNanoMeasRotationSessionDir = null;
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

        #region IDisposable

        public void Dispose()
        {
            App.CalibrationAppModeChanged -= OnGlobalCalibrationModeChanged;
        }

        #endregion
    }
}
