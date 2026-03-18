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

namespace singalUI.ViewModels
{
    public partial class CalibrationSetupViewModel : ViewModelBase
    {
        private readonly string _logFile = Path.Combine(Path.GetTempPath(), "singalui_debug.log");

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

        public CalibrationSetupViewModel()
        {
            // Initialize static StageManager with default stages FIRST
            StageManager.InitializeDefaultStages();

            // THEN subscribe to wrapper property changes
            InitializeMotionRows();

            CalculateTotals();
            StartConnectionPolling();
            StartPositionPolling();
            Log("CalibrationSetupViewModel initialized");
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
        /// Get the unit for an axis type
        /// </summary>
        private string GetAxisUnit(AxisType axis)
        {
            return axis switch
            {
                AxisType.X => "µm",
                AxisType.Y => "µm",
                AxisType.Z => "µm",
                AxisType.Rx => "deg",
                AxisType.Ry => "deg",
                AxisType.Rz => "deg",
                _ => ""
            };
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

            try
            {
                Log($"========== StartSequence {(MovementModeAbsolute ? "ABSOLUTE" : "RELATIVE")} ==========");
                Log($"[StartSequence] Total axes: {totalAxes}");

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

                            await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));

                            // Update position display immediately
                            RefreshPositionsNow();

                            MovementStatus = $"{axisConfig.Label}: Step {step + 1}/{stepCount}";
                            Log($"  Step {step + 1}/{stepCount} +{stepSizeInUm:F2} µm");
                            await Task.Delay(50);
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
                            await Task.Delay(50);
                        }

                        double finalPosInMm = wrapper.GetAxisPosition(axisType);
                        double actualMoveUm = (finalPosInMm - currentPosInMm) * 1000.0;
                        Log($"  Done: now at {finalPosInMm*1000:F2} µm (moved {actualMoveUm:F2} µm)");
                    }
                }

                MovementStatus = $"Sequence completed ({totalAxes} axes)";
                Log($"[StartSequence] === COMPLETED ===");
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
                        await Task.Delay(50);
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
    }
}
