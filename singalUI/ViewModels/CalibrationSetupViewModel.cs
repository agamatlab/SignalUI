using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using singalUI.Models;
using singalUI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using singalUI.libs;
using OpenCvSharp;

namespace singalUI.ViewModels
{
    // ==================== POSE ESTIMATION DATA STRUCTURES ====================
    
    public class PoseEstimationTask
    {
        public string ImagePath { get; set; } = "";
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double PositionZ { get; set; }
        public double PositionRx { get; set; }
        public double PositionRy { get; set; }
        public double PositionRz { get; set; }
        public DateTime Timestamp { get; set; }
        public int StepNumber { get; set; }
        
        // Capture configuration parameters at queue time
        public double Fx { get; set; }
        public double Fy { get; set; }
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double PixelSize { get; set; }
        public double FocalLength { get; set; }
        public double PitchX { get; set; }
        public double PitchY { get; set; }
        public double ComponentsX { get; set; }
        public double ComponentsY { get; set; }
        public double WindowSize { get; set; }
        public double CodePitchBlocks { get; set; }
        
        // HMF file paths
        public bool UseCustomHmfPaths { get; set; }
        public string HmfGPath { get; set; } = "";
        public string HmfXPath { get; set; } = "";
        public string HmfYPath { get; set; } = "";
    }
    
    public class PoseEstimationResult
    {
        public string ImagePath { get; set; } = "";
        public double StageX { get; set; }
        public double StageY { get; set; }
        public double StageZ { get; set; }
        public double StageRx { get; set; }
        public double StageRy { get; set; }
        public double StageRz { get; set; }
        public double EstimatedX { get; set; }
        public double EstimatedY { get; set; }
        public double EstimatedZ { get; set; }
        public double EstimatedRx { get; set; }
        public double EstimatedRy { get; set; }
        public double EstimatedRz { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public int StepNumber { get; set; }
    }
    
    public class PlotPoint
    {
        public int StepNumber { get; set; }
        public double Value { get; set; }
        
        public PlotPoint(int stepNumber, double value)
        {
            StepNumber = stepNumber;
            Value = value;
        }
    }


    public partial class CalibrationSetupViewModel : ViewModelBase
    {
        private readonly string _logFile = Path.Combine(Path.GetTempPath(), "singalui_debug.log");
        private string _lastExportedCsvPath = "";

        // ==================== MULTI-STAGE CONFIGURATION ====================
        // Stage wrappers are managed by the static StageManager service
        // Use StageManager.Wrappers to access all stage wrappers

        [ObservableProperty]
        private StageWrapper? _selectedStageWrapper;

        // Connection status polling timer
        private Timer? _connectionPollTimer;

        [ObservableProperty]
        private int _nextStageId = 1;

        [ObservableProperty]
        private string _overallStageStatus = "No stages configured";

        [ObservableProperty]
        private int _totalConnectedAxes = 0;

        // Legacy properties (kept for backward compatibility, but not used)
        [ObservableProperty]
        [Obsolete("Use StageInstances collection instead")]
        private StageHardwareType _selectedStageHardware = StageHardwareType.None;

        [ObservableProperty]
        [Obsolete("Use StageInstances collection instead")]
        private SigmakokiControllerType _selectedSigmakokiController = SigmakokiControllerType.HSC_103;

        [ObservableProperty]
        [Obsolete("Use StageInstances collection instead")]
        private StageController? _currentStageController;

        [ObservableProperty]
        [Obsolete("Use StageInstances collection instead")]
        private bool _isStageConnected = false;

        [ObservableProperty]
        [Obsolete("Use StageInstances collection instead")]
        private string[] _availableAxes = Array.Empty<string>();

        [ObservableProperty]
        [Obsolete("Use StageInstances collection instead")]
        private string _connectionStatus = "Not connected";

        [ObservableProperty]
        private SamplingMode _samplingMode = SamplingMode.Timed;

        [ObservableProperty]
        private double _timedDuration = 60;

        [ObservableProperty]
        private double _timedInterval = 0.6;

        [ObservableProperty]
        private int _timedPoints = 100;

        [ObservableProperty]
        private double _triggeredDelay = 0; // milliseconds

        [ObservableProperty]
        private double _controlDelay = 0; // milliseconds

        [ObservableProperty]
        private ObservableCollection<MotionConfiguration> _motionRows = new();

        [ObservableProperty]
        private int _totalPlannedPoints = 1;

        [ObservableProperty]
        private double _estimatedTime = 60;

        [ObservableProperty]
        private bool _isSequenceRunning = false;

        // Stage Movement Controls
        [ObservableProperty]
        private string _selectedAxis = "1"; // "1" = X, "2" = Y, "3" = Z

        [ObservableProperty]
        private double _moveStepSize = 1000;  // 1000 nm = 1 micron

        [ObservableProperty]
        private int _moveNumSteps = 10;

        [ObservableProperty]
        private double _moveTargetPosition = 0.0;

        [ObservableProperty]
        private int _moveAbsoluteStepCount = 10;  // Number of steps to divide absolute move into

        [ObservableProperty]
        private double _moveRelativeDistance = 1.0;

        [ObservableProperty]
        private int _moveRelativeStepCount = 1;  // Number of times to repeat relative move

        [ObservableProperty]
        private string _movementStatus = "Ready";

        // Calculated end position for relative mode (display only)
        public double CalculatedEndPosition
        {
            get
            {
                // Get current position based on first enabled axis
                var axisConfig = GetFirstEnabledAxis();
                if (axisConfig == null) return 0.0;

                var axisId = GetAxisId(axisConfig.Axis);
                var wrapper = GetStageWrapper(axisId);
                if (wrapper == null || !wrapper.IsConnected) return 0.0;

                var axisType = AxisIdToAxisType(axisId);
                double currentPos = wrapper.GetAxisPosition(axisType);

                // Convert from mm to µm for display
                if (axisType == AxisType.X || axisType == AxisType.Y || axisType == AxisType.Z)
                {
                    currentPos *= 1000.0;
                }

                // Calculate: end = current + (distance * stepCount)
                return currentPos + (MoveRelativeDistance * MoveRelativeStepCount);
            }
        }

        // Update calculated position when relative distance or step count changes
        partial void OnMoveRelativeDistanceChanged(double value)
        {
            OnPropertyChanged(nameof(CalculatedEndPosition));
        }

        partial void OnMoveRelativeStepCountChanged(int value)
        {
            OnPropertyChanged(nameof(CalculatedEndPosition));
        }

        // Error log for UI display
        private const int MaxErrorLogLines = 100;

        [ObservableProperty]
        private string _errorLog = "";

        // Movement Mode: Absolute vs Relative
        [ObservableProperty]
        private bool _movementModeAbsolute = true; // true = Absolute, false = Relative

        // Current Position from stage controllers
        [ObservableProperty]
        private double _currentPositionX = 0.0;

        [ObservableProperty]
        private double _currentPositionY = 0.0;

        [ObservableProperty]
        private double _currentPositionZ = 0.0;

        [ObservableProperty]
        private double _currentPositionRx = 0.0;

        [ObservableProperty]
        private double _currentPositionRy = 0.0;

        [ObservableProperty]
        private double _currentPositionRz = 0.0;

        // Position polling timer
        private Timer? _positionPollTimer;

        // ==================== POSE ESTIMATION SCAN CONFIGURATION ====================
        
        [ObservableProperty]
        private int _stepPauseDurationMs = 100; // Pause duration at each step in milliseconds
        
        [ObservableProperty]
        private int _maxConcurrentPoseEstimations = 4; // Max concurrent pose estimation threads
        
        [ObservableProperty]
        private bool _enablePoseEstimationScan = false; // Enable pose estimation during movement
        
        [ObservableProperty]
        private string _captureDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SignalUI_Captures");
        
        [ObservableProperty]
        private int _capturedImageCount = 0;
        
        [ObservableProperty]
        private int _processedPoseCount = 0;
        
        [ObservableProperty]
        private int _queuedPoseCount = 0;
        
        // Computed property for graph visibility
        public bool HasPoseResults => ProcessedPoseCount > 0;
        
        // Notify when ProcessedPoseCount changes
        partial void OnProcessedPoseCountChanged(int value)
        {
            OnPropertyChanged(nameof(HasPoseResults));
        }

        // Pose estimation queue and processing
        private readonly System.Collections.Concurrent.ConcurrentQueue<PoseEstimationTask> _poseEstQueue = new();
        private readonly List<PoseEstimationResult> _poseResults = new();
        private readonly SemaphoreSlim _poseEstSemaphore = new(4); // Default to 4 concurrent
        private readonly SemaphoreSlim _poseEstDllSemaphore = new(1); // Only 1 DLL call at a time to prevent crashes
        private CancellationTokenSource? _poseEstCts;
        private readonly object _poseResultsLock = new();

        // Graph data for visualization
        [ObservableProperty]
        private ObservableCollection<PlotPoint> _errorXData = new();
        
        [ObservableProperty]
        private ObservableCollection<PlotPoint> _errorYData = new();
        
        [ObservableProperty]
        private ObservableCollection<PlotPoint> _errorZData = new();
        
        [ObservableProperty]
        private ObservableCollection<Models.Point3D> _stagePositions3D = new();
        
        [ObservableProperty]
        private ObservableCollection<Models.Point3D> _estimatedPositions3D = new();
        
        [ObservableProperty]
        private double _maxErrorX = 0;
        
        [ObservableProperty]
        private double _maxErrorY = 0;
        
        [ObservableProperty]
        private double _maxErrorZ = 0;

        public CalibrationSetupViewModel()
        {
            // Initialize static StageManager with default stages FIRST
            StageManager.InitializeDefaultStages();

            // THEN subscribe to wrapper property changes
            InitializeMotionRows();

            CalculateTotals();
            StartConnectionPolling();
            StartPositionPolling();
            
            // Ensure capture directory exists
            Directory.CreateDirectory(CaptureDirectory);
            
            // Ensure Archive directory with HMF pattern files exists
            EnsureArchiveFiles();
            
            // Explicitly set pose estimation counts to 0 to ensure UI binding works
            ProcessedPoseCount = 0;
            CapturedImageCount = 0;
            QueuedPoseCount = 0;
            
            Log($"CalibrationSetupViewModel initialized - ProcessedPoseCount={ProcessedPoseCount}");
        }
        
        // Update semaphore when max concurrent threads changes
        partial void OnMaxConcurrentPoseEstimationsChanged(int value)
        {
            if (value < 1) value = 1;
            if (value > 32) value = 32;
            
            // Recreate semaphore with new count
            // Note: We can't directly change SemaphoreSlim count, so we'll handle this in the processing loop
            Log($"[PoseEst] Max concurrent threads set to {value}");
        }

        // Expose static StageManager.Wrappers as a property for UI binding
        public ObservableCollection<StageWrapper> StageWrappers => StageManager.Wrappers;

        // ==================== CONNECTION STATUS POLLING ====================

        /// <summary>
        /// Start periodic connection status checking
        /// </summary>
        private void StartConnectionPolling()
        {
            // Check every 2 seconds
            _connectionPollTimer = new Timer(callback => CheckAllConnections(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }

        /// <summary>
        /// Stop periodic connection status checking
        /// </summary>
        private void StopConnectionPolling()
        {
            _connectionPollTimer?.Dispose();
            _connectionPollTimer = null;
        }

        /// <summary>
        /// Start periodic position polling
        /// </summary>
        private void StartPositionPolling()
        {
            // Poll positions every 500ms for real-time updates
            _positionPollTimer = new Timer(callback => UpdatePositions(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        }

        /// <summary>
        /// Stop periodic position polling
        /// </summary>
        private void StopPositionPolling()
        {
            _positionPollTimer?.Dispose();
            _positionPollTimer = null;
        }

        /// <summary>
        /// Update current position from all connected stages
        /// Note: PI controller returns position in mm, we convert to µm for display
        /// </summary>
        private void UpdatePositions()
        {
            try
            {
                // Reset positions (stored in µm for linear axes, degrees for rotation)
                double x = 0, y = 0, z = 0, rx = 0, ry = 0, rz = 0;

                foreach (var wrapper in StageWrappers)
                {
                    if (!wrapper.IsConnected || wrapper.Controller == null)
                        continue;

                    var axes = wrapper.EnabledAxes;
                    foreach (var axis in axes)
                    {
                        // GetPosition returns mm from PI controller
                        double posMm = wrapper.GetAxisPosition(axis);
                        // Convert mm to µm (1 mm = 1000 µm) for linear axes
                        // Rotation axes stay in degrees
                        double posDisplay = axis switch
                        {
                            AxisType.X or AxisType.Y or AxisType.Z => posMm * 1000.0, // mm to µm
                            _ => posMm // degrees stay as degrees
                        };

                        switch (axis)
                        {
                            case AxisType.X: x = posDisplay; break;
                            case AxisType.Y: y = posDisplay; break;
                            case AxisType.Z: z = posDisplay; break;
                            case AxisType.Rx: rx = posDisplay; break;
                            case AxisType.Ry: ry = posDisplay; break;
                            case AxisType.Rz: rz = posDisplay; break;
                        }
                    }
                }

                // Update UI on main thread (Avalonia requires this)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CurrentPositionX = x;
                    CurrentPositionY = y;
                    CurrentPositionZ = z;
                    CurrentPositionRx = rx;
                    CurrentPositionRy = ry;
                    CurrentPositionRz = rz;

                    // Also update positions in MotionRows for calculated end position
                    foreach (var row in MotionRows)
                    {
                        row.CurrentPosition = row.Axis switch
                        {
                            AxisType.X => x,
                            AxisType.Y => y,
                            AxisType.Z => z,
                            AxisType.Rx => rx,
                            AxisType.Ry => ry,
                            AxisType.Rz => rz,
                            _ => 0
                        };
                    }

                    // Update calculated end position for relative mode display
                    OnPropertyChanged(nameof(CalculatedEndPosition));
                });
            }
            catch (Exception ex)
            {
                Log($"[UpdatePositions] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Force an immediate position update (call after moves)
        /// </summary>
        public void RefreshPositionsNow()
        {
            UpdatePositions();
        }

        /// <summary>
        /// Check connection status for all stages and update their status
        /// </summary>
        private void CheckAllConnections()
        {
            StageManager.CheckAllConnections();
            // Force UI to refresh after checking connections
            OnPropertyChanged(nameof(StageWrappers));
            // Update overall status
            OverallStageStatus = StageManager.OverallStatus;
        }

        private void Log(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                File.AppendAllText(_logFile, logMessage);
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

        // Convert AxisType to gRPC axis ID
        private string GetAxisId(AxisType axis)
        {
            return axis switch
            {
                AxisType.X => "1",
                AxisType.Y => "2",
                AxisType.Z => "3",
                AxisType.Rx => "4",
                AxisType.Ry => "5",
                AxisType.Rz => "6",
                _ => "1"
            };
        }

        /// <summary>
        /// Update motion rows based on connected stage axes
        /// </summary>
        private void UpdateAxis()
        {
            Log($"[UpdateAxis] Clearing motion rows, checking {StageWrappers.Count} wrappers...");
            MotionRows.Clear();

            foreach (var wrapper in StageWrappers)
            {
                Log($"[UpdateAxis] Checking Stage {wrapper.Id}: IsConnected={wrapper.IsConnected}, Controller={wrapper.Controller != null}");

                if (!wrapper.IsConnected || wrapper.Controller == null)
                {
                    continue;
                }

                // Map controller axes to AxisType
                var axes = wrapper.EnabledAxes;
                var availableAxes = wrapper.AvailableAxes;
                Log($"[UpdateAxis] Stage {wrapper.Id}: EnabledAxes={axes.Length}, AvailableAxes={availableAxes.Length}");
                Log($"[UpdateAxis] Stage {wrapper.Id}: Available axes: [{string.Join(", ", availableAxes)}]");

                for (int i = 0; i < axes.Length && i < availableAxes.Length; i++)
                {
                    var axis = axes[i];
                    var config = new MotionConfiguration
                    {
                        Axis = axis,
                        Label = $"{axis} ({availableAxes[i]})",
                        Unit = GetAxisUnit(axis),
                        IsEnabled = true,
                        Range = 1000,     // 1000 µm = 1 mm range
                        StepSize = 100,   // 100 µm per step (0.1 mm)
                        NumSteps = 10,    // 10 steps default
                        TargetPosition = 0  // Default target
                    };
                    MotionRows.Add(config);
                    Log($"[UpdateAxis] Added axis: {config.Label}, Unit={config.Unit}, StepSize={config.StepSize}");
                }
            }

            Log($"[UpdateAxis] Total motion rows: {MotionRows.Count}");
            CalculateTotals();
        }

        /// <summary>
        /// Get the unit for an axis type based on controller type
        /// PI Controller: nanometer (nm) for linear, microradian (µrad) for rotation
        /// SigmaKoki: micrometer (µm) for linear, degree (°) for rotation
        /// </summary>
        private string GetAxisUnit(AxisType axis)
        {
            // Determine controller type from selected stage wrapper
            bool isPIController = SelectedStageWrapper?.HardwareType == StageHardwareType.PI;
            bool isSigmaKoki = SelectedStageWrapper?.HardwareType == StageHardwareType.Sigmakoki;

            // Linear axes (X, Y, Z)
            if (axis == AxisType.X || axis == AxisType.Y || axis == AxisType.Z)
            {
                if (isPIController)
                    return "nm";  // nanometer for PI
                else if (isSigmaKoki)
                    return "µm";  // micrometer for SigmaKoki
                else
                    return "µm";  // default to micrometer
            }
            
            // Rotation axes (Rx, Ry, Rz)
            if (axis == AxisType.Rx || axis == AxisType.Ry || axis == AxisType.Rz)
            {
                if (isPIController)
                    return "µrad"; // microradian for PI
                else if (isSigmaKoki)
                    return "°";    // degree for SigmaKoki
                else
                    return "°";    // default to degree
            }

            return "";
        }

        // Get the first enabled axis from MotionRows
        private MotionConfiguration? GetFirstEnabledAxis()
        {
            return MotionRows.FirstOrDefault(r => r.IsEnabled);
        }

        private void InitializeMotionRows()
        {
            MotionRows.Clear();
            // Subscribe to wrapper property changes
            foreach (var wrapper in StageWrappers)
            {
                wrapper.PropertyChanged += OnWrapperPropertyChanged;
            }
        }

        /// <summary>
        /// Handle wrapper property changes - only update for IsConnected changes
        /// </summary>
        private void OnWrapperPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StageWrapper.IsConnected))
            {
                UpdateAxis();
            }
        }

        /// <summary>
        /// Cleanup event subscriptions
        /// </summary>
        public void Cleanup()
        {
            foreach (var wrapper in StageWrappers)
            {
                wrapper.PropertyChanged -= OnWrapperPropertyChanged;
            }
            StopConnectionPolling();
            StopPositionPolling();
        }

        [RelayCommand]
        private void UpdateStepSize(int index)
        {
            if (index < 0 || index >= MotionRows.Count) return;
            var row = MotionRows[index];
            if (row.NumSteps > 0)
            {
                row.Range = Math.Round(row.StepSize * row.NumSteps, 6);
            }
            CalculateTotals();
        }

        [RelayCommand]
        private void UpdateNumSteps(int index)
        {
            if (index < 0 || index >= MotionRows.Count) return;
            var row = MotionRows[index];
            row.Range = Math.Round(row.StepSize * row.NumSteps, 6);
            CalculateTotals();
        }

        [RelayCommand]
        private void UpdateRangeFromInputs(int index)
        {
            if (index < 0 || index >= MotionRows.Count) return;
            var row = MotionRows[index];
            row.Range = Math.Round(row.StepSize * row.NumSteps, 6);
            CalculateTotals();
        }

        [RelayCommand]
        private void ToggleAxisEnabled(int index)
        {
            if (index < 0 || index >= MotionRows.Count) return;
            MotionRows[index].IsEnabled = !MotionRows[index].IsEnabled;
            CalculateTotals();
        }

        [RelayCommand]
        private void UpdateTimedDuration()
        {
            if (TimedInterval > 0)
            {
                TimedPoints = (int)(TimedDuration / TimedInterval);
            }
            CalculateTotals();
        }

        [RelayCommand]
        private void UpdateTimedInterval()
        {
            if (TimedInterval > 0)
            {
                TimedDuration = TimedPoints * TimedInterval;
            }
            CalculateTotals();
        }

        private void CalculateTotals()
        {
            var enabledRows = MotionRows.Where(r => r.IsEnabled).ToList();

            if (enabledRows.Count > 0)
            {
                TotalPlannedPoints = enabledRows.Aggregate(1, (acc, r) => acc * r.NumSteps);
            }
            else
            {
                TotalPlannedPoints = 1;
            }

            EstimatedTime = TotalPlannedPoints * (SamplingMode == SamplingMode.Timed ? TimedInterval : 1);
        }

        [RelayCommand]
        private async Task StartSequence()
        {
            var enabledAxes = MotionRows.Where(r => r.IsEnabled).ToList();
            if (enabledAxes.Count == 0)
            {
                MovementStatus = "No axis enabled";
                Log("[StartSequence] No axis enabled in motion configuration");
                return;
            }

            IsSequenceRunning = true;
            int totalAxes = enabledAxes.Count;
            int currentAxis = 0;
            int globalStepNumber = 0; // Track step number across all axes

            try
            {
                Log($"========== StartSequence {(MovementModeAbsolute ? "ABSOLUTE" : "RELATIVE")} ==========");
                Log($"[StartSequence] Total axes: {totalAxes}");
                Log($"[StartSequence] Enabled axes: {string.Join(", ", enabledAxes.Select(a => $"{a.Label}(IsEnabled={a.IsEnabled})"))}");
                
                // Start pose estimation workers if enabled
                if (EnablePoseEstimationScan)
                {
                    Log($"[PoseEst] === POSE ESTIMATION SCAN ENABLED ===");
                    Log($"[PoseEst] Pause Duration: {StepPauseDurationMs}ms");
                    Log($"[PoseEst] Max Concurrent Threads: {MaxConcurrentPoseEstimations}");
                    Log($"[PoseEst] Capture Directory: {CaptureDirectory}");
                    
                    ClearGraphs();
                    StartPoseEstimationWorkers();
                    CapturedImageCount = 0;
                    ProcessedPoseCount = 0;
                    QueuedPoseCount = 0;
                }
                else
                {
                    Log($"[PoseEst] Pose estimation scan is DISABLED");
                }

                foreach (var axisConfig in enabledAxes)
                {
                    currentAxis++;
                    var axisId = GetAxisId(axisConfig.Axis);
                    var axisType = AxisIdToAxisType(axisId);
                    var wrapper = GetStageWrapper(axisId);

                    if (wrapper == null || !wrapper.IsConnected)
                    {
                        Log($"[StartSequence] No connected stage for {axisConfig.Label}");
                        continue;
                    }
                    
                    // Log stage details for debugging
                    Log($"[StartSequence] {axisConfig.Label} -> Stage {wrapper.Id}, Type: {wrapper.HardwareType}, Controller: {wrapper.Controller?.GetType().Name ?? "null"}");

                    // Get current position
                    double currentPosInMm = wrapper.GetAxisPosition(axisType);
                    double currentPosInUm = currentPosInMm * 1000.0;
                    // Sync UI model with live current position so AutoStepSize matches what UI shows.
                    axisConfig.CurrentPosition = currentPosInUm;

                    if (MovementModeAbsolute)
                    {
                        // ABSOLUTE MODE
                        double stepSizeInUm = axisConfig.AutoStepSize;
                        double stepAmount = GetStepAmountForController(wrapper, stepSizeInUm);
                        int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                        double totalMoveUm = stepSizeInUm * stepCount;

                        Log($"[StartSequence] Axis {currentAxis}/{totalAxes}: {axisConfig.Label} ABSOLUTE");
                        Log($"[StartSequence] StepSize(UI)={stepSizeInUm}, StepAmount(sent)={stepAmount}");
                        Log($"  Current: {currentPosInUm:F2} µm");
                        Log($"  StepSize: {stepSizeInUm:F2} µm, Steps: {stepCount}, Total: {totalMoveUm:F2} µm");
                        MovementStatus = $"{axisConfig.Label}: Moving {totalMoveUm:F0} µm...";

                        for (int step = 0; step < stepCount; step++)
                        {
                            if (!IsSequenceRunning)
                            {
                                Log($"[StartSequence] Aborted");
                                MovementStatus = "Sequence aborted";
                                return;
                            }

                            try
                            {
                                Log($"[StartSequence] About to call wrapper.MoveAxis({axisType}, {stepAmount:F4})");
                                await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));
                                Log($"[StartSequence] MoveAxis call completed successfully");
                            }
                            catch (Exception ex)
                            {
                                Log($"[StartSequence] EXCEPTION in MoveAxis: {ex.GetType().Name}: {ex.Message}");
                                throw;
                            }

                            // Update position display immediately
                            RefreshPositionsNow();

                            MovementStatus = $"{axisConfig.Label}: Step {step + 1}/{stepCount}";
                            Log($"  Step {step + 1}/{stepCount} +{stepSizeInUm:F2} µm");
                            
                            // Pose estimation capture if enabled
                            // Only capture if step size is non-zero (axis is actually moving)
                            if (EnablePoseEstimationScan && Math.Abs(stepSizeInUm) > 0.001)
                            {
                                globalStepNumber++;
                                Log($"[PoseEst] Step {globalStepNumber}: Starting capture sequence (pause={StepPauseDurationMs}ms)");
                                
                                if (StepPauseDurationMs > 0)
                                {
                                    await Task.Delay(StepPauseDurationMs);
                                    Log($"[PoseEst] Step {globalStepNumber}: Pause complete");
                                }
                                
                                // Update positions before capture
                                RefreshPositionsNow();
                                
                                // Capture image
                                Log($"[PoseEst] Step {globalStepNumber}: Capturing image...");
                                string? imagePath = await CaptureAndSaveImage(globalStepNumber);
                                
                                // Queue for pose estimation
                                if (imagePath != null)
                                {
                                    Log($"[PoseEst] Step {globalStepNumber}: Image captured, queuing for processing");
                                    QueuePoseEstimation(imagePath, globalStepNumber);
                                }
                                else
                                {
                                    Log($"[PoseEst] Step {globalStepNumber}: Image capture FAILED");
                                }
                            }
                            else if (EnablePoseEstimationScan)
                            {
                                Log($"[PoseEst] Skipping capture for {axisConfig.Label} step {step + 1} (stepSize={stepSizeInUm:F3} µm is too small)");
                            }
                            else
                            {
                                await Task.Delay(50);
                            }
                        }

                        double finalPosInMm = wrapper.GetAxisPosition(axisType);
                        double actualMoveUm = (finalPosInMm - currentPosInMm) * 1000.0;
                        Log($"  Done: now at {finalPosInMm*1000:F2} µm (moved {actualMoveUm:F2} µm)");
                    }
                    else
                    {
                        // RELATIVE MODE
                        double stepSizeInUm = axisConfig.StepSize;
                        double stepAmount = GetStepAmountForController(wrapper, stepSizeInUm);
                        int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                        double totalMoveUm = stepSizeInUm * stepCount;

                        Log($"[StartSequence] Axis {currentAxis}/{totalAxes}: {axisConfig.Label} RELATIVE");
                        Log($"[StartSequence] StepSize(UI)={stepSizeInUm}, StepAmount(sent)={stepAmount}");
                        Log($"  Current: {currentPosInUm:F2} µm");
                        Log($"  StepSize: {stepSizeInUm:F2} µm, Steps: {stepCount}, Total: {totalMoveUm:F2} µm");
                        MovementStatus = $"{axisConfig.Label}: Moving {totalMoveUm:F0} µm...";

                        for (int step = 0; step < stepCount; step++)
                        {
                            if (!IsSequenceRunning)
                            {
                                Log($"[StartSequence] Aborted");
                                MovementStatus = "Sequence aborted";
                                return;
                            }

                            await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));

                            // Update position display immediately
                            RefreshPositionsNow();

                            MovementStatus = $"{axisConfig.Label}: Step {step + 1}/{stepCount}";
                            Log($"  Step {step + 1}/{stepCount} +{stepSizeInUm:F2} µm");
                            
                            // Pose estimation capture if enabled
                            // Only capture if step size is non-zero (axis is actually moving)
                            if (EnablePoseEstimationScan && Math.Abs(stepSizeInUm) > 0.001)
                            {
                                globalStepNumber++;
                                Log($"[PoseEst] Step {globalStepNumber}: Starting capture sequence (pause={StepPauseDurationMs}ms)");
                                
                                if (StepPauseDurationMs > 0)
                                {
                                    await Task.Delay(StepPauseDurationMs);
                                    Log($"[PoseEst] Step {globalStepNumber}: Pause complete");
                                }
                                
                                // Update positions before capture
                                RefreshPositionsNow();
                                
                                // Capture image
                                Log($"[PoseEst] Step {globalStepNumber}: Capturing image...");
                                string? imagePath = await CaptureAndSaveImage(globalStepNumber);
                                
                                // Queue for pose estimation
                                if (imagePath != null)
                                {
                                    Log($"[PoseEst] Step {globalStepNumber}: Image captured, queuing for processing");
                                    QueuePoseEstimation(imagePath, globalStepNumber);
                                }
                                else
                                {
                                    Log($"[PoseEst] Step {globalStepNumber}: Image capture FAILED");
                                }
                            }
                            else if (EnablePoseEstimationScan)
                            {
                                Log($"[PoseEst] Skipping capture for {axisConfig.Label} step {step + 1} (stepSize={stepSizeInUm:F3} µm is too small)");
                            }
                            else
                            {
                                await Task.Delay(50);
                            }
                        }

                        double finalPosInMm = wrapper.GetAxisPosition(axisType);
                        double actualMoveUm = (finalPosInMm - currentPosInMm) * 1000.0;
                        Log($"  Done: now at {finalPosInMm*1000:F2} µm (moved {actualMoveUm:F2} µm)");
                    }
                }

                MovementStatus = $"Sequence completed ({totalAxes} axes)";
                
                // Don't wait for pose estimation - let it run in background
                if (EnablePoseEstimationScan)
                {
                    Log("[StartSequence] === POSE ESTIMATION RUNNING IN BACKGROUND ===");
                    Log($"[StartSequence] QueuedPoseCount={QueuedPoseCount}, ProcessedPoseCount={ProcessedPoseCount}, CapturedImageCount={CapturedImageCount}");
                    Log("[StartSequence] Stage movement is now free - pose estimation will continue in background");
                    
                    // Export and send results in background without blocking
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Wait for queue to finish in background
                            int waitCount = 0;
                            int maxWaitCount = 3000; // Max 5 minutes
                            
                            while (QueuedPoseCount > 0 && waitCount < maxWaitCount)
                            {
                                waitCount++;
                                if (waitCount % 50 == 0) // Log every 5 seconds
                                {
                                    Log($"[Background] Still processing... QueuedPoseCount={QueuedPoseCount}, ProcessedPoseCount={ProcessedPoseCount}");
                                }
                                
                                if (ProcessedPoseCount >= CapturedImageCount && CapturedImageCount > 0)
                                {
                                    Log($"[Background] All images processed ({ProcessedPoseCount}/{CapturedImageCount})");
                                    break;
                                }
                                
                                await Task.Delay(100);
                            }
                            
                            Log("[Background] Stopping pose estimation workers...");
                            await StopPoseEstimationWorkers();
                            Log($"[Background] Workers stopped. Pose estimation complete: {ProcessedPoseCount} processed");
                            
                            // Export results to CSV
                            Log($"[Background] Exporting {_poseResults.Count} results to CSV...");
                            await ExportPoseResults();
                            Log("[Background] CSV export completed");
                            
                            // Send results to Analysis tab
                            Log($"[Background] Sending {_poseResults.Count} results to Analysis tab...");
                            SendResultsToAnalysisTab();
                            Log("[Background] Results sent to Analysis tab");
                        }
                        catch (Exception ex)
                        {
                            Log($"[Background] Error: {ex.Message}");
                        }
                    });
                }
                
                Log($"[StartSequence] === COMPLETED (pose estimation continues in background) ===");
            }
            catch (Exception ex)
            {
                Log($"[StartSequence] Error: {ex.Message}");
                MovementStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsSequenceRunning = false;
            }
        }

        [RelayCommand]
        private void AbortSequence()
        {
            if (IsSequenceRunning)
            {
                Log("[AbortSequence] Aborting sequence...");
                MovementStatus = "Aborting...";
                IsSequenceRunning = false;
            }
        }

        [RelayCommand]
        private void SamplePoint()
        {
        }

        // Helper methods for StageManager integration (replaces gRPC)
        private AxisType AxisIdToAxisType(string axisId)
        {
            return axisId switch
            {
                "1" => AxisType.X,
                "2" => AxisType.Y,
                "3" => AxisType.Z,
                "4" => AxisType.Rx,
                "5" => AxisType.Ry,
                "6" => AxisType.Rz,
                _ => AxisType.X
            };
        }

        private StageWrapper? GetStageWrapper(string axisId)
        {
            var axisType = AxisIdToAxisType(axisId);
            return StageManager.GetWrapperForAxis(axisType);
        }

        private async Task MoveAxisAsync(string axisId, double distance)
        {
            var wrapper = GetStageWrapper(axisId);
            if (wrapper == null)
            {
                Log($"[MoveAxis] No wrapper found for axis {axisId}");
                throw new InvalidOperationException($"No wrapper found for axis {axisId}");
            }
            if (!wrapper.IsConnected)
            {
                Log($"[MoveAxis] Stage {wrapper.Id} is not connected for axis {axisId}");
                throw new InvalidOperationException($"Stage {wrapper.Id} is not connected for axis {axisId}");
            }

            var axisType = AxisIdToAxisType(axisId);
            Log($"[MoveAxis] Moving axis {axisId} ({axisType}) by {distance:F4} µm using Stage {wrapper.Id}");

            // Convert micrometers to millimeters for PI controller (1 mm = 1000 µm)
            double distanceInMm = distance / 1000.0;
            Log($"[MoveAxis] Converted to {distanceInMm:F6} mm for controller");

            await Task.Run(() => wrapper.MoveAxis(axisType, distanceInMm));
        }

        // Stage Movement Commands
        [RelayCommand]
        private async Task MoveSingle()
        {
            var axisConfig = GetFirstEnabledAxis();
            if (axisConfig == null)
            {
                MovementStatus = "No axis enabled";
                Log("[MoveSingle] No axis enabled in motion configuration");
                return;
            }

            try
            {
                var axisId = GetAxisId(axisConfig.Axis);
                MovementStatus = $"Moving {axisConfig.Label} (axis {axisId})...";
                Log($"[MoveSingle] Axis={axisConfig.Label}, StepSize={axisConfig.StepSize}, NumSteps={axisConfig.NumSteps}");

                // Use StageManager instead of gRPC
                double distance = axisConfig.StepSize * axisConfig.NumSteps;
                await MoveAxisAsync(axisId, distance);

                MovementStatus = $"Success: Moved {distance:F3}";
                Log($"[MoveSingle] Success: Moved axis {axisConfig.Label} by {distance:F3}");
            }
            catch (Exception ex)
            {
                MovementStatus = $"Error: {ex.Message}";
                Log($"[MoveSingle] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task MoveToTarget()
        {
            var axisConfig = GetFirstEnabledAxis();
            if (axisConfig == null)
            {
                MovementStatus = "No axis enabled";
                Log("[MoveToTarget] No axis enabled in motion configuration");
                return;
            }

            try
            {
                var axisId = GetAxisId(axisConfig.Axis);
                var axisType = AxisIdToAxisType(axisId);
                var wrapper = GetStageWrapper(axisId);

                if (wrapper == null || !wrapper.IsConnected)
                {
                    MovementStatus = $"Error: Stage not connected for {axisConfig.Label}";
                    Log($"[MoveToTarget] No connected stage for axis {axisConfig.Label}");
                    return;
                }

                // Get current position BEFORE any move
                double currentPosInMm = wrapper.GetAxisPosition(axisType);
                double currentPosInUm = currentPosInMm * 1000.0;

                if (MovementModeAbsolute)
                {
                    // Absolute mode uses UI StepSize/NumSteps directly (relative stepping)
                    double stepSizeInUm = axisConfig.AutoStepSize;
                    double stepAmount = GetStepAmountForController(wrapper, stepSizeInUm);
                    int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                    double totalMoveUm = stepSizeInUm * stepCount;

                    Log($"========== ABSOLUTE MOVE DEBUG ==========");
                    Log($"[MoveToTarget] Mode: ABSOLUTE");
                    Log($"[MoveToTarget] Axis: {axisConfig.Label} ({axisType})");
                    Log($"[MoveToTarget] StepSize(UI)={stepSizeInUm}, StepAmount(sent)={stepAmount}");
                    Log($"[MoveToTarget] Current Position: {currentPosInUm:F4} µm ({currentPosInMm:F6} mm)");
                    Log($"[MoveToTarget] Step Size: {stepSizeInUm:F4} µm");
                    Log($"[MoveToTarget] Step Count: {stepCount}");
                    Log($"[MoveToTarget] Total Move: {totalMoveUm:F4} µm");
                    Log($"==========================================");

                    MovementStatus = $"Moving by {stepSizeInUm:F3} µm × {stepCount} steps...";
                    
                    // Start pose estimation workers if enabled
                    if (EnablePoseEstimationScan)
                    {
                        Log($"[PoseEst] === POSE ESTIMATION SCAN ENABLED ===");
                        Log($"[PoseEst] Pause Duration: {StepPauseDurationMs}ms");
                        Log($"[PoseEst] Max Concurrent Threads: {MaxConcurrentPoseEstimations}");
                        Log($"[PoseEst] Capture Directory: {CaptureDirectory}");
                        
                        ClearGraphs();
                        StartPoseEstimationWorkers();
                        CapturedImageCount = 0;
                        ProcessedPoseCount = 0;
                        QueuedPoseCount = 0;
                    }
                    else
                    {
                        Log($"[PoseEst] Pose estimation scan is DISABLED");
                    }

                    for (int i = 0; i < stepCount; i++)
                    {
                        double posBeforeStepMm = wrapper.GetAxisPosition(axisType);
                        double posBeforeStepUm = posBeforeStepMm * 1000.0;

                        Log($"[MoveToTarget] Step {i + 1}/{stepCount}: At {posBeforeStepUm:F4} µm, moving by {stepSizeInUm:F4} µm");

                        await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));

                        double posAfterStepMm = wrapper.GetAxisPosition(axisType);
                        double posAfterStepUm = posAfterStepMm * 1000.0;
                        double actualMoveUm = posAfterStepUm - posBeforeStepUm;

                        Log($"[MoveToTarget] Step {i + 1} completed: Actually moved {actualMoveUm:F4} µm, now at {posAfterStepUm:F4} µm");
                        
                        // Pause, capture, and queue pose estimation if enabled
                        if (EnablePoseEstimationScan)
                        {
                            Log($"[PoseEst] Step {i + 1}: Starting capture sequence (pause={StepPauseDurationMs}ms)");
                            
                            if (StepPauseDurationMs > 0)
                            {
                                await Task.Delay(StepPauseDurationMs);
                                Log($"[PoseEst] Step {i + 1}: Pause complete");
                            }
                            
                            // Update positions before capture
                            RefreshPositionsNow();
                            
                            // Capture image
                            Log($"[PoseEst] Step {i + 1}: Capturing image...");
                            string? imagePath = await CaptureAndSaveImage(i + 1);
                            
                            // Queue for pose estimation
                            if (imagePath != null)
                            {
                                Log($"[PoseEst] Step {i + 1}: Image captured, queuing for processing");
                                QueuePoseEstimation(imagePath, i + 1);
                            }
                            else
                            {
                                Log($"[PoseEst] Step {i + 1}: Image capture FAILED");
                            }
                        }
                    }
                    
                    // Don't wait for pose estimation - let it continue in background
                    if (EnablePoseEstimationScan)
                    {
                        Log($"[MoveToTarget] Pose estimation running in background: {QueuedPoseCount} queued, {ProcessedPoseCount} processed");
                    }

                    double finalPosInMm = wrapper.GetAxisPosition(axisType);
                    double finalPosInUm = finalPosInMm * 1000.0;
                    double actualTotalMoveUm = finalPosInUm - currentPosInUm;

                    Log($"[MoveToTarget] FINAL: Position {finalPosInUm:F4} µm, Total moved: {actualTotalMoveUm:F4} µm");
                    MovementStatus = $"Success: Moved {actualTotalMoveUm:F3} µm (now {finalPosInUm:F3} µm)";

                    // Update calculated position display
                    OnPropertyChanged(nameof(CalculatedEndPosition));
                }
                else
                {
                    // Relative move - use StepSize and NumSteps from axis config
                    double distanceInUm = axisConfig.StepSize;  // StepSize is in µm
                    double distanceAmount = GetStepAmountForController(wrapper, distanceInUm);
                    int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                    double totalMoveUm = distanceInUm * stepCount;

                    Log($"========== RELATIVE MOVE DEBUG ==========");
                    Log($"[MoveToTarget] Mode: RELATIVE");
                    Log($"[MoveToTarget] Axis: {axisConfig.Label} ({axisType})");
                    Log($"[MoveToTarget] StepSize(UI)={distanceInUm}, StepAmount(sent)={distanceAmount}");
                    Log($"[MoveToTarget] Current Position: {currentPosInUm:F4} µm ({currentPosInMm:F6} mm)");
                    Log($"[MoveToTarget] Distance per step: {distanceInUm:F4} µm");
                    Log($"[MoveToTarget] Step Count: {stepCount}");
                    Log($"[MoveToTarget] Total Expected Move: {totalMoveUm:F4} µm");
                    Log($"==========================================");

                    MovementStatus = $"Moving by {distanceInUm:F3} µm × {stepCount} steps...";
                    
                    // Start pose estimation workers if enabled
                    if (EnablePoseEstimationScan)
                    {
                        ClearGraphs();
                        StartPoseEstimationWorkers();
                        CapturedImageCount = 0;
                        ProcessedPoseCount = 0;
                        QueuedPoseCount = 0;
                    }

                    for (int i = 0; i < stepCount; i++)
                    {
                        double posBeforeStepMm = wrapper.GetAxisPosition(axisType);
                        double posBeforeStepUm = posBeforeStepMm * 1000.0;

                        Log($"[MoveToTarget] Step {i + 1}/{stepCount}: At {posBeforeStepUm:F4} µm, moving by {distanceInUm:F4} µm");

                        await Task.Run(() => wrapper.MoveAxis(axisType, distanceAmount));

                        double posAfterStepMm = wrapper.GetAxisPosition(axisType);
                        double posAfterStepUm = posAfterStepMm * 1000.0;
                        double actualMoveUm = posAfterStepUm - posBeforeStepUm;

                        Log($"[MoveToTarget] Step {i + 1} completed: Actually moved {actualMoveUm:F4} µm, now at {posAfterStepUm:F4} µm");
                        
                        // Pause, capture, and queue pose estimation if enabled
                        if (EnablePoseEstimationScan)
                        {
                            Log($"[PoseEst] Step {i + 1}: Starting capture sequence (pause={StepPauseDurationMs}ms)");
                            
                            if (StepPauseDurationMs > 0)
                            {
                                await Task.Delay(StepPauseDurationMs);
                                Log($"[PoseEst] Step {i + 1}: Pause complete");
                            }
                            
                            // Update positions before capture
                            RefreshPositionsNow();
                            
                            // Capture image
                            Log($"[PoseEst] Step {i + 1}: Capturing image...");
                            string? imagePath = await CaptureAndSaveImage(i + 1);
                            
                            // Queue for pose estimation
                            if (imagePath != null)
                            {
                                Log($"[PoseEst] Step {i + 1}: Image captured, queuing for processing");
                                QueuePoseEstimation(imagePath, i + 1);
                            }
                            else
                            {
                                Log($"[PoseEst] Step {i + 1}: Image capture FAILED");
                            }
                        }
                        else
                        {
                            await Task.Delay(50);
                        }
                    }
                    
                    // Don't wait for pose estimation - let it continue in background
                    if (EnablePoseEstimationScan)
                    {
                        Log($"[MoveToTarget] Pose estimation running in background: {QueuedPoseCount} queued, {ProcessedPoseCount} processed");
                    }

                    double finalPosInMm = wrapper.GetAxisPosition(axisType);
                    double finalPosInUm = finalPosInMm * 1000.0;
                    double actualTotalMoveUm = finalPosInUm - currentPosInUm;

                    Log($"[MoveToTarget] FINAL: Position {finalPosInUm:F4} µm, Total moved: {actualTotalMoveUm:F4} µm");
                    MovementStatus = $"Success: At {finalPosInUm:F3} µm (moved {actualTotalMoveUm:F3} µm)";

                    // Update calculated position display
                    OnPropertyChanged(nameof(CalculatedEndPosition));
                }
            }
            catch (Exception ex)
            {
                MovementStatus = $"Error: {ex.Message}";
                Log($"[MoveToTarget] Error: {ex.Message}");
            }
        }

        private static double GetStepAmountForController(StageWrapper wrapper, double stepSizeUm)
        {
            // SigmaKoki expects raw steps from UI; other controllers use mm
            if (wrapper.Controller is SigmakokiController)
            {
                return stepSizeUm;
            }
            return stepSizeUm / 1000.0;
        }

        [RelayCommand]
        private async Task StopMovement()
        {
            try
            {
                MovementStatus = "Stopping...";
                Log("[StopMovement] Stopping all movement");

                // Use StageManager instead of gRPC
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

        // ==================== STAGE CONTROLLER MANAGEMENT ====================

        [RelayCommand]
        private void AddStage()
        {
            var newWrapper = StageManager.AddStage();
            Log($"[AddStage] Added Stage {newWrapper.Id}");
        }

        [RelayCommand]
        private void RemoveStage(StageWrapper? wrapper)
        {
            if (wrapper == null) return;
            StageManager.RemoveStage(wrapper);
            Log($"[RemoveStage] Removed Stage {wrapper.Id}");
        }

        [RelayCommand]
        private async Task ConnectStage(StageWrapper? wrapper)
        {
            if (wrapper == null)
            {
                Log("[ConnectStage] Stage is null");
                return;
            }

            Log($"[ConnectStage] Stage {wrapper.Id}: Attempting to connect (Hardware: {wrapper.HardwareType})...");

            try
            {
                bool success = await wrapper.ConnectAsync();
                if (success)
                {
                    StageManager.RebuildAxisToWrapperMap();
                    Log($"[ConnectStage] Stage {wrapper.Id}: Connected successfully ({wrapper.AvailableAxes.Length} axes)");

                    // Update the motion rows with the connected axes
                    UpdateAxis();
                    Log($"[ConnectStage] Updated motion configuration with {MotionRows.Count} axes");
                }
                else
                {
                    Log($"[ConnectStage] Stage {wrapper.Id}: Failed - {wrapper.ConnectionStatus}");
                }
            }
            catch (Exception ex)
            {
                Log($"[ConnectStage] Stage {wrapper.Id}: Exception - {ex.Message}");
            }
        }

        [RelayCommand]
        private void DisconnectStage(StageWrapper? wrapper)
        {
            if (wrapper == null) return;
            wrapper.Disconnect();
            StageManager.RebuildAxisToWrapperMap();
            Log($"[DisconnectStage] Stage {wrapper.Id}: Disconnected");
        }

        [RelayCommand]
        private void DisconnectAllStages()
        {
            StageManager.DisconnectAll();
        }

        [RelayCommand]
        private async Task ReferenceAxes(StageWrapper? wrapper)
        {
            if (wrapper == null) return;

            if (!wrapper.IsConnected)
            {
                Log($"[ReferenceAxes] Stage {wrapper.Id} is not connected");
                return;
            }

            try
            {
                Log($"[ReferenceAxes] Stage {wrapper.Id}: Starting axis referencing...");
                MovementStatus = "Referencing axes...";

                await Task.Run(() => wrapper.ReferenceAxes());

                bool referenced = wrapper.AreAxesReferenced();
                Log($"[ReferenceAxes] Stage {wrapper.Id}: Referencing complete. All referenced: {referenced}");
                MovementStatus = referenced ? "Axes referenced" : "Referencing completed (check status)";
            }
            catch (Exception ex)
            {
                Log($"[ReferenceAxes] Stage {wrapper.Id}: Error - {ex.Message}");
                MovementStatus = $"Reference error: {ex.Message}";
            }
        }

        /// <summary>
        /// Update overall stage status display
        /// </summary>
        private void UpdateOverallStageStatus()
        {
            OverallStageStatus = StageManager.OverallStatus;
        }

        partial void OnSelectedStageHardwareChanged(StageHardwareType value)
        {
            Log($"[StageController] Hardware type changed to: {StageControllerFactory.GetDisplayName(value)}");

            // Disconnect if currently connected
            if (IsStageConnected && CurrentStageController != null)
            {
                try
                {
                    CurrentStageController.Disconnect();
                    Log("[StageController] Disconnected previous controller");
                }
                catch (Exception ex)
                {
                    Log($"[StageController] Error disconnecting: {ex.Message}");
                }
            }

            IsStageConnected = false;
            CurrentStageController = null;
            AvailableAxes = Array.Empty<string>();
            ConnectionStatus = "Not connected";

            // Create new controller instance
            var controller = StageControllerFactory.CreateController(value, SelectedSigmakokiController);
            if (controller != null)
            {
                CurrentStageController = controller;
                ConnectionStatus = $"Ready to connect: {StageControllerFactory.GetDisplayName(value)}";
                Log($"[StageController] Created controller: {StageControllerFactory.GetDisplayName(value)}");
            }
        }

        partial void OnSelectedSigmakokiControllerChanged(SigmakokiControllerType value)
        {
            // Only recreate controller if Sigmakoki is selected
            if (SelectedStageHardware == StageHardwareType.Sigmakoki)
            {
                Log($"[StageController] Sigma Koki controller type changed to: {value.GetDisplayName()}");

                // Disconnect if currently connected
                if (IsStageConnected && CurrentStageController != null)
                {
                    try
                    {
                        CurrentStageController.Disconnect();
                        Log("[StageController] Disconnected previous controller");
                    }
                    catch (Exception ex)
                    {
                        Log($"[StageController] Error disconnecting: {ex.Message}");
                    }
                }

                IsStageConnected = false;
                CurrentStageController = null;
                AvailableAxes = Array.Empty<string>();

                // Create new controller instance with selected type
                var controller = StageControllerFactory.CreateController(StageHardwareType.Sigmakoki, value);
                if (controller != null)
                {
                    CurrentStageController = controller;
                    ConnectionStatus = $"Ready to connect: {value.GetDisplayName()}";
                    Log($"[StageController] Created controller: {value.GetDisplayName()}");
                }
            }
        }

        [RelayCommand]
        private async Task ConnectStageLegacy()
        {
            if (CurrentStageController == null)
            {
                ConnectionStatus = "No controller selected";
                Log("[ConnectStage] No controller selected");
                return;
            }

            try
            {
                ConnectionStatus = "Connecting...";
                Log("[ConnectStage] Attempting to connect...");

                await Task.Run(() => CurrentStageController.Connect());

                IsStageConnected = CurrentStageController.CheckConnected();
                if (IsStageConnected)
                {
                    AvailableAxes = CurrentStageController.GetAxesList();
                    ConnectionStatus = $"Connected ({AvailableAxes.Length} axes: {string.Join(", ", AvailableAxes)})";
                    Log($"[ConnectStage] Connected successfully. Axes: {string.Join(", ", AvailableAxes)}");
                }
                else
                {
                    ConnectionStatus = "Connection failed";
                    Log("[ConnectStage] Connection check failed");
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Error: {ex.Message}";
                Log($"[ConnectStage] Error: {ex.Message}");
                IsStageConnected = false;
            }
        }

        [RelayCommand]
        private void DisconnectStageLegacy()
        {
            if (CurrentStageController == null || !IsStageConnected)
            {
                ConnectionStatus = "Not connected";
                return;
            }

            try
            {
                CurrentStageController.Disconnect();
                IsStageConnected = false;
                AvailableAxes = Array.Empty<string>();
                ConnectionStatus = "Disconnected";
                Log("[DisconnectStage] Disconnected successfully");
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Error: {ex.Message}";
                Log($"[DisconnectStage] Error: {ex.Message}");
            }
        }

        // ==================== LOCAL STAGE MOVEMENT ====================

        /// <summary>
        /// Move a single axis using the connected stage wrapper with configured step size
        /// </summary>
        [RelayCommand]
        private async Task MoveAxisLocal(MotionConfiguration? axisConfig)
        {
            if (axisConfig == null)
            {
                Log("[MoveAxisLocal] Axis configuration is null");
                return;
            }

            if (!axisConfig.IsEnabled)
            {
                Log($"[MoveAxisLocal] Axis {axisConfig.Label} is not enabled");
                return;
            }

            // Get the wrapper that controls this axis
            var wrapper = StageManager.GetWrapperForAxis(axisConfig.Axis);
            if (wrapper == null || !wrapper.IsConnected)
            {
                MovementStatus = $"No connected stage for {axisConfig.Label}";
                Log($"[MoveAxisLocal] No connected stage for {axisConfig.Label}");
                return;
            }

            try
            {
                // Get current position
                double currentPosInMm = wrapper.GetAxisPosition(axisConfig.Axis);
                double currentPosInUm = currentPosInMm * 1000.0;

                if (MovementModeAbsolute)
                {
                    // ABSOLUTE MODE
                    double stepSizeInUm = axisConfig.AutoStepSize;
                    Log($"[Step Size in UM = {stepSizeInUm}]");
                    double stepAmount = GetStepAmountForController(wrapper, stepSizeInUm);
                    int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                    double totalMoveUm = stepSizeInUm * stepCount;

                    Log($"========== MoveAxisLocal ABSOLUTE ==========");
                    Log($"[MoveAxisLocal] Axis: {axisConfig.Label}");
                    Log($"[MoveAxisLocal] StepAmount(sent): {stepAmount}");
                    Log($"[MoveAxisLocal] axisConfig.TargetPosition: {axisConfig.TargetPosition} µm");
                    Log($"[MoveAxisLocal] axisConfig.NumSteps: {axisConfig.NumSteps}");
                    Log($"[MoveAxisLocal] Current Position: {currentPosInUm:F4} µm ({currentPosInMm:F6} mm)");
                    Log($"[MoveAxisLocal] Step Size: {stepSizeInUm:F4} µm");
                    Log($"[MoveAxisLocal] Step Count: {stepCount}");
                    Log($"[MoveAxisLocal] Total Move: {totalMoveUm:F4} µm");
                    Log($"=============================================");

                    MovementStatus = $"Moving {axisConfig.Label} by {totalMoveUm:F1} µm...";

                    for (int step = 0; step < stepCount; step++)
                    {
                        Log($"[MoveAxisLocal] Step {step + 1}/{stepCount}: Moving by {stepSizeInUm:F4} µm");

                        double posBeforeMm = wrapper.GetAxisPosition(axisConfig.Axis);
                        await Task.Run(() => wrapper.MoveAxis(axisConfig.Axis, stepAmount));
                        double posAfterMm = wrapper.GetAxisPosition(axisConfig.Axis);
                        double actualMoveUm = (posAfterMm - posBeforeMm) * 1000.0;

                        // Update position display immediately
                        RefreshPositionsNow();

                        Log($"[MoveAxisLocal] Step {step + 1} done: moved {actualMoveUm:F4} µm, now at {posAfterMm*1000:F4} µm");

                        MovementStatus = $"{axisConfig.Label}: Step {step + 1}/{stepCount}";
                        await Task.Delay(50);
                    }

                    double finalPosInMm = wrapper.GetAxisPosition(axisConfig.Axis);
                    double finalPosInUm = finalPosInMm * 1000.0;
                    Log($"[MoveAxisLocal] FINAL: {finalPosInUm:F4} µm");
                    MovementStatus = $"Done: {axisConfig.Label} at {finalPosInUm:F1} µm";
                }
                else
                {
                    // RELATIVE MODE
                    double stepSizeInUm = axisConfig.StepSize;
                    double stepAmount = GetStepAmountForController(wrapper, stepSizeInUm);
                    int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                    double totalMoveUm = stepSizeInUm * stepCount;

                    Log($"========== MoveAxisLocal RELATIVE ==========");
                    Log($"[MoveAxisLocal] Axis: {axisConfig.Label}");
                    Log($"[MoveAxisLocal] StepAmount(sent): {stepAmount}");
                    Log($"[MoveAxisLocal] axisConfig.StepSize: {axisConfig.StepSize} µm");
                    Log($"[MoveAxisLocal] axisConfig.NumSteps: {axisConfig.NumSteps}");
                    Log($"[MoveAxisLocal] Current Position: {currentPosInUm:F4} µm ({currentPosInMm:F6} mm)");
                    Log($"[MoveAxisLocal] Step Size: {stepSizeInUm:F4} µm");
                    Log($"[MoveAxisLocal] Step Count: {stepCount}");
                    Log($"[MoveAxisLocal] Total Move: {totalMoveUm:F4} µm");
                    Log($"=============================================");

                    MovementStatus = $"Moving {axisConfig.Label} by {totalMoveUm:F1} µm...";

                    for (int step = 0; step < stepCount; step++)
                    {
                        double posBeforeMm = wrapper.GetAxisPosition(axisConfig.Axis);
                        double posBeforeUm = posBeforeMm * 1000.0;

                        Log($"[MoveAxisLocal] Step {step + 1}/{stepCount}: At {posBeforeUm:F4} µm, moving by {stepSizeInUm:F4} µm");

                        await Task.Run(() => wrapper.MoveAxis(axisConfig.Axis, stepAmount));

                        double posAfterMm = wrapper.GetAxisPosition(axisConfig.Axis);
                        double posAfterUm = posAfterMm * 1000.0;
                        double actualMoveUm = posAfterUm - posBeforeUm;

                        // Update position display immediately
                        RefreshPositionsNow();

                        Log($"[MoveAxisLocal] Step {step + 1} done: actually moved {actualMoveUm:F4} µm, now at {posAfterUm:F4} µm");

                        MovementStatus = $"{axisConfig.Label}: Step {step + 1}/{stepCount}";
                        await Task.Delay(50);
                    }

                    double finalPosInMm = wrapper.GetAxisPosition(axisConfig.Axis);
                    double finalPosInUm = finalPosInMm * 1000.0;
                    double actualTotalUm = finalPosInUm - currentPosInUm;
                    Log($"[MoveAxisLocal] FINAL: {finalPosInUm:F4} µm (moved {actualTotalUm:F4} µm)");
                    MovementStatus = $"Done: {axisConfig.Label} moved {actualTotalUm:F1} µm";
                }

                // Update calculated position display
                OnPropertyChanged(nameof(CalculatedEndPosition));
            }
            catch (Exception ex)
            {
                MovementStatus = $"Error: {ex.Message}";
                Log($"[MoveAxisLocal] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Move all enabled axes in sequence using the connected stage wrappers
        /// </summary>
        [RelayCommand]
        private async Task MoveAllAxesLocal()
        {
            var enabledAxes = MotionRows.Where(r => r.IsEnabled).ToList();
            if (enabledAxes.Count == 0)
            {
                MovementStatus = "No axis enabled";
                Log("[MoveAllAxesLocal] No axis enabled");
                return;
            }

            // Check if any stage is connected
            if (!StageManager.HasConnectedStages)
            {
                MovementStatus = "No stages connected";
                Log("[MoveAllAxesLocal] No stages connected");
                return;
            }

            IsSequenceRunning = true;
            int totalAxes = enabledAxes.Count;
            int currentAxis = 0;

            try
            {
                Log($"========== MoveAllAxesLocal START ==========");
                Log($"[MoveAllAxesLocal] Mode: {(MovementModeAbsolute ? "ABSOLUTE" : "RELATIVE")}");
                Log($"[MoveAllAxesLocal] Total axes: {totalAxes}");
                Log($"=============================================");

                MovementStatus = $"Starting sequence ({totalAxes} axes)...";

                foreach (var axisConfig in enabledAxes)
                {
                    currentAxis++;

                    // Get the wrapper that controls this axis
                    var wrapper = StageManager.GetWrapperForAxis(axisConfig.Axis);
                    if (wrapper == null || !wrapper.IsConnected)
                    {
                        Log($"[MoveAllAxesLocal] No connected stage for {axisConfig.Label}");
                        continue;
                    }

                    // Get current position
                    double currentPosInMm = wrapper.GetAxisPosition(axisConfig.Axis);
                    double currentPosInUm = currentPosInMm * 1000.0;

                    if (MovementModeAbsolute)
                    {
                        // ABSOLUTE MODE
                        double stepSizeInUm = axisConfig.AutoStepSize;
                        double stepAmount = GetStepAmountForController(wrapper, stepSizeInUm);
                        int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                        double totalMoveUm = stepSizeInUm * stepCount;

                        Log($"[MoveAllAxesLocal] Axis {currentAxis}/{totalAxes}: {axisConfig.Label} ABSOLUTE");
                        Log($"  Current: {currentPosInUm:F4} µm");
                        Log($"  StepSize: {stepSizeInUm:F4} µm, Steps: {stepCount}, Total: {totalMoveUm:F4} µm");

                        for (int step = 0; step < stepCount; step++)
                        {
                            if (!IsSequenceRunning)
                            {
                                Log($"[MoveAllAxesLocal] Aborted");
                                MovementStatus = "Sequence aborted";
                                return;
                            }

                            await Task.Run(() => wrapper.MoveAxis(axisConfig.Axis, stepAmount));

                            // Update position display immediately
                            RefreshPositionsNow();

                            MovementStatus = $"Axis {currentAxis}/{totalAxes}: {axisConfig.Label} step {step + 1}/{stepCount}";
                            await Task.Delay(50);
                        }

                        double finalPosInMm = wrapper.GetAxisPosition(axisConfig.Axis);
                        double actualMoveUm = (finalPosInMm - currentPosInMm) * 1000.0;
                        Log($"  Done: now at {finalPosInMm*1000:F4} µm (moved {actualMoveUm:F4} µm)");
                    }
                    else
                    {
                        // RELATIVE MODE
                        double stepSizeInUm = axisConfig.StepSize;
                        double stepAmount = GetStepAmountForController(wrapper, stepSizeInUm);
                        int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                        double totalMoveUm = stepSizeInUm * stepCount;

                        Log($"[MoveAllAxesLocal] Axis {currentAxis}/{totalAxes}: {axisConfig.Label} RELATIVE");
                        Log($"  Current: {currentPosInUm:F4} µm");
                        Log($"  StepSize: {stepSizeInUm:F4} µm, Steps: {stepCount}, Total: {totalMoveUm:F4} µm");

                        for (int step = 0; step < stepCount; step++)
                        {
                            if (!IsSequenceRunning)
                            {
                                Log($"[MoveAllAxesLocal] Aborted");
                                MovementStatus = "Sequence aborted";
                                return;
                            }

                            await Task.Run(() => wrapper.MoveAxis(axisConfig.Axis, stepAmount));

                            // Update position display immediately
                            RefreshPositionsNow();

                            MovementStatus = $"Axis {currentAxis}/{totalAxes}: {axisConfig.Label} step {step + 1}/{stepCount}";
                            await Task.Delay(50);
                        }

                        double finalPosInMm = wrapper.GetAxisPosition(axisConfig.Axis);
                        double actualMoveUm = (finalPosInMm - currentPosInMm) * 1000.0;
                        Log($"  Done: now at {finalPosInMm*1000:F4} µm (moved {actualMoveUm:F4} µm)");
                    }
                }

                MovementStatus = "Sequence completed";
                Log("[MoveAllAxesLocal] Sequence completed successfully");
                OnPropertyChanged(nameof(CalculatedEndPosition));
            }
            catch (Exception ex)
            {
                MovementStatus = $"Error: {ex.Message}";
                Log($"[MoveAllAxesLocal] Error: {ex.Message}");
            }
            finally
            {
                IsSequenceRunning = false;
            }
        }

        /// <summary>
        /// Get the controller axis index for a given AxisType
        /// </summary>
        private int GetAxisIndex(AxisType axis)
        {
            // Map AxisType to 0-based index for the controller
            return axis switch
            {
                AxisType.X => 0,
                AxisType.Y => 1,
                AxisType.Z => 2,
                AxisType.Rx => 3,
                AxisType.Ry => 4,
                AxisType.Rz => 5,
                _ => 0
            };
        }

        // ==================== POSE ESTIMATION SCAN METHODS ====================

        /// <summary>
        /// Capture image from camera and save to disk
        /// </summary>
        private async Task<string?> CaptureAndSaveImage(int stepNumber)
        {
            try
            {
                // Get camera service from App
                var cameraService = App.CameraService;
                
                if (cameraService == null || !cameraService.IsConnected)
                {
                    Log("[CaptureImage] Camera service not available or not connected");
                    return null;
                }

                // Get latest frame from camera
                var (buffer, width, height, seq) = cameraService.GetLatestFrameSnapshot();
                
                if (buffer == null || buffer.Length == 0)
                {
                    Log("[CaptureImage] No frame available from camera");
                    return null;
                }

                // Create timestamped filename
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string filename = $"step_{stepNumber:D5}_{timestamp}.jpg";
                string filepath = Path.Combine(CaptureDirectory, filename);

                // Convert buffer to OpenCV Mat and save as JPEG
                await Task.Run(() =>
                {
                    using var mat = new Mat(height, width, MatType.CV_8UC1, buffer);
                    Cv2.ImWrite(filepath, mat, new ImageEncodingParam(ImwriteFlags.JpegQuality, 95));
                });

                CapturedImageCount++;
                Log($"[CaptureImage] Saved: {filename}");
                
                return filepath;
            }
            catch (Exception ex)
            {
                Log($"[CaptureImage] Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Queue a pose estimation task
        /// </summary>
        private void QueuePoseEstimation(string imagePath, int stepNumber)
        {
            // Capture configuration parameters from ConfigViewModel at queue time
            var configVM = ConfigViewModel.Instance;
            
            var task = new PoseEstimationTask
            {
                ImagePath = imagePath,
                PositionX = CurrentPositionX,
                PositionY = CurrentPositionY,
                PositionZ = CurrentPositionZ,
                PositionRx = CurrentPositionRx,
                PositionRy = CurrentPositionRy,
                PositionRz = CurrentPositionRz,
                Timestamp = DateTime.Now,
                StepNumber = stepNumber,
                // Capture current configuration parameters
                Fx = configVM?.Fx ?? 6363.64,
                Fy = configVM?.Fy ?? 6363.64,
                Cx = configVM?.Cx ?? 0.0,
                Cy = configVM?.Cy ?? 0.0,
                PixelSize = configVM?.PixelSize ?? 4.4e-3,
                FocalLength = configVM?.FocalLength ?? 28.0,
                PitchX = configVM?.PitchX ?? 0.1,
                PitchY = configVM?.PitchY ?? 0.1,
                ComponentsX = configVM?.ComponentsX ?? 11.0,
                ComponentsY = configVM?.ComponentsY ?? 11.0,
                WindowSize = configVM?.WindowSize ?? 12.0,
                CodePitchBlocks = configVM?.CodePitchBlocks ?? 6.0,
                // Capture HMF file paths
                UseCustomHmfPaths = configVM?.UseCustomHmfPaths ?? false,
                HmfGPath = configVM?.HmfGPath ?? "",
                HmfXPath = configVM?.HmfXPath ?? "",
                HmfYPath = configVM?.HmfYPath ?? ""
            };

            _poseEstQueue.Enqueue(task);
            QueuedPoseCount = _poseEstQueue.Count;
            Log($"[PoseEst] Queued task for step {stepNumber} with params: Fx={task.Fx:F2}, Fy={task.Fy:F2}, PitchX={task.PitchX:F3}, PitchY={task.PitchY:F3}, UseCustomHmf={task.UseCustomHmfPaths}, queue size: {QueuedPoseCount}");
        }

        /// <summary>
        /// Start pose estimation worker threads
        /// </summary>
        private void StartPoseEstimationWorkers()
        {
            _poseEstCts = new CancellationTokenSource();
            
            // Start worker tasks based on max concurrent setting
            for (int i = 0; i < MaxConcurrentPoseEstimations; i++)
            {
                int workerId = i;
                Task.Run(() => PoseEstimationWorker(workerId, _poseEstCts.Token), _poseEstCts.Token);
            }
            
            Log($"[PoseEst] Started {MaxConcurrentPoseEstimations} worker threads");
        }

        /// <summary>
        /// Stop pose estimation workers
        /// </summary>
        private async Task StopPoseEstimationWorkers()
        {
            if (_poseEstCts != null)
            {
                _poseEstCts.Cancel();
                await Task.Delay(500); // Give workers time to finish current tasks
                _poseEstCts.Dispose();
                _poseEstCts = null;
            }
            
            Log("[PoseEst] Stopped all worker threads");
        }

        /// <summary>
        /// Pose estimation worker thread
        /// </summary>
        private async Task PoseEstimationWorker(int workerId, CancellationToken ct)
        {
            Log($"[PoseEst] Worker {workerId} started");
            
            while (!ct.IsCancellationRequested)
            {
                if (_poseEstQueue.TryDequeue(out var task))
                {
                    QueuedPoseCount = _poseEstQueue.Count;
                    
                    try
                    {
                        Log($"[PoseEst] Worker {workerId} processing step {task.StepNumber}");
                        Log($"[PoseEst] Worker {workerId} calling ProcessPoseEstimation...");
                        
                        var result = await ProcessPoseEstimation(task);
                        
                        Log($"[PoseEst] Worker {workerId} ProcessPoseEstimation returned, adding to results...");
                        
                        lock (_poseResultsLock)
                        {
                            _poseResults.Add(result);
                            Log($"[PoseEst] Worker {workerId} added result, total results: {_poseResults.Count}");
                        }
                        
                        ProcessedPoseCount++;
                        Log($"[PoseEst] Worker {workerId} updated ProcessedPoseCount to {ProcessedPoseCount}");
                        
                        // Update graphs in real-time
                        Log($"[PoseEst] Worker {workerId} updating graphs...");
                        UpdateGraphsWithResult(result);
                        Log($"[PoseEst] Worker {workerId} graphs updated");
                        
                        Log($"[PoseEst] Worker {workerId} completed step {task.StepNumber}, success: {result.Success}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[PoseEst] Worker {workerId} EXCEPTION on step {task.StepNumber}: {ex.GetType().Name}: {ex.Message}");
                        Log($"[PoseEst] Worker {workerId} Stack trace: {ex.StackTrace}");
                    }
                }
                else
                {
                    // Queue is empty, wait a bit before checking again
                    await Task.Delay(50, ct);
                }
            }
            
            Log($"[PoseEst] Worker {workerId} stopped");
        }

        /// <summary>
        /// Process a single pose estimation task
        /// </summary>
        private async Task<PoseEstimationResult> ProcessPoseEstimation(PoseEstimationTask task)
        {
            Log($"[PoseEst] ProcessPoseEstimation ENTER for step {task.StepNumber}");
            
            var result = new PoseEstimationResult
            {
                ImagePath = task.ImagePath,
                StageX = task.PositionX,
                StageY = task.PositionY,
                StageZ = task.PositionZ,
                StageRx = task.PositionRx,
                StageRy = task.PositionRy,
                StageRz = task.PositionRz,
                Timestamp = task.Timestamp,
                StepNumber = task.StepNumber,
                Success = false
            };
            
            Log($"[PoseEst] Step {task.StepNumber}: Result object created");

            try
            {
                Log($"[PoseEst] Step {task.StepNumber}: Using parameters captured at queue time");
                
                // Use parameters that were captured when the task was queued
                // This ensures that parameter changes in ConfigView are reflected
                // in the pose estimation for each step
                
                // Create camera intrinsics from task parameters
                var intrinsics = new libs.CameraIntrinsics
                {
                    Fx = task.Fx,
                    Fy = task.Fy,
                    Cx = task.Cx,
                    Cy = task.Cy
                };
                
                // Log all parameters being used
                Log($"[PoseEst] Parameters: Fx={task.Fx:F2}, Fy={task.Fy:F2}, Cx={task.Cx:F2}, Cy={task.Cy:F2}");
                Log($"[PoseEst] Parameters: PixelSize={task.PixelSize:F6}mm, FocalLength={task.FocalLength:F2}mm");
                Log($"[PoseEst] Parameters: PitchX={task.PitchX:F3}mm, PitchY={task.PitchY:F3}mm");
                Log($"[PoseEst] Parameters: ComponentsX={task.ComponentsX}, ComponentsY={task.ComponentsY}");
                Log($"[PoseEst] Parameters: WindowSize={task.WindowSize:F1}, CodePitchBlocks={task.CodePitchBlocks}");
                Log($"[PoseEst] Image: {task.ImagePath}");
                
                // Call the actual pose estimation API with timeout protection
                // Use semaphore to ensure only ONE DLL call at a time (DLL may not be thread-safe)
                Log($"[PoseEst] Step {task.StepNumber}: Waiting for DLL semaphore...");
                await _poseEstDllSemaphore.WaitAsync();
                
                try
                {
                    Log($"[PoseEst] Step {task.StepNumber}: Calling native DLL...");
                    
                    // Choose API based on whether custom HMF paths are provided
                    if (task.UseCustomHmfPaths && 
                        !string.IsNullOrWhiteSpace(task.HmfGPath) && 
                        !string.IsNullOrWhiteSpace(task.HmfXPath) && 
                        !string.IsNullOrWhiteSpace(task.HmfYPath))
                    {
                        Log($"[PoseEst] Step {task.StepNumber}: Using custom HMF paths:");
                        Log($"[PoseEst]   G: {task.HmfGPath}");
                        Log($"[PoseEst]   X: {task.HmfXPath}");
                        Log($"[PoseEst]   Y: {task.HmfYPath}");
                        
                        var poseTask = Task.Run(() => PoseEstApiBridgeClient.EstimatePoseFromImagePathWithHmfFiles(
                            imagePath: task.ImagePath,
                            intrinsicMatrix: intrinsics.ToArray(),
                            pixelSize: task.PixelSize,
                            focalLength: task.FocalLength,
                            pitchX: task.PitchX,
                            pitchY: task.PitchY,
                            componentsX: task.ComponentsX,
                            componentsY: task.ComponentsY,
                            windowSize: task.WindowSize,
                            codePitchBlocks: task.CodePitchBlocks,
                            hmfGPath: task.HmfGPath,
                            hmfXPath: task.HmfXPath,
                            hmfYPath: task.HmfYPath
                        ));
                        
                        // Wait with timeout (120 seconds)
                        if (await Task.WhenAny(poseTask, Task.Delay(120000)) == poseTask)
                        {
                            var poseResult = await poseTask;
                            Log($"[PoseEst] Step {task.StepNumber}: Native DLL returned status={poseResult.Status}");
                            
                            if (poseResult.Status == PoseStatus.Success)
                            {
                                result.EstimatedX = poseResult.Tx;
                                result.EstimatedY = poseResult.Ty;
                                result.EstimatedZ = poseResult.Tz;
                                result.EstimatedRx = poseResult.Rx * (180.0 / Math.PI); // Convert radians to degrees
                                result.EstimatedRy = poseResult.Ry * (180.0 / Math.PI);
                                result.EstimatedRz = poseResult.Rz * (180.0 / Math.PI);
                                result.Success = true;
                                Log($"[PoseEst] Step {task.StepNumber} SUCCESS: Tx={result.EstimatedX:F3}, Ty={result.EstimatedY:F3}, Tz={result.EstimatedZ:F3}");
                            }
                            else
                            {
                                result.Success = false;
                                result.ErrorMessage = $"Pose estimation failed: {poseResult.Status}";
                                Log($"[PoseEst] Step {task.StepNumber} FAILED: {poseResult.Status}");
                            }
                        }
                        else
                        {
                            result.Success = false;
                            result.ErrorMessage = "Pose estimation timed out after 120 seconds";
                            Log($"[PoseEst] Step {task.StepNumber} TIMEOUT after 120 seconds");
                        }
                    }
                    else
                    {
                        // Use default API (looks for Archive folder relative to image)
                        Log($"[PoseEst] Step {task.StepNumber}: Using default HMF paths (Archive folder)");
                        
                        var poseTask = Task.Run(() => PoseEstApiBridgeClient.EstimatePoseFromImagePath(
                            imagePath: task.ImagePath,
                            intrinsicMatrix: intrinsics.ToArray(),
                            pixelSize: task.PixelSize,
                            focalLength: task.FocalLength,
                            pitchX: task.PitchX,
                            pitchY: task.PitchY,
                            componentsX: task.ComponentsX,
                            componentsY: task.ComponentsY,
                            windowSize: task.WindowSize,
                            codePitchBlocks: task.CodePitchBlocks
                        ));
                        
                        // Wait with timeout (120 seconds)
                        if (await Task.WhenAny(poseTask, Task.Delay(120000)) == poseTask)
                        {
                            var poseResult = await poseTask;
                            Log($"[PoseEst] Step {task.StepNumber}: Native DLL returned status={poseResult.Status}");
                            
                            if (poseResult.Status == PoseStatus.Success)
                            {
                                result.EstimatedX = poseResult.Tx;
                                result.EstimatedY = poseResult.Ty;
                                result.EstimatedZ = poseResult.Tz;
                                result.EstimatedRx = poseResult.Rx * (180.0 / Math.PI); // Convert radians to degrees
                                result.EstimatedRy = poseResult.Ry * (180.0 / Math.PI);
                                result.EstimatedRz = poseResult.Rz * (180.0 / Math.PI);
                                result.Success = true;
                                Log($"[PoseEst] Step {task.StepNumber} SUCCESS: Tx={result.EstimatedX:F3}, Ty={result.EstimatedY:F3}, Tz={result.EstimatedZ:F3}");
                            }
                            else
                            {
                                result.Success = false;
                                result.ErrorMessage = $"Pose estimation failed: {poseResult.Status}";
                                Log($"[PoseEst] Step {task.StepNumber} FAILED: {poseResult.Status}");
                            }
                        }
                        else
                        {
                            result.Success = false;
                            result.ErrorMessage = "Pose estimation timed out after 120 seconds";
                            Log($"[PoseEst] Step {task.StepNumber} TIMEOUT after 120 seconds");
                        }
                    }
                }
                catch (Exception dllEx)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Native DLL exception: {dllEx.Message}";
                    Log($"[PoseEst] Step {task.StepNumber} DLL EXCEPTION: {dllEx.GetType().Name}: {dllEx.Message}");
                }
                finally
                {
                    _poseEstDllSemaphore.Release();
                    Log($"[PoseEst] Step {task.StepNumber}: Released DLL semaphore");
                }
            }
            catch (Exception ex)
            {
                Log($"[PoseEst] Step {task.StepNumber}: EXCEPTION in ProcessPoseEstimation: {ex.GetType().Name}: {ex.Message}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            Log($"[PoseEst] ProcessPoseEstimation EXIT for step {task.StepNumber}, success={result.Success}");
            return result;
        }

        /// <summary>
        /// Export pose estimation results to CSV
        /// </summary>
        [RelayCommand]
        private async Task ExportPoseResults()
        {
            try
            {
                Log($"[ExportResults] ENTER - checking results count...");
                
                if (_poseResults.Count == 0)
                {
                    Log("[ExportResults] No results to export");
                    return;
                }

                Log($"[ExportResults] Exporting {_poseResults.Count} results...");
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"pose_results_{timestamp}.csv";
                string filepath = Path.Combine(CaptureDirectory, filename);
                
                _lastExportedCsvPath = filepath; // Store for later use
                
                Log($"[ExportResults] CSV path: {filepath}");

                await Task.Run(() =>
                {
                    Log($"[ExportResults] Creating StreamWriter...");
                    using var writer = new StreamWriter(filepath);
                    
                    Log($"[ExportResults] Writing header...");
                    // Write header
                    writer.WriteLine("StepNumber,Timestamp,ImagePath," +
                                   "StageX,StageY,StageZ,StageRx,StageRy,StageRz," +
                                   "EstX,EstY,EstZ,EstRx,EstRy,EstRz," +
                                   "ErrorX,ErrorY,ErrorZ,ErrorRx,ErrorRy,ErrorRz," +
                                   "Success,ErrorMessage");

                    Log($"[ExportResults] Writing data rows...");
                    // Write data
                    lock (_poseResultsLock)
                    {
                        int rowCount = 0;
                        foreach (var result in _poseResults.OrderBy(r => r.StepNumber))
                        {
                            writer.WriteLine($"{result.StepNumber}," +
                                           $"{result.Timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                                           $"\"{result.ImagePath}\"," +
                                           $"{result.StageX:F6},{result.StageY:F6},{result.StageZ:F6}," +
                                           $"{result.StageRx:F6},{result.StageRy:F6},{result.StageRz:F6}," +
                                           $"{result.EstimatedX:F6},{result.EstimatedY:F6},{result.EstimatedZ:F6}," +
                                           $"{result.EstimatedRx:F6},{result.EstimatedRy:F6},{result.EstimatedRz:F6}," +
                                           $"{result.EstimatedX - result.StageX:F6}," +
                                           $"{result.EstimatedY - result.StageY:F6}," +
                                           $"{result.EstimatedZ - result.StageZ:F6}," +
                                           $"{result.EstimatedRx - result.StageRx:F6}," +
                                           $"{result.EstimatedRy - result.StageRy:F6}," +
                                           $"{result.EstimatedRz - result.StageRz:F6}," +
                                           $"{result.Success}," +
                                           $"\"{result.ErrorMessage}\"");
                            rowCount++;
                        }
                        Log($"[ExportResults] Wrote {rowCount} data rows");
                    }
                    Log($"[ExportResults] StreamWriter closing...");
                });

                Log($"[ExportResults] Exported {_poseResults.Count} results to {filename}");
                MovementStatus = $"Exported {_poseResults.Count} results to {filename}";
                
                // Open the folder containing the CSV file
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = CaptureDirectory,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    Log($"[ExportResults] Opened folder: {CaptureDirectory}");
                }
                catch (Exception openEx)
                {
                    Log($"[ExportResults] Could not open folder: {openEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"[ExportResults] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update graphs with new pose estimation result
        /// </summary>
        private void UpdateGraphsWithResult(PoseEstimationResult result)
        {
            try
            {
                if (!result.Success) return;

                // Calculate errors
                double errorX = result.EstimatedX - result.StageX;
                double errorY = result.EstimatedY - result.StageY;
                double errorZ = result.EstimatedZ - result.StageZ;
                double errorRx = result.EstimatedRx - result.StageRx;
                double errorRy = result.EstimatedRy - result.StageRy;
                double errorRz = result.EstimatedRz - result.StageRz;

                // Update on UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // Add to 2D error plots
                    ErrorXData.Add(new PlotPoint(result.StepNumber, errorX));
                    ErrorYData.Add(new PlotPoint(result.StepNumber, errorY));
                    ErrorZData.Add(new PlotPoint(result.StepNumber, errorZ));

                    // Add to 3D scatter plots
                    StagePositions3D.Add(new Models.Point3D(result.StageX, result.StageY, result.StageZ, "#4fc3f7"));
                    EstimatedPositions3D.Add(new Models.Point3D(result.EstimatedX, result.EstimatedY, result.EstimatedZ, "#ff9800"));

                    // Update max errors for scaling
                    MaxErrorX = Math.Max(MaxErrorX, Math.Abs(errorX));
                    MaxErrorY = Math.Max(MaxErrorY, Math.Abs(errorY));
                    MaxErrorZ = Math.Max(MaxErrorZ, Math.Abs(errorZ));

                    // Keep only last 100 points for performance
                    if (ErrorXData.Count > 100)
                    {
                        ErrorXData.RemoveAt(0);
                        ErrorYData.RemoveAt(0);
                        ErrorZData.RemoveAt(0);
                    }
                    
                    // Update Analysis tab in real-time with all successful results so far
                    UpdateAnalysisTabRealtime();
                });
            }
            catch (Exception ex)
            {
                Log($"[UpdateGraphs] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update Analysis tab in real-time as pose estimation results come in
        /// </summary>
        private void UpdateAnalysisTabRealtime()
        {
            try
            {
                Log("[UpdateAnalysisRealtime] ENTER");
                var analysisVM = AnalysisViewModel.Instance;
                if (analysisVM == null)
                {
                    Log("[UpdateAnalysisRealtime] AnalysisViewModel.Instance is NULL");
                    return;
                }

                // Get all successful results so far
                var errorData = new List<(double errorX, double errorY, double errorZ, double errorRx, double errorRy, double errorRz, double stageX, double stageY, double stageZ, double stageRx, double stageRy, double stageRz, double estX, double estY, double estZ, double estRx, double estRy, double estRz)>();
                
                lock (_poseResultsLock)
                {
                    foreach (var result in _poseResults.OrderBy(r => r.StepNumber))
                    {
                        if (result.Success)
                        {
                            errorData.Add((
                                result.EstimatedX - result.StageX,
                                result.EstimatedY - result.StageY,
                                result.EstimatedZ - result.StageZ,
                                result.EstimatedRx - result.StageRx,
                                result.EstimatedRy - result.StageRy,
                                result.EstimatedRz - result.StageRz,
                                result.StageX,
                                result.StageY,
                                result.StageZ,
                                result.StageRx,
                                result.StageRy,
                                result.StageRz,
                                result.EstimatedX,
                                result.EstimatedY,
                                result.EstimatedZ,
                                result.EstimatedRx,
                                result.EstimatedRy,
                                result.EstimatedRz
                            ));
                        }
                    }
                }

                Log($"[UpdateAnalysisRealtime] Found {errorData.Count} successful results");
                
                if (errorData.Count > 0)
                {
                    Log($"[UpdateAnalysisRealtime] Calling LoadPoseEstimationResults with {errorData.Count} results");
                    analysisVM.LoadPoseEstimationResults(errorData);
                    Log("[UpdateAnalysisRealtime] LoadPoseEstimationResults completed");
                }
            }
            catch (Exception ex)
            {
                Log($"[UpdateAnalysisRealtime] Error: {ex.Message}");
                Log($"[UpdateAnalysisRealtime] Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Clear all graph data
        /// </summary>
        [RelayCommand]
        private void ClearGraphs()
        {
            ErrorXData.Clear();
            ErrorYData.Clear();
            ErrorZData.Clear();
            StagePositions3D.Clear();
            EstimatedPositions3D.Clear();
            MaxErrorX = 0;
            MaxErrorY = 0;
            MaxErrorZ = 0;
            
            lock (_poseResultsLock)
            {
                _poseResults.Clear();
            }
            
            CapturedImageCount = 0;
            ProcessedPoseCount = 0;
            QueuedPoseCount = 0;
            
            Log("[ClearGraphs] All graphs and results cleared");
        }

        /// <summary>
        /// Send pose estimation results to Analysis tab
        /// </summary>
        private void SendResultsToAnalysisTab()
        {
            try
            {
                if (_poseResults.Count == 0)
                {
                    Log("[SendToAnalysis] No results to send");
                    return;
                }

                var analysisVM = AnalysisViewModel.Instance;
                if (analysisVM != null)
                {
                    // Convert results to the format expected by Analysis tab
                    var errorData = new List<(double errorX, double errorY, double errorZ, double errorRx, double errorRy, double errorRz, double stageX, double stageY, double stageZ, double stageRx, double stageRy, double stageRz, double estX, double estY, double estZ, double estRx, double estRy, double estRz)>();
                    
                    int totalResults = 0;
                    int successfulResults = 0;
                    int failedResults = 0;
                    
                    lock (_poseResultsLock)
                    {
                        totalResults = _poseResults.Count;
                        
                        foreach (var result in _poseResults.OrderBy(r => r.StepNumber))
                        {
                            if (result.Success)
                            {
                                successfulResults++;
                                errorData.Add((
                                    result.EstimatedX - result.StageX,
                                    result.EstimatedY - result.StageY,
                                    result.EstimatedZ - result.StageZ,
                                    result.EstimatedRx - result.StageRx,
                                    result.EstimatedRy - result.StageRy,
                                    result.EstimatedRz - result.StageRz,
                                    result.StageX,
                                    result.StageY,
                                    result.StageZ,
                                    result.StageRx,
                                    result.StageRy,
                                    result.StageRz,
                                    result.EstimatedX,
                                    result.EstimatedY,
                                    result.EstimatedZ,
                                    result.EstimatedRx,
                                    result.EstimatedRy,
                                    result.EstimatedRz
                                ));
                            }
                            else
                            {
                                failedResults++;
                            }
                        }
                    }

                    Log($"[SendToAnalysis] Total: {totalResults}, Successful: {successfulResults}, Failed: {failedResults}");
                    
                    if (successfulResults > 0)
                    {
                        analysisVM.LoadPoseEstimationResults(errorData);
                        
                        // Also load the session from CSV if available
                        if (!string.IsNullOrEmpty(_lastExportedCsvPath) && System.IO.File.Exists(_lastExportedCsvPath))
                        {
                            analysisVM.PreviewDataFolder = _lastExportedCsvPath;
                            analysisVM.LoadPreviewDataCommand.Execute(null);
                        }
                        
                        Log($"[SendToAnalysis] Sent {errorData.Count} results to Analysis tab");
                        MovementStatus = $"Results sent to Analysis tab ({successfulResults} successful, {failedResults} failed)";
                    }
                    else
                    {
                        // Show failure status in Analysis tab
                        analysisVM.ShowFailureStatus(totalResults, failedResults);
                        Log($"[SendToAnalysis] All {failedResults} pose estimation attempts failed - showing failure status in Analysis tab");
                        MovementStatus = $"Pose estimation completed: All {failedResults} attempts failed (check pattern visibility and camera focus)";
                    }
                }
                else
                {
                    Log("[SendToAnalysis] AnalysisViewModel.Instance is null");
                }
            }
            catch (Exception ex)
            {
                Log($"[SendToAnalysis] Error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Ensure Archive directory with HMF pattern files exists in capture directory
        /// </summary>
        private void EnsureArchiveFiles()
        {
            try
            {
                string archiveDir = Path.Combine(CaptureDirectory, "Archive");
                Directory.CreateDirectory(archiveDir);
                
                // Source archive files from application directory
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string sourceArchiveDir = Path.Combine(appDir, "Archive");
                
                // List of required archive files
                string[] archiveFiles = { "12+12_G.txt", "12+12_X.txt", "12+12_Y.txt" };
                
                foreach (string fileName in archiveFiles)
                {
                    string sourceFile = Path.Combine(sourceArchiveDir, fileName);
                    string destFile = Path.Combine(archiveDir, fileName);
                    
                    // Copy if source exists and (dest doesn't exist or is older)
                    if (File.Exists(sourceFile))
                    {
                        if (!File.Exists(destFile) || 
                            File.GetLastWriteTime(sourceFile) > File.GetLastWriteTime(destFile))
                        {
                            File.Copy(sourceFile, destFile, overwrite: true);
                            Log($"[Archive] Copied {fileName} to capture directory");
                        }
                    }
                    else
                    {
                        Log($"[Archive] WARNING: Source file not found: {sourceFile}");
                    }
                }
                
                Log($"[Archive] Archive directory ready: {archiveDir}");
            }
            catch (Exception ex)
            {
                Log($"[Archive] Error setting up archive files: {ex.Message}");
            }
        }
    }
}
