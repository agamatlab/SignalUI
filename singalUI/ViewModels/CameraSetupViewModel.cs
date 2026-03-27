using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using singalUI.Models;
using singalUI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Threading;
using Stagecontrol;
using PositionData = singalUI.Models.PositionData;

namespace singalUI.ViewModels
{
    public partial class CameraSetupViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _cameraFrameTimer;
        private readonly DispatcherTimer _liveCameraApplyTimer;
        private readonly Random _random = new();
        private readonly Dictionary<string, string> _patternConfigFileMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _logFile = Path.Combine(Path.GetTempPath(), "singalui_debug.log");
        private readonly string _cameraLogFile = Path.Combine(Path.GetTempPath(), "singalui_camera.log");
        private WriteableBitmap? _reusableBitmap;
        private long _latestFrameSeq;
        private long _lastRenderedSeq = -1;
        private long _fpsBaseSeq = -1;
        private DateTime _fpsBaseUtc = DateTime.UtcNow;
        private double? _lastAppliedExposure;
        private double? _lastAppliedGain;
        private double? _lastAppliedFps;
        private double? _lastAppliedEffectiveExposure;
        private DateTime _lastLiveApplyUtc = DateTime.MinValue;
        private static readonly uint[] GrayToBgraLut = Enumerable.Range(0, 256)
            .Select(v => 0xFF000000u | ((uint)v << 16) | ((uint)v << 8) | (uint)v)
            .ToArray();

        private void OnSharedStagePositionChanged()
        {
            var shared = StagePositionService.Current;
            LivePosition.X = shared.X;
            LivePosition.Y = shared.Y;
            LivePosition.Z = shared.Z;
            LivePosition.Rx = shared.Rx;
            LivePosition.Ry = shared.Ry;
            LivePosition.Rz = shared.Rz;

            CurrentPosition.X = shared.X;
            CurrentPosition.Y = shared.Y;
            CurrentPosition.Z = shared.Z;
            CurrentPosition.Rx = shared.Rx;
            CurrentPosition.Ry = shared.Ry;
            CurrentPosition.Rz = shared.Rz;
            NotifyCurrentPositionChanged();
        }

        // Camera Selection & Presets
        [ObservableProperty]
        private ObservableCollection<string> _connectedCameras = new();

        [ObservableProperty]
        private string? _selectedConnectedCamera;

        [ObservableProperty]
        private ObservableCollection<string> _availableCtiFiles = new();

        [ObservableProperty]
        private string? _selectedCtiFile;

        [ObservableProperty]
        private ObservableCollection<string> _cameraPresets = new();

        [ObservableProperty]
        private string? _selectedPreset;

        // Config management
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "singalUI",
            "CameraConfigs");
        private static readonly string[] PatternConfigDirectoryCandidates =
        {
            Path.Combine(Environment.CurrentDirectory, "pattern-configs"),
            Path.Combine(Environment.CurrentDirectory, "singalUI", "pattern-configs"),
            Path.Combine(AppContext.BaseDirectory, "pattern-configs"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "pattern-configs"),
            "/Users/aghamatlabakbarzade/ms/lastSignal/pattern-configs"
        };

        [ObservableProperty]
        private ObservableCollection<string> _savedConfigs = new();

        [ObservableProperty]
        private ObservableCollection<string> _availablePatternConfigs = new();

        [ObservableProperty]
        private string? _selectedPatternConfig;

        partial void OnSelectedPatternConfigChanged(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                LoadSelectedPatternConfig();
            }
        }

        // Pattern Analysis
        [ObservableProperty]
        private int _selectedPatternType = 0; // 0 = CheckerBoard

        [ObservableProperty]
        private int _checkerBoardColumns = 9;

        [ObservableProperty]
        private int _checkerBoardRows = 6;

        [ObservableProperty]
        private double _checkerBoardPitchX = 1.0;

        [ObservableProperty]
        private double _checkerBoardPitchY = 1.0;

        [ObservableProperty]
        private int _checkerBoardUnit = 0; // 0 = mm, 1 = μm, 2 = in

        [ObservableProperty]
        private bool _patternHasCode;

        [ObservableProperty]
        private string _patternConfigType = "Checker Board";

        [ObservableProperty]
        private double _patternConfigPitchSize = 1.0;

        [ObservableProperty]
        private string _patternCompatibleLenses = "Not specified";

        [ObservableProperty]
        private int _codePitchBlocks = 6;

        [ObservableProperty]
        private int _selectedCameraModel = -1;

        [ObservableProperty]
        private int _selectedAlgorithmCode = 0;

        [ObservableProperty]
        private double _zoomLevel = 1.0;

        [ObservableProperty]
        private double _panX = 0.0;

        [ObservableProperty]
        private double _panY = 0.0;

        // Session Name from shared store
        public string SessionName
        {
            get => SharedConfigParametersStore.Instance.SessionName;
            set
            {
                if (SharedConfigParametersStore.Instance.SessionName != value)
                {
                    SharedConfigParametersStore.Instance.SessionName = value;
                    OnPropertyChanged();
                }
            }
        }

        // Auto Mode Toggles
        [ObservableProperty]
        private bool _autoFocus = false;

        [ObservableProperty]
        private bool _autoIllumination = false;

        [ObservableProperty]
        private bool _advancedMode = false;

        // FFT Focus Calculation
        [ObservableProperty]
        private bool _enableFftFocus = false;  // Default: disabled (checkbox unchecked)

        [ObservableProperty]
        private int _fftCalculationInterval = 10;  // Calculate every 10 frames (for performance)

        [ObservableProperty]
        private double _rawFocusValue = 0.0;  // Raw FFT energy value (for debugging)

        // Modular collections for collapsible panels
        [ObservableProperty]
        private ObservableCollection<CollapsiblePanelViewModel> _rightPanels = new();

        [ObservableProperty]
        private ObservableCollection<SettingsSliderViewModel> _cameraSettingsSliders = new();

        [ObservableProperty]
        private ObservableCollection<TwoColumnInputViewModel> _pitchInputs = new();

        [ObservableProperty]
        private CameraParameters _cameraParameters = new();

        partial void OnCameraParametersChanged(CameraParameters value)
        {
            HookCameraParameters(value);
        }

        [ObservableProperty]
        private PositionData _livePosition = new()
        {
            X = 10.2452,
            Y = -5.1120,
            Z = 25.0001,
            Rx = 0.0122,
            Ry = -0.0045,
            Rz = 180.0000
        };

        [ObservableProperty]
        private PositionData _currentPosition = new()
        {
            X = 10.2452,
            Y = -5.1120,
            Z = 25.0001,
            Rx = 0.0122,
            Ry = -0.0045,
            Rz = 180.0000
        };

        [ObservableProperty]
        private bool _isLive = true;

        [ObservableProperty]
        private double _reprojectionError = 0.024;

        [ObservableProperty]
        private bool _cameraConnected = false;

        [ObservableProperty]
        private string _cameraStatus = "Disconnected";

        [ObservableProperty]
        private double _focusLevel = 72;

        // Stage Movement Controls
        [ObservableProperty]
        private string _selectedAxis = "1"; // "1" = X, "2" = Y, "3" = Z

        [ObservableProperty]
        private double _moveStepSize = 0.5;

        [ObservableProperty]
        private int _moveNumSteps = 10;

        [ObservableProperty]
        private double _moveTargetPosition = 0.0;

        [ObservableProperty]
        private int _moveAbsoluteStepCount = 10;  // Number of steps to divide absolute move into

        [ObservableProperty]
        private double _moveRelativeDistance = 1.0;

        [ObservableProperty]
        private string _movementStatus = "Ready";

        // Error log for UI display
        private const int MaxErrorLogLines = 100;

        [ObservableProperty]
        private string _errorLog = "";

        // Camera Frame Display
        [ObservableProperty]
        private WriteableBitmap? _cameraFrame;
    
        [ObservableProperty]
        private string _cameraFrameInfo = "No camera";

        [ObservableProperty]
        private long _frameCount = 0;

        [ObservableProperty]
        private bool _isAcquiring = false;

        [ObservableProperty]
        private double _liveFps = 0;



        public CameraSetupViewModel()
        {
            Console.WriteLine("[CameraSetupViewModel] Constructor START");
            // Initialize logging
            File.WriteAllText(_logFile, $"=== Log started at {DateTime.Now:O} ===\n");
            File.WriteAllText(_cameraLogFile, $"=== Camera log started at {DateTime.Now:O} ===\n");
            Log("CameraSetupViewModel constructor called");

            StagePositionService.Start();
            StagePositionService.PositionChanged += OnSharedStagePositionChanged;
            OnSharedStagePositionChanged();

            // Wire up camera service events
            WireUpCameraService();

            // Load saved configs
            LoadSavedConfigs();
            LoadPatternConfigs();

            // NOTE: Position timer disabled - using real gRPC stream instead
            // _positionUpdateTimer = new Timer(100) { AutoReset = true };
            // _positionUpdateTimer.Elapsed += (s, e) => OnPositionUpdate(null!);
            // _positionUpdateTimer.Start();
            Log("Using gRPC position stream (timer disabled)");

            // Setup UI timer for camera frame updates (60 FPS = ~16ms)
            _cameraFrameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _cameraFrameTimer.Tick += (s, e) => UpdateCameraFrame();
            _cameraFrameTimer.Start();
            Log("Camera frame timer started");

            // Apply exposure/gain/fps continuously even when frame rendering cadence changes.
            _liveCameraApplyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _liveCameraApplyTimer.Tick += (s, e) => TryApplyLiveCameraParameters();
            _liveCameraApplyTimer.Start();
            Log("Live camera apply timer started");

            InitializeModularCollections();
            HookCameraParameters(CameraParameters);
            Log("Collections initialized");
            Log($"Initial LivePosition: X={LivePosition.X}, Y={LivePosition.Y}, Z={LivePosition.Z}");
            Console.WriteLine("[CameraSetupViewModel] Constructor END");
            LogToError("CameraSetupViewModel initialized - Error log ready");
        }

        private void WireUpCameraService()
        {
            try
            {
                var cameraService = App.CameraService;
                if (cameraService == null)
                {
                    Log("[CameraService] Camera service is null");
                    return;
                }

                // Subscribe to camera service events
                cameraService.FrameUpdated += OnCameraFrameUpdated;
                cameraService.StatusChanged += OnCameraServiceStatusChanged;
                cameraService.ConnectionChanged += OnCameraConnectionChanged;

                // Update initial connection state
                CameraConnected = cameraService.IsConnected;
                CameraStatus = cameraService.IsConnected ? "Connected" : "Disconnected";

                Log($"[CameraService] Wired up. Connected: {cameraService.IsConnected}, Serial: {cameraService.CameraSerial}");
            }
            catch (Exception ex)
            {
                Log($"[CameraService] Error wiring up camera service: {ex.Message}");
                Console.WriteLine($"[CameraService] Wire-up error: {ex.Message}");
            }
        }

        private void OnCameraFrameUpdated(long seq)
        {
            // Capture thread callback: store latest seq only, UI thread pulls actual frame on timer.
            // Avoid per-frame property changes from non-UI thread.
            _latestFrameSeq = seq;
        }

        /// <summary>
        /// UI timer callback: Pull latest frame and render (like Python's cv2.imshow in the loop)
        /// </summary>
        private void UpdateCameraFrame()
        {
            try
            {
                var cameraService = App.CameraService;
                if (cameraService == null) return;

                // Get snapshot from service (stable front buffer, no concurrent overwrite)
                var (buffer, width, height, seq) = cameraService.GetLatestFrameSnapshot();

                // Avoid reprocessing the same frame multiple times on UI timer ticks.
                if (seq <= 0 || seq == _lastRenderedSeq)
                {
                    return;
                }

                // Log frame status every 60 frames
                if (seq % 60 == 0)
                {
                    string status = buffer == null ? "NULL" : $"OK ({width}x{height})";
                    Log($"[CameraFrame] Frame #{seq} - Buffer: {status}, Connected: {cameraService.IsConnected}");
                }

                if (buffer == null || width == 0 || height == 0)
                {
                    return;
                }

                LogFpsIfDue(seq, width, height);
                TryApplyLiveCameraParameters();

                // Create or reuse bitmap
                if (_reusableBitmap == null || _reusableBitmap.PixelSize.Width != width || _reusableBitmap.PixelSize.Height != height)
                {
                    _reusableBitmap?.Dispose();
                    _reusableBitmap = new WriteableBitmap(
                        new PixelSize(width, height),
                        new Avalonia.Vector(96, 96),
                        Avalonia.Platform.PixelFormat.Bgra8888);
                }

                // Faster grayscale->BGRA expansion via lookup-table uint writes.
                using var fb = _reusableBitmap.Lock();
                int dstRowBytes = fb.RowBytes;
                unsafe
                {
                    fixed (byte* srcBase = buffer)
                    fixed (uint* lut = GrayToBgraLut)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            byte* srcRow = srcBase + (y * width);
                            uint* dstRow = (uint*)((byte*)fb.Address + (y * dstRowBytes));

                            for (int x = 0; x < width; x++)
                            {
                                dstRow[x] = lut[srcRow[x]];
                            }
                        }
                    }
                }

                // Update UI (we're already on UI thread)
                CameraFrame = _reusableBitmap;
                OnPropertyChanged(nameof(CameraFrame));
                CameraFrameInfo = $"{width}x{height} | Frame: {seq} (latest: {_latestFrameSeq})";
                FrameCount = seq;
                _lastRenderedSeq = seq;

                // Calculate real focus quality using FFT (only if enabled)
                if (EnableFftFocus && seq % FftCalculationInterval == 0)
                {
                    try
                    {
                        double rawFocus = FocusCalculator.CalculateFFTFocus(buffer, width, height, downsampleFactor: 4);
                        RawFocusValue = rawFocus;
                        FocusLevel = rawFocus;  // Already normalized to 0-100
                        
                        // Log every 60 calculations
                        if (seq % (FftCalculationInterval * 60) == 0)
                        {
                            Log($"[FFT Focus] Frame {seq}: {FocusLevel:F1}% (raw: {RawFocusValue:F2})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[FFT Focus] Calculation error: {ex.Message}");
                        // Keep previous FocusLevel value on error
                    }
                }
                else if (!EnableFftFocus)
                {
                    // When FFT is disabled, show a neutral value
                    FocusLevel = 50.0;
                }
            }
            catch (Exception ex)
            {
                Log($"[CameraFrame] Error updating frame: {ex.Message}");
            }
        }

        private void LogFpsIfDue(long seq, int width, int height)
        {
            if (seq <= 0)
            {
                return;
            }

            var now = DateTime.UtcNow;

            if (_fpsBaseSeq < 0 || seq < _fpsBaseSeq)
            {
                _fpsBaseSeq = seq;
                _fpsBaseUtc = now;
                return;
            }

            double elapsedSec = (now - _fpsBaseUtc).TotalSeconds;
            if (elapsedSec < 1.0)
            {
                return;
            }

            long frameDelta = seq - _fpsBaseSeq;
            double fps = frameDelta > 0 ? frameDelta / elapsedSec : 0.0;
            LiveFps = fps;
            Log($"[CameraFPS] fps={fps:F2}, frames={frameDelta}, window={elapsedSec:F2}s, seq={seq}, res={width}x{height}");

            _fpsBaseSeq = seq;
            _fpsBaseUtc = now;
        }

        private void LogCameraConfigSnapshot(string source)
        {
            var snapshot =
                $"[{source}] SelectedCamera={SelectedConnectedCamera ?? "(none)"}, " +
                $"CTI={SelectedCtiFile ?? "(none)"}, Preset={SelectedPreset ?? "(none)"}, " +
                $"Exposure={CameraParameters.Exposure}, Gain={CameraParameters.Gain}, FPS={CameraParameters.Fps}, Binning={CameraParameters.Binning}, " +
                $"PatternType={SelectedPatternType}, Grid={CheckerBoardColumns}x{CheckerBoardRows}, Pitch={CheckerBoardPitchX}x{CheckerBoardPitchY}, Unit={CheckerBoardUnit}, " +
                $"AutoFocus={AutoFocus}, AutoIllumination={AutoIllumination}";
            Log($"[CameraConfig] {snapshot}");
        }

        private void HookCameraParameters(CameraParameters? parameters)
        {
            if (parameters == null)
            {
                return;
            }

            parameters.PropertyChanged -= OnCameraParametersPropertyChanged;
            parameters.PropertyChanged += OnCameraParametersPropertyChanged;
            ResetAppliedCameraParameters();
        }

        private void OnCameraParametersPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            ResetAppliedCameraParameters();
            TryApplyLiveCameraParameters();
        }

        private void ResetAppliedCameraParameters()
        {
            _lastAppliedExposure = null;
            _lastAppliedGain = null;
            _lastAppliedFps = null;
            _lastAppliedEffectiveExposure = null;
        }

        private void TryApplyLiveCameraParameters()
        {
            var cameraService = App.CameraService;
            if (cameraService == null || !cameraService.IsConnected)
            {
                return;
            }

            // Throttle hardware writes while still feeling real-time in UI.
            var now = DateTime.UtcNow;
            if ((now - _lastLiveApplyUtc).TotalMilliseconds < 120)
            {
                return;
            }
            _lastLiveApplyUtc = now;

            double exposure = CameraParameters.Exposure;
            double gain = CameraParameters.Gain;
            double fps = CameraParameters.Fps;
            double illumination = Math.Clamp(CameraParameters.Illumination, 0.0, 100.0);
            // Illumination always affects effective exposure in this app path.
            double effectiveExposure = exposure * Math.Max(illumination / 100.0, 0.01);

            bool exposureChanged = !_lastAppliedEffectiveExposure.HasValue || Math.Abs(effectiveExposure - _lastAppliedEffectiveExposure.Value) > 0.001;
            bool gainChanged = !_lastAppliedGain.HasValue || Math.Abs(gain - _lastAppliedGain.Value) > 0.001;
            bool fpsChanged = !_lastAppliedFps.HasValue || Math.Abs(fps - _lastAppliedFps.Value) > 0.001;

            if (exposureChanged)
            {
                cameraService.SetExposure(effectiveExposure);
                _lastAppliedExposure = exposure;
                _lastAppliedEffectiveExposure = effectiveExposure;
            }

            if (gainChanged)
            {
                cameraService.SetGain(gain);
                _lastAppliedGain = gain;
            }

            if (fpsChanged)
            {
                cameraService.SetFrameRate(fps);
                _lastAppliedFps = fps;
            }

            if (exposureChanged || gainChanged || fpsChanged)
            {
                Log($"[LiveApply] Exposure={exposure:F1}us EffectiveExposure={effectiveExposure:F1}us Illum={illumination:F1}% AutoIllum={AutoIllumination} Gain={gain:F2}dB FPS={fps:F2}");

                if (cameraService.TryReadAppliedParameters(out var appliedExposure, out var appliedGain, out var appliedFps))
                {
                    Log($"[LiveConfirm] Camera Exposure={appliedExposure:F1}us Gain={appliedGain:F2}dB FPS={appliedFps:F2}");
                }
            }
        }

        private void OnCameraServiceStatusChanged(string status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Log($"[CameraService] Status: {status}");
                // Don't set CameraStatus here to avoid recursion with OnCameraStatusChanged
            });
        }

        private void OnCameraConnectionChanged(bool connected)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Log($"[CameraService] Connection changed: {connected}");
                CameraConnected = connected;
                CameraStatus = connected ? "Connected" : "Disconnected";
                IsAcquiring = false;

                if (!connected)
                {
                    CameraFrame = null;
                    CameraFrameInfo = "Disconnected";
                    _lastRenderedSeq = -1;
                }
            });
        }

        private void NotifyCurrentPositionChanged()
        {
            // This tells Avalonia to re-read all CurrentPosition.X, CurrentPosition.Y, etc. bindings
            OnPropertyChanged(nameof(CurrentPosition));
        }

        /// <summary>
        /// Update position for a specific axis in the UI
        /// </summary>
        private void UpdatePositionForAxis(AxisType axis, double position)
        {
            switch (axis)
            {
                case AxisType.X:
                    LivePosition.X = position;
                    CurrentPosition.X = position;
                    break;
                case AxisType.Y:
                    LivePosition.Y = position;
                    CurrentPosition.Y = position;
                    break;
                case AxisType.Z:
                    LivePosition.Z = position;
                    CurrentPosition.Z = position;
                    break;
                case AxisType.Rx:
                    LivePosition.Rx = position;
                    CurrentPosition.Rx = position;
                    break;
                case AxisType.Ry:
                    LivePosition.Ry = position;
                    CurrentPosition.Ry = position;
                    break;
                case AxisType.Rz:
                    LivePosition.Rz = position;
                    CurrentPosition.Rz = position;
                    break;
            }
            NotifyCurrentPositionChanged();
            Log($"[UI] Updated {axis} position to {position:F4}");
        }

        private void Log(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                File.AppendAllText(_logFile, logMessage);
                File.AppendAllText(_cameraLogFile, logMessage);
                Debug.WriteLine(message);

                // Also add to error log for UI display
                LogToError(message);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private void LogToError(string message)
        {
            try
            {
                string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                ErrorLog = logMessage + "\n" + ErrorLog;

                // Keep only last MaxErrorLogLines lines
                var lines = ErrorLog.Split('\n');
                if (lines.Length > MaxErrorLogLines)
                {
                    ErrorLog = string.Join("\n", lines.Take(MaxErrorLogLines));
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void ClearErrorLog()
        {
            try
            {
                ErrorLog = "";
                Console.WriteLine("[ErrorLog] Error log cleared");
            }
            catch { }
        }


        private void OnPositionUpdate(Stagecontrol.PositionData grpcPos)
        {
            Log($"[OnPositionUpdate] Called, grpcPos={grpcPos != null}");

            // Ensure we're on the UI thread for property updates
            Dispatcher.UIThread.Post(() =>
            {
                if (grpcPos != null && grpcPos.Positions != null && grpcPos.Positions.Count > 0)
                {
                    // Use real gRPC position data - update in place
                    LivePosition.X = grpcPos.Positions.TryGetValue("1", out var x) ? x : 0;
                    LivePosition.Y = grpcPos.Positions.TryGetValue("2", out var y) ? y : 0;
                    LivePosition.Z = grpcPos.Positions.TryGetValue("3", out var z) ? z : 0;
                    LivePosition.Rx = grpcPos.Positions.TryGetValue("4", out var rx) ? rx : 0;
                    LivePosition.Ry = grpcPos.Positions.TryGetValue("5", out var ry) ? ry : 0;
                    LivePosition.Rz = grpcPos.Positions.TryGetValue("6", out var rz) ? rz : 0;
                    Log($"[gRPC Update] X={LivePosition.X:F3}, Y={LivePosition.Y:F3}, Z={LivePosition.Z:F3}");
                }
                else
                {
                    // Simulate position data when gRPC is not available - update in place
                    LivePosition.X += (_random.NextDouble() - 0.5) * 0.01;
                    LivePosition.Y += (_random.NextDouble() - 0.5) * 0.01;
                    LivePosition.Z += (_random.NextDouble() - 0.5) * 0.005;
                    LivePosition.Rx += (_random.NextDouble() - 0.5) * 0.001;
                    LivePosition.Ry += (_random.NextDouble() - 0.5) * 0.001;
                    LivePosition.Rz += (_random.NextDouble() - 0.5) * 0.001;
                    Log($"[Sim Update] X={LivePosition.X:F3}, Y={LivePosition.Y:F3}, Z={LivePosition.Z:F3}");
                }

                // Sync current position with live position
                CurrentPosition.X = LivePosition.X;
                CurrentPosition.Y = LivePosition.Y;
                CurrentPosition.Z = LivePosition.Z;
                CurrentPosition.Rx = LivePosition.Rx;
                CurrentPosition.Ry = LivePosition.Ry;
                CurrentPosition.Rz = LivePosition.Rz;

                // IMPORTANT: Notify that CurrentPosition has changed
                NotifyCurrentPositionChanged();
                Log($"[Sync] CurrentPosition updated to match LivePosition");

                // Update reprojection error
                ReprojectionError = 0.020 + _random.NextDouble() * 0.01;

                // Update focus level (simulate fluctuation)
                FocusLevel = 65 + _random.NextDouble() * 25;
            });
        }

        private void Apply(Stagecontrol.PositionData grpcPos)
        {
            var positions = grpcPos.Positions;
            Log($"[Apply] Called, positions count={positions?.Count ?? 0}");
            // Convert gRPC PositionData to local PositionData - update in place
            LivePosition.X = positions?.TryGetValue("1", out var x) == true ? x : 0;
            LivePosition.Y = positions?.TryGetValue("2", out var y) == true ? y : 0;
            LivePosition.Z = positions?.TryGetValue("3", out var z) == true ? z : 0;
            LivePosition.Rx = positions?.TryGetValue("4", out var rx) == true ? rx : 0;
            LivePosition.Ry = positions?.TryGetValue("5", out var ry) == true ? ry : 0;
            LivePosition.Rz = positions?.TryGetValue("6", out var rz) == true ? rz : 0;

            // Sync current position with live position
            CurrentPosition.X = LivePosition.X;
            CurrentPosition.Y = LivePosition.Y;
            CurrentPosition.Z = LivePosition.Z;
            CurrentPosition.Rx = LivePosition.Rx;
            CurrentPosition.Ry = LivePosition.Ry;
            CurrentPosition.Rz = LivePosition.Rz;

            // IMPORTANT: Notify that CurrentPosition has changed
            NotifyCurrentPositionChanged();
            Log($"[Apply] Updated: X={LivePosition.X:F3}, Y={LivePosition.Y:F3}, Z={LivePosition.Z:F3}");
        }

        private void InitializeModularCollections()
        {
            // Initialize camera settings sliders
            CameraSettingsSliders.Add(new SettingsSliderViewModel("Exposure (μs)", 2500, 100, 100000, 100, false));
            CameraSettingsSliders.Add(new SettingsSliderViewModel("Gain (dB)", 12, 0, 24, 0.1, false));
            CameraSettingsSliders.Add(new SettingsSliderViewModel("Illumination (%)", 80, 0, 100, 1, true));
            CameraSettingsSliders.Add(new SettingsSliderViewModel("Frame Rate (fps)", 30, 1, 60, 0.5, false));

            // Initialize pitch inputs for checkerboard
            PitchInputs.Add(new TwoColumnInputViewModel("Columns", 9, 3, 50, "F0", "Rows", 6, 3, 50, "F0"));

            // Initialize right panels
            InitializeRightPanels();
        }

        private void InitializeRightPanels()
        {
            // Camera Selection Panel
            var cameraSelectionContent = new StackPanel { Spacing = 16 };
            // TODO: Add camera selection UI here

            // Pattern Analysis Panel
            var patternAnalysisContent = new StackPanel { Spacing = 16 };
            // TODO: Add pattern analysis UI here

            // Camera Parameters Panel
            var cameraParamsContent = new StackPanel { Spacing = 18 };
            // TODO: Add camera parameters UI here

            // Stage Position Panel
            var stagePositionContent = new StackPanel { Spacing = 16 };
            // TODO: Add stage position UI here

            // RightPanels will be populated later with actual controls
        }

        [RelayCommand]
        private void AutoAdjust()
        {
            // Simulate auto-adjustment by optimizing parameters
            CameraParameters = new CameraParameters
            {
                Exposure = 15000,
                Gain = 10,
                Illumination = 85,
                Fps = 30,
                Binning = BinningMode.Bin1x1
            };
            FocusLevel = 95;
        }

        [RelayCommand]
        private void CaptureImage()
        {
            try
            {
                var cameraService = App.CameraService;
                var (buffer, width, height, seq) = cameraService.GetLatestFrameSnapshot();

                if (buffer == null || width <= 0 || height <= 0)
                {
                    Log("[CaptureImage] No frame available to save");
                    CameraStatus = "No frame available";
                    return;
                }

                string outputDir = Path.Combine(Environment.CurrentDirectory, "imgs");
                Directory.CreateDirectory(outputDir);

                string fileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                string filePath = Path.Combine(outputDir, fileName);

                using var mat = new Mat(height, width, MatType.CV_8UC1, buffer);
                Cv2.ImWrite(filePath, mat);

                Log($"[CaptureImage] Saved frame {seq} to {filePath}");
                CameraStatus = $"Captured: {fileName}";
            }
            catch (Exception ex)
            {
                Log($"[CaptureImage] Error: {ex.Message}");
                CameraStatus = $"Capture failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task RefreshConnection()
        {
            try
            {
                Log("[RefreshConnection] Refreshing connections...");

                var cameraService = App.CameraService;
                if (cameraService != null)
                {
                    // Stop any ongoing acquisition
                    cameraService.StopAcquisition();
                    IsAcquiring = false;

                    // Re-initialize camera
                    var success = await Task.Run(() => cameraService.Initialize());
                    CameraConnected = success;
                    CameraStatus = success ? "Connected" : "Failed to connect";
                    Log($"[RefreshConnection] Camera: {(success ? "Connected" : "Failed")}");
                }
                else
                {
                    Log("[RefreshConnection] Camera service is null");
                }

            }
            catch (Exception ex)
            {
                Log($"[RefreshConnection] Error: {ex.Message}");
                CameraStatus = $"Error: {ex.Message}";
                CameraConnected = false;
            }
        }

        [RelayCommand]
        private void StartAcquisition()
        {
            try
            {
                var cameraService = App.CameraService;
                if (cameraService == null || !cameraService.IsConnected)
                {
                    Log("[StartAcquisition] Camera not connected");
                    CameraStatus = "Not connected";
                    return;
                }

                _fpsBaseSeq = -1;
                _fpsBaseUtc = DateTime.UtcNow;
                _lastRenderedSeq = -1;
                ResetAppliedCameraParameters();
                LogCameraConfigSnapshot("StartAcquisition");
                Log("[StartAcquisition] Starting acquisition...");
                cameraService.StartAcquisition();
                IsAcquiring = true;
                CameraStatus = "Acquiring...";
                Log("[StartAcquisition] Started");
            }
            catch (Exception ex)
            {
                Log($"[StartAcquisition] Error: {ex.Message}");
                CameraStatus = $"Error: {ex.Message}";
            }
        }

        [RelayCommand]
        private void StopAcquisition()
        {
            try
            {
                var cameraService = App.CameraService;
                if (cameraService == null) return;

                Log("[StopAcquisition] Stopping acquisition...");
                cameraService.StopAcquisition();
                IsAcquiring = false;
                CameraStatus = "Connected";
                Log("[StopAcquisition] Stopped");
                _fpsBaseSeq = -1;
                _lastRenderedSeq = -1;
                LiveFps = 0;
            }
            catch (Exception ex)
            {
                Log($"[StopAcquisition] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ResetDefaults()
        {
            CameraParameters = new CameraParameters
            {
                Exposure = 2500,
                Gain = 12,
                Illumination = 80,
                Fps = 30,
                Binning = BinningMode.Bin1x1
            };

            // Apply to hardware
            try
            {
                var cameraService = App.CameraService;
                if (cameraService != null && cameraService.IsConnected)
                {
                    cameraService.SetExposure(2500);
                    cameraService.SetGain(12);
                    cameraService.SetFrameRate(30);
                    Log("[ResetDefaults] Applied to camera");
                }
            }
            catch (Exception ex)
            {
                Log($"[ResetDefaults] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ApplySettings()
        {
            try
            {
                var cameraService = App.CameraService;
                if (cameraService == null || !cameraService.IsConnected)
                {
                    Log("[ApplySettings] Camera not connected");
                    CameraStatus = "Not connected";
                    return;
                }

                Log($"[ApplySettings] Exposure={CameraParameters.Exposure}, Gain={CameraParameters.Gain}, FPS={CameraParameters.Fps}");

                cameraService.SetExposure(CameraParameters.Exposure);
                cameraService.SetGain(CameraParameters.Gain);
                cameraService.SetFrameRate(CameraParameters.Fps);

                CameraStatus = "Settings applied";
                LogCameraConfigSnapshot("ApplySettings");
                Log("[ApplySettings] Applied successfully");
            }
            catch (Exception ex)
            {
                Log($"[ApplySettings] Error: {ex.Message}");
                CameraStatus = $"Error: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task BrowseCtiFile()
        {
            try
            {
                // Get top-level window
                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (topLevel == null)
                {
                    Log("[BrowseCtiFile] No top-level window found");
                    return;
                }

                // Open file picker for .cti files
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Camera File (.cti)",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Camera Configuration Files")
                        {
                            Patterns = new[] { "*.cti" },
                            AppleUniformTypeIdentifiers = new[] { "public.file" }
                        },
                        new FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*" },
                            AppleUniformTypeIdentifiers = new[] { "public.file" }
                        }
                    }
                });

                if (files.Count > 0)
                {
                    var file = files[0];
                    SelectedCtiFile = file.Path.LocalPath;
                    Log($"[BrowseCtiFile] Selected: {SelectedCtiFile}");

                    // Add to available CTI files if not already there
                    if (!AvailableCtiFiles.Contains(SelectedCtiFile))
                    {
                        AvailableCtiFiles.Add(SelectedCtiFile);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[BrowseCtiFile] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void SavePreset()
        {
            try
            {
                var config = CameraConfig.FromViewModel(this);
                config.Name = SelectedPreset ?? $"Preset_{DateTime.Now:yyyyMMdd_HHmmss}";

                // Ensure directory exists
                Directory.CreateDirectory(ConfigDirectory);

                var filePath = Path.Combine(ConfigDirectory, $"{config.Name}.json");
                File.WriteAllText(filePath, config.ToJson());

                Log($"[SavePreset] Saved: {config.Name}");

                // Refresh config list
                LoadSavedConfigs();

                // Select the saved config
                SelectedPreset = config.Name;
            }
            catch (Exception ex)
            {
                Log($"[SavePreset] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void DeletePreset()
        {
            try
            {
                if (string.IsNullOrEmpty(SelectedPreset))
                {
                    Log("[DeletePreset] No preset selected");
                    return;
                }

                var filePath = Path.Combine(ConfigDirectory, $"{SelectedPreset}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Log($"[DeletePreset] Deleted: {SelectedPreset}");
                }

                // Refresh config list
                LoadSavedConfigs();
            }
            catch (Exception ex)
            {
                Log($"[DeletePreset] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Load all saved config files
        /// </summary>
        private void LoadSavedConfigs()
        {
            try
            {
                SavedConfigs.Clear();

                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                    return;
                }

                foreach (var file in Directory.GetFiles(ConfigDirectory, "*.json"))
                {
                    SavedConfigs.Add(Path.GetFileNameWithoutExtension(file));
                }

                Log($"[LoadSavedConfigs] Found {SavedConfigs.Count} configs");
            }
            catch (Exception ex)
            {
                Log($"[LoadSavedConfigs] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Load selected config
        /// </summary>
        [RelayCommand]
        private void LoadConfig()
        {
            try
            {
                if (string.IsNullOrEmpty(SelectedPreset))
                {
                    Log("[LoadConfig] No config selected");
                    return;
                }

                var filePath = Path.Combine(ConfigDirectory, $"{SelectedPreset}.json");
                if (!File.Exists(filePath))
                {
                    Log($"[LoadConfig] Config not found: {SelectedPreset}");
                    return;
                }

                var json = File.ReadAllText(filePath);
                var config = CameraConfig.FromJson(json);

                if (config != null)
                {
                    config.ApplyToViewModel(this);
                    Log($"[LoadConfig] Loaded: {config.Name}");
                }
                else
                {
                    Log($"[LoadConfig] Failed to parse config: {SelectedPreset}");
                }
            }
            catch (Exception ex)
            {
                Log($"[LoadConfig] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Save config with a custom name
        /// </summary>
        [RelayCommand]
        private async Task SaveConfigAs()
        {
            try
            {
                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (topLevel == null)
                {
                    Log("[SaveConfigAs] No top-level window found");
                    return;
                }

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Camera Config",
                    DefaultExtension = "json",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Camera Config")
                        {
                            Patterns = new[] { "*.json" }
                        }
                    },
                    SuggestedFileName = $"CameraConfig_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                });

                if (file != null)
                {
                    var config = CameraConfig.FromViewModel(this);
                    config.Name = Path.GetFileNameWithoutExtension(file.Name);

                    // Ensure parent directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Path.LocalPath)!);

                    File.WriteAllText(file.Path.LocalPath, config.ToJson());
                    Log($"[SaveConfigAs] Saved: {file.Path.LocalPath}");
                }
            }
            catch (Exception ex)
            {
                Log($"[SaveConfigAs] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Load config from file
        /// </summary>
        [RelayCommand]
        private async Task LoadConfigFromFile()
        {
            try
            {
                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (topLevel == null)
                {
                    Log("[LoadConfigFromFile] No top-level window found");
                    return;
                }

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Load Camera Config",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Camera Config")
                        {
                            Patterns = new[] { "*.json" }
                        }
                    }
                });

                if (files.Count > 0)
                {
                    var json = await File.ReadAllTextAsync(files[0].Path.LocalPath);
                    var config = CameraConfig.FromJson(json);

                    if (config != null)
                    {
                        config.ApplyToViewModel(this);
                        Log($"[LoadConfigFromFile] Loaded: {files[0].Name}");
                    }
                    else
                    {
                        Log($"[LoadConfigFromFile] Failed to parse config");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[LoadConfigFromFile] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void LoadSelectedPatternConfig()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SelectedPatternConfig))
                {
                    Log("[LoadSelectedPatternConfig] No pattern config selected");
                    return;
                }

                if (!_patternConfigFileMap.TryGetValue(SelectedPatternConfig, out var path) || !File.Exists(path))
                {
                    Log($"[LoadSelectedPatternConfig] Pattern config not found for selection: {SelectedPatternConfig}");
                    return;
                }

                var json = File.ReadAllText(path);
                var config = CameraConfig.FromJson(json);

                if (config == null)
                {
                    Log("[LoadSelectedPatternConfig] Failed to parse config");
                    return;
                }

                config.ApplyPatternToViewModel(this);
                Log($"[LoadSelectedPatternConfig] Loaded pattern config: {SelectedPatternConfig}");
            }
            catch (Exception ex)
            {
                Log($"[LoadSelectedPatternConfig] Error: {ex.Message}");
            }
        }

        private void LoadPatternConfigs()
        {
            try
            {
                var directory = EnsurePatternConfigDirectory();
                AvailablePatternConfigs.Clear();
                _patternConfigFileMap.Clear();

                Log($"[LoadPatternConfigs] Searching directory: {directory}");
                
                if (!Directory.Exists(directory))
                {
                    Log($"[LoadPatternConfigs] Directory does not exist: {directory}");
                    return;
                }

                var files = Directory.GetFiles(directory, "*.json");
                Log($"[LoadPatternConfigs] Found {files.Length} JSON files");

                foreach (var file in files.OrderBy(Path.GetFileName))
                {
                    Log($"[LoadPatternConfigs] Processing file: {file}");
                    var displayName = GetPatternConfigDisplayName(file);
                    var uniqueName = displayName;
                    var duplicateIndex = 2;

                    while (_patternConfigFileMap.ContainsKey(uniqueName))
                    {
                        uniqueName = $"{displayName} ({duplicateIndex})";
                        duplicateIndex++;
                    }

                    _patternConfigFileMap[uniqueName] = file;
                    AvailablePatternConfigs.Add(uniqueName);
                    Log($"[LoadPatternConfigs] Added config: {uniqueName}");
                }

                if (AvailablePatternConfigs.Count > 0 && string.IsNullOrWhiteSpace(SelectedPatternConfig))
                {
                    SelectedPatternConfig = AvailablePatternConfigs[0];
                }

                Log($"[LoadPatternConfigs] Loaded {AvailablePatternConfigs.Count} pattern configs from {directory}");
            }
            catch (Exception ex)
            {
                Log($"[LoadPatternConfigs] Error: {ex.Message}");
                Log($"[LoadPatternConfigs] Stack trace: {ex.StackTrace}");
            }
        }

        private static string GetPatternConfigDisplayName(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var config = CameraConfig.FromJson(json);
                if (!string.IsNullOrWhiteSpace(config?.Name))
                {
                    return config.Name;
                }
            }
            catch
            {
                // Fall back to the filename if parsing fails.
            }

            return Path.GetFileNameWithoutExtension(filePath);
        }

        private string EnsurePatternConfigDirectory()
        {
            foreach (var candidate in PatternConfigDirectoryCandidates)
            {
                Log($"[EnsurePatternConfigDirectory] Checking: {candidate}");
                if (Directory.Exists(candidate))
                {
                    Log($"[EnsurePatternConfigDirectory] Found: {candidate}");
                    return candidate;
                }
            }

            Log($"[EnsurePatternConfigDirectory] No existing directory found, creating: {PatternConfigDirectoryCandidates[0]}");
            Directory.CreateDirectory(PatternConfigDirectoryCandidates[0]);
            return PatternConfigDirectoryCandidates[0];
        }

        // Stage Movement Commands
        [RelayCommand]
        private async Task MoveSingle()
        {
            try
            {
                MovementStatus = $"Moving axis {SelectedAxis}...";
                Log($"[MoveSingle] Axis={SelectedAxis}, StepSize={MoveStepSize}, NumSteps={MoveNumSteps}");

                // Convert axis string to AxisType enum: "1"=X, "2"=Y, "3"=Z, "4"=Rx, "5"=Ry, "6"=Rz
                var axisType = SelectedAxis switch
                {
                    "1" => AxisType.X,
                    "2" => AxisType.Y,
                    "3" => AxisType.Z,
                    "4" => AxisType.Rx,
                    "5" => AxisType.Ry,
                    "6" => AxisType.Rz,
                    _ => AxisType.X
                };

                // Get wrapper for this axis from StageManager
                var wrapper = StageManager.GetWrapperForAxis(axisType);
                if (wrapper == null || !wrapper.IsConnected)
                {
                    MovementStatus = $"Failed: No stage connected for axis {SelectedAxis}";
                    Log($"[MoveSingle] No stage connected for axis {SelectedAxis}");
                    return;
                }

                // Calculate total distance (raw steps for SigmaKoki)
                double distance = GetStepAmountForController(wrapper, MoveStepSize) * MoveNumSteps;

                // Move using StageManager (direct USB/serial control)
                await Task.Run(() => wrapper.MoveAxis(axisType, distance));

                // Update UI with new position
                double newPos = wrapper.GetAxisPosition(axisType);
                UpdatePositionForAxis(axisType, newPos);

                MovementStatus = $"Success: Moved {distance:F3}, pos: {newPos:F4}";
                Log($"[MoveSingle] Success: Moved axis {SelectedAxis} by {distance:F3}, new pos: {newPos:F4}");
            }
            catch (Exception ex)
            {
                MovementStatus = $"Error: {ex.Message}";
                Log($"[MoveSingle] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task MoveToAbsolute()
        {
            try
            {
                // Convert axis string to AxisType enum
                var axisType = SelectedAxis switch
                {
                    "1" => AxisType.X,
                    "2" => AxisType.Y,
                    "3" => AxisType.Z,
                    "4" => AxisType.Rx,
                    "5" => AxisType.Ry,
                    "6" => AxisType.Rz,
                    _ => AxisType.X
                };

                // Get wrapper for this axis from StageManager
                var wrapper = StageManager.GetWrapperForAxis(axisType);
                if (wrapper == null || !wrapper.IsConnected)
                {
                    MovementStatus = $"Failed: No stage connected for axis {SelectedAxis}";
                    Log($"[MoveToAbsolute] No stage connected for axis {SelectedAxis}");
                    return;
                }

                // Use UI StepSize/StepCount directly (relative stepping)
                double currentPos = wrapper.GetAxisPosition(axisType);
                int stepCount = MoveAbsoluteStepCount > 0 ? MoveAbsoluteStepCount : 1;
                double stepSize = GetStepAmountForController(wrapper, MoveStepSize);
                double totalMove = stepSize * stepCount;

                MovementStatus = $"Moving axis {SelectedAxis} by {stepSize:F3} × {stepCount} steps...";
                Log($"[MoveToAbsolute] Axis={SelectedAxis}, Current={currentPos:F3}, StepSize={stepSize:F4}, Steps={stepCount}, Total={totalMove:F4}");

                // Execute multiple small moves
                for (int i = 0; i < stepCount; i++)
                {
                    await Task.Run(() => wrapper.MoveAxis(axisType, stepSize));
                    Log($"[MoveToAbsolute] Step {i + 1}/{stepCount} completed");
                }

                // Update UI with final position
                double newPos = wrapper.GetAxisPosition(axisType);
                UpdatePositionForAxis(axisType, newPos);

                MovementStatus = $"Success: Moved to {newPos:F3}";
                Log($"[MoveToAbsolute] Success: Axis {SelectedAxis} moved from {currentPos:F3} to {newPos:F3}");
            }
            catch (Exception ex)
            {
                MovementStatus = $"Error: {ex.Message}";
                Log($"[MoveToAbsolute] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task MoveRelative()
        {
            try
            {
                MovementStatus = $"Moving axis {SelectedAxis} by {MoveRelativeDistance:F3}...";
                Log($"[MoveRelative] Axis={SelectedAxis}, Distance={MoveRelativeDistance:F3}");

                // Convert axis string to AxisType enum
                var axisType = SelectedAxis switch
                {
                    "1" => AxisType.X,
                    "2" => AxisType.Y,
                    "3" => AxisType.Z,
                    "4" => AxisType.Rx,
                    "5" => AxisType.Ry,
                    "6" => AxisType.Rz,
                    _ => AxisType.X
                };

                // Get wrapper for this axis from StageManager
                var wrapper = StageManager.GetWrapperForAxis(axisType);
                if (wrapper == null || !wrapper.IsConnected)
                {
                    MovementStatus = $"Failed: No stage connected for axis {SelectedAxis}";
                    Log($"[MoveRelative] No stage connected for axis {SelectedAxis}");
                    return;
                }

                // Move using StageManager (direct USB/serial control)
                await Task.Run(() => wrapper.MoveAxis(axisType, GetStepAmountForController(wrapper, MoveRelativeDistance)));

                // Update UI with new position
                double newPos = wrapper.GetAxisPosition(axisType);
                UpdatePositionForAxis(axisType, newPos);

                MovementStatus = $"Success: Moved {MoveRelativeDistance:F3}";
                Log($"[MoveRelative] Success: Axis {SelectedAxis} moved by {MoveRelativeDistance:F3}, new pos: {newPos:F4}");
            }
            catch (Exception ex)
            {
                MovementStatus = $"Error: {ex.Message}";
                Log($"[MoveRelative] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void StopMovement()
        {
            try
            {
                MovementStatus = "Stopping...";
                Log("[StopMovement] Stopping all movement");

                // Disconnect all stages using StageManager
                StageManager.DisconnectAll();

                MovementStatus = "Stopped";
                Log("[StopMovement] Success: All stages stopped");
            }
            catch (Exception ex)
            {
                MovementStatus = $"Error: {ex.Message}";
                Log($"[StopMovement] Error: {ex.Message}");
            }
        }

        private static double GetStepAmountForController(StageWrapper wrapper, double uiValue)
        {
            // SigmaKoki expects raw steps from UI; other controllers use mm
            if (wrapper.Controller is SigmakokiController)
            {
                return uiValue;
            }
            return uiValue;
        }

        public void Dispose()
        {
            CameraParameters.PropertyChanged -= OnCameraParametersPropertyChanged;
            StagePositionService.PositionChanged -= OnSharedStagePositionChanged;
        }
    }
}
