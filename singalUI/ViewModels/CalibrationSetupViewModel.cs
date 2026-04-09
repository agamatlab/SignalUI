using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using singalUI.Models;
using singalUI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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
        private ObservableCollection<ConnectedStageMotionGroup> _stageMotionGroups = new();

        [ObservableProperty]
        private int _totalPlannedPoints = 1;

        [ObservableProperty]
        private double _estimatedTime = 60;

        [ObservableProperty]
        private bool _isSequenceRunning = false;

        private bool _sequenceAbortUiStopped;
        private bool _sequenceEndedByUserAbort;

        [ObservableProperty]
        private string _abortSequenceButtonText = "Abort Sequence";

        [ObservableProperty]
        private string _abortSequenceButtonBackground = "#555555";

        partial void OnIsSequenceRunningChanged(bool value) => UpdateAbortSequenceButtonChrome();

        private void UpdateAbortSequenceButtonChrome()
        {
            if (_sequenceAbortUiStopped)
            {
                AbortSequenceButtonText = "Stopped";
                AbortSequenceButtonBackground = "#555555";
            }
            else
            {
                AbortSequenceButtonText = "Abort Sequence";
                AbortSequenceButtonBackground = IsSequenceRunning ? "#c62828" : "#555555";
            }
        }

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

        public ConfigViewModel RotationCalibVm => App.SharedConfigViewModel;

        // Position polling timer
        private Timer? _positionPollTimer;

        public CalibrationSetupViewModel()
        {
            // Initialize static StageManager with default stages FIRST
            StageManager.InitializeDefaultStages();

            // THEN subscribe to wrapper property changes
            InitializeMotionRows();

            IsRotationCalibrationMode = App.CalibrationAppMode == CalibrationAppMode.RotationStage;
            App.CalibrationAppModeChanged += OnGlobalCalibrationAppModeChanged;

            MotionRows.CollectionChanged += OnMotionRowsCollectionChanged;

            CalculateTotals();
            StartConnectionPolling();
            StartPositionPolling();
            UpdateAbortSequenceButtonChrome();
            Log("CalibrationSetupViewModel initialized");
        }

        [ObservableProperty]
        private bool _isRotationCalibrationMode;

        private void OnGlobalCalibrationAppModeChanged(object? sender, EventArgs e)
        {
            IsRotationCalibrationMode = App.CalibrationAppMode == CalibrationAppMode.RotationStage;
        }

        partial void OnIsRotationCalibrationModeChanged(bool value)
        {
            OnPropertyChanged(nameof(UseFullMotionTable));
            OnPropertyChanged(nameof(ShowRotationCompactMotion));
            OnPropertyChanged(nameof(StartSequenceButtonText));
        }

        /// <summary>Setup primary action label.</summary>
        public string StartSequenceButtonText => "Start Sequence";

        private void OnMotionRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(RotationMotionRow));
            OnPropertyChanged(nameof(ShowRotationCompactMotion));
        }

        /// <summary>When false, Setup shows compact single-DOF motion card (rotation calibration mode).</summary>
        public bool UseFullMotionTable => !IsRotationCalibrationMode;

        public bool ShowRotationCompactMotion => IsRotationCalibrationMode && MotionRows.Count > 0;

        /// <summary>First motion row for compact rotation UI (typically θ / Rz only).</summary>
        public MotionConfiguration? RotationMotionRow => MotionRows.Count > 0 ? MotionRows[0] : null;

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
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var row in MotionRows)
                    {
                        var w = StageWrappers.FirstOrDefault(sw => sw.Id == row.OwningStageId);
                        if (w == null || !w.IsConnected || w.Controller == null)
                            continue;
                        row.CurrentPosition = GetCurrentDisplayPosition(w, row.Axis);
                    }

                    foreach (var group in StageMotionGroups)
                    {
                        group.UpdateRawFromWrapper();
                    }

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
                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? logMessage : $"{ErrorLog}\n{logMessage}";

                // Keep only last MaxErrorLogLines lines
                var lines = ErrorLog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > MaxErrorLogLines)
                {
                    ErrorLog = string.Join("\n", lines.Skip(lines.Length - MaxErrorLogLines));
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
            StageMotionGroups.Clear();

            foreach (var wrapper in StageWrappers)
            {
                Log($"[UpdateAxis] Checking Stage {wrapper.Id}: IsConnected={wrapper.IsConnected}, Controller={wrapper.Controller != null}");

                if (!wrapper.IsConnected || wrapper.Controller == null)
                {
                    continue;
                }

                var axes = wrapper.EnabledAxes;
                var availableAxes = wrapper.AvailableAxes;
                Log($"[UpdateAxis] Stage {wrapper.Id}: EnabledAxes={axes.Length}, AvailableAxes={availableAxes.Length}");
                Log($"[UpdateAxis] Stage {wrapper.Id}: Available axes: [{string.Join(", ", availableAxes)}]");

                var groupRows = new ObservableCollection<MotionConfiguration>();

                for (int i = 0; i < axes.Length && i < availableAxes.Length; i++)
                {
                    var axis = axes[i];
                    bool rot = IsRotationalAxis(axis);
                    var config = new MotionConfiguration
                    {
                        OwningStageId = wrapper.Id,
                        Axis = axis,
                        Label = $"{axis} ({availableAxes[i]})",
                        Unit = GetAxisUnit(axis),
                        IsEnabled = true,
                        Range = rot ? 90 : 1000,
                        StepSize = rot ? 1 : 100,
                        NumSteps = 10,
                        TargetPosition = 0
                    };
                    MotionRows.Add(config);
                    groupRows.Add(config);
                    Log($"[UpdateAxis] Added axis: {config.Label}, Unit={config.Unit}, StepSize={config.StepSize}");
                }

                if (groupRows.Count > 0)
                {
                    var group = new ConnectedStageMotionGroup(wrapper, groupRows);
                    group.RefreshAxisVisibility();
                    StageMotionGroups.Add(group);
                }
            }

            Log($"[UpdateAxis] Total motion rows: {MotionRows.Count}, stage groups: {StageMotionGroups.Count}");
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
            foreach (var wrapper in StageWrappers)
            {
                wrapper.PropertyChanged += OnWrapperPropertyChanged;
            }

            StageWrappers.CollectionChanged += (_, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (StageWrapper w in e.NewItems)
                        w.PropertyChanged += OnWrapperPropertyChanged;
                }
                if (e.OldItems != null)
                {
                    foreach (StageWrapper w in e.OldItems)
                        w.PropertyChanged -= OnWrapperPropertyChanged;
                }
            };
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
            MotionRows.CollectionChanged -= OnMotionRowsCollectionChanged;
            App.CalibrationAppModeChanged -= OnGlobalCalibrationAppModeChanged;
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

        private const int MinRotationCalibRzSteps = 12;

        [RelayCommand]
        private async Task StartSequence()
        {
            _sequenceEndedByUserAbort = false;
            _sequenceAbortUiStopped = false;
            UpdateAbortSequenceButtonChrome();

            var enabledAxes = MotionRows.Where(r => r.IsEnabled).ToList();
            if (enabledAxes.Count == 0)
            {
                MovementStatus = "No axis enabled";
                Log("[StartSequence] No axis enabled in motion configuration");
                return;
            }

            if (IsRotationCalibrationMode)
            {
                var rotationRow = enabledAxes.FirstOrDefault(r => r.Axis == AxisType.Rz);
                if (rotationRow == null)
                {
                    MovementStatus = "Rotation capture: enable the Rz (θ) motion row.";
                    Log("[StartSequence] Rotation mode: no enabled Rz row");
                    return;
                }

                if (rotationRow.NumSteps < MinRotationCalibRzSteps)
                {
                    MovementStatus = $"Set Points to at least {MinRotationCalibRzSteps} on the Rz row.";
                    Log($"[StartSequence] Rotation mode: NumSteps={rotationRow.NumSteps} < {MinRotationCalibRzSteps}");
                    return;
                }
            }

            IsSequenceRunning = true;
            int totalAxes = enabledAxes.Count;
            int currentAxis = 0;
            string? rotationCaptureSessionDir = null;
            if (IsRotationCalibrationMode)
            {
                rotationCaptureSessionDir = RotationCalibVm.CreateRotationCaptureSessionDirectory();
                Log($"[StartSequence] Rotation capture session: {rotationCaptureSessionDir}");
                MovementStatus = $"Saving frames to: {rotationCaptureSessionDir}";
            }

            try
            {
                Log($"========== StartSequence {(MovementModeAbsolute ? "ABSOLUTE" : "RELATIVE")} ==========");
                Log($"[StartSequence] Total axes: {totalAxes}");

                if (IsRotationCalibrationMode && rotationCaptureSessionDir != null)
                {
                    await MoveRzToZeroBeforeRotationCaptureAsync(enabledAxes);
                    if (!IsSequenceRunning)
                    {
                        MovementStatus = "Sequence aborted";
                        return;
                    }
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

                    double currentDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                    string u = DisplayUnitForAxis(axisType);
                    axisConfig.CurrentPosition = currentDisplay;

                    if (MovementModeAbsolute)
                    {
                        double stepSizeUi = axisConfig.AutoStepSize;
                        double stepAmount = GetStepAmountForController(wrapper, stepSizeUi, axisType);
                        int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                        double totalMoveUi = stepSizeUi * stepCount;

                        Log($"[StartSequence] Axis {currentAxis}/{totalAxes}: {axisConfig.Label} ABSOLUTE");
                        Log($"[StartSequence] StepSize(UI)={stepSizeUi}, StepAmount(sent)={stepAmount}");
                        Log($"  Current: {currentDisplay:F2} {u}");
                        Log($"  StepSize: {stepSizeUi:F2} {u}, Steps: {stepCount}, Total: {totalMoveUi:F2} {u}");
                        MovementStatus = $"{axisConfig.Label}: Moving {totalMoveUi:F0} {u}...";

                        for (int step = 0; step < stepCount; step++)
                        {
                            if (!IsSequenceRunning)
                            {
                                Log($"[StartSequence] Aborted");
                                MovementStatus = "Sequence aborted";
                                return;
                            }

                            await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));

                            RefreshPositionsNow();

                            MovementStatus = $"{axisConfig.Label}: Step {step + 1}/{stepCount}";
                            Log($"  Step {step + 1}/{stepCount} +{stepSizeUi:F2} {u}");
                            int settleMs = IsRotationCalibrationMode && axisType == AxisType.Rz ? 400 : 50;
                            await Task.Delay(settleMs);

                            if (IsRotationCalibrationMode && axisType == AxisType.Rz &&
                                rotationCaptureSessionDir != null)
                            {
                                MovementStatus = $"{axisConfig.Label}: Save frame {step + 1}/{stepCount}…";
                                var save = RotationCalibVm.TrySaveRotationCaptureFrame(
                                    rotationCaptureSessionDir,
                                    step + 1);
                                if (save.ok)
                                    Log($"[StartSequence] Saved {save.savedPath}");
                                else
                                {
                                    Log($"[StartSequence] Frame {step + 1} save failed (continuing): {save.message}");
                                    MovementStatus =
                                        $"{axisConfig.Label}: Frame {step + 1} save failed — continuing";
                                }
                            }
                        }

                        double finalDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                        double actualMoveUi = finalDisplay - currentDisplay;
                        Log($"  Done: now at {finalDisplay:F2} {u} (moved {actualMoveUi:F2} {u})");
                    }
                    else
                    {
                        double stepSizeUi = axisConfig.StepSize;
                        double stepAmount = GetStepAmountForController(wrapper, stepSizeUi, axisType);
                        int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                        double totalMoveUi = stepSizeUi * stepCount;

                        Log($"[StartSequence] Axis {currentAxis}/{totalAxes}: {axisConfig.Label} RELATIVE");
                        Log($"[StartSequence] StepSize(UI)={stepSizeUi}, StepAmount(sent)={stepAmount}");
                        Log($"  Current: {currentDisplay:F2} {u}");
                        Log($"  StepSize: {stepSizeUi:F2} {u}, Steps: {stepCount}, Total: {totalMoveUi:F2} {u}");
                        MovementStatus = $"{axisConfig.Label}: Moving {totalMoveUi:F0} {u}...";

                        for (int step = 0; step < stepCount; step++)
                        {
                            if (!IsSequenceRunning)
                            {
                                Log($"[StartSequence] Aborted");
                                MovementStatus = "Sequence aborted";
                                return;
                            }

                            await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));

                            RefreshPositionsNow();

                            MovementStatus = $"{axisConfig.Label}: Step {step + 1}/{stepCount}";
                            Log($"  Step {step + 1}/{stepCount} +{stepSizeUi:F2} {u}");
                            int settleMsRel = IsRotationCalibrationMode && axisType == AxisType.Rz ? 400 : 50;
                            await Task.Delay(settleMsRel);

                            if (IsRotationCalibrationMode && axisType == AxisType.Rz &&
                                rotationCaptureSessionDir != null)
                            {
                                MovementStatus = $"{axisConfig.Label}: Save frame {step + 1}/{stepCount}…";
                                var save = RotationCalibVm.TrySaveRotationCaptureFrame(
                                    rotationCaptureSessionDir,
                                    step + 1);
                                if (save.ok)
                                    Log($"[StartSequence] Saved {save.savedPath}");
                                else
                                {
                                    Log($"[StartSequence] Frame {step + 1} save failed (continuing): {save.message}");
                                    MovementStatus =
                                        $"{axisConfig.Label}: Frame {step + 1} save failed — continuing";
                                }
                            }
                        }

                        double finalDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                        double actualMoveUi = finalDisplay - currentDisplay;
                        Log($"  Done: now at {finalDisplay:F2} {u} (moved {actualMoveUi:F2} {u})");
                    }
                }

                MovementStatus = rotationCaptureSessionDir != null
                    ? $"Rotation capture done. Folder: {rotationCaptureSessionDir}"
                    : $"Sequence completed ({totalAxes} axes)";
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
                if (!_sequenceEndedByUserAbort)
                    _sequenceAbortUiStopped = false;
                UpdateAbortSequenceButtonChrome();
            }
        }

        [RelayCommand]
        private void AbortSequence()
        {
            if (IsSequenceRunning)
            {
                _sequenceEndedByUserAbort = true;
                _sequenceAbortUiStopped = true;
                Log("[AbortSequence] Aborting sequence...");
                MovementStatus = "Aborting...";
                IsSequenceRunning = false;
                UpdateAbortSequenceButtonChrome();
            }
        }

        [RelayCommand]
        private void SamplePoint()
        {
        }

        /// <summary>Absolute Rz → 0° before rotation capture so the stepped sweep starts from a known angle.</summary>
        private async Task MoveRzToZeroBeforeRotationCaptureAsync(List<MotionConfiguration> enabledAxes)
        {
            var rzRow = enabledAxes.FirstOrDefault(r => r.Axis == AxisType.Rz);
            if (rzRow == null)
                return;

            var axisId = GetAxisId(rzRow.Axis);
            var wrapper = GetStageWrapper(axisId);
            if (wrapper == null || !wrapper.IsConnected || !wrapper.ControlsAxis(AxisType.Rz))
            {
                Log("[StartSequence] Rz→0° skipped: no connected stage with Rz");
                return;
            }

            try
            {
                MovementStatus = "Rotation: moving Rz to 0° before capture…";
                Log("[StartSequence] Rz absolute move to 0° before rotation capture sweep");
                await Task.Run(() => wrapper.MoveAxisAbsolute(AxisType.Rz, 0));
                if (!IsSequenceRunning)
                    return;
                RefreshPositionsNow();
                await Task.Delay(600);
                if (!IsSequenceRunning)
                    return;
                rzRow.CurrentPosition = GetCurrentDisplayPosition(wrapper, AxisType.Rz);
                Log($"[StartSequence] Rz homing done, display ≈ {rzRow.CurrentPosition:F3}°");
            }
            catch (Exception ex)
            {
                Log($"[StartSequence] Rz homing failed: {ex.Message} (continuing with sequence)");
                MovementStatus = $"Rz homing failed — continuing: {ex.Message}";
            }
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
            bool rot = IsRotationalAxis(axisType);
            string u = DisplayUnitForAxis(axisType);
            Log($"[MoveAxis] Moving axis {axisId} ({axisType}) by {distance:F4} {u} using Stage {wrapper.Id}");

            double moveAmount = rot ? distance : distance / 1000.0;
            Log($"[MoveAxis] Controller delta: {moveAmount:F6} {(rot ? "deg" : "mm")}");

            await Task.Run(() => wrapper.MoveAxis(axisType, moveAmount));
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

                string u = DisplayUnitForAxis(axisConfig.Axis);
                MovementStatus = $"Success: Moved {distance:F3} {u}";
                Log($"[MoveSingle] Success: Moved axis {axisConfig.Label} by {distance:F3} {u}");
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

                double currentDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                string u = DisplayUnitForAxis(axisType);

                if (MovementModeAbsolute)
                {
                    double stepSizeUi = axisConfig.AutoStepSize;
                    double stepAmount = GetStepAmountForController(wrapper, stepSizeUi, axisType);
                    int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                    double totalMoveUi = stepSizeUi * stepCount;

                    Log($"========== ABSOLUTE MOVE DEBUG ==========");
                    Log($"[MoveToTarget] Mode: ABSOLUTE");
                    Log($"[MoveToTarget] Axis: {axisConfig.Label} ({axisType})");
                    Log($"[MoveToTarget] StepSize(UI)={stepSizeUi}, StepAmount(sent)={stepAmount}");
                    Log($"[MoveToTarget] Current Position: {currentDisplay:F4} {u}");
                    Log($"[MoveToTarget] Step Size: {stepSizeUi:F4} {u}");
                    Log($"[MoveToTarget] Step Count: {stepCount}");
                    Log($"[MoveToTarget] Total Move: {totalMoveUi:F4} {u}");
                    Log($"==========================================");

                    MovementStatus = $"Moving by {stepSizeUi:F3} {u} × {stepCount} steps...";

                    for (int i = 0; i < stepCount; i++)
                    {
                        double posBefore = GetCurrentDisplayPosition(wrapper, axisType);

                        Log($"[MoveToTarget] Step {i + 1}/{stepCount}: At {posBefore:F4} {u}, moving by {stepSizeUi:F4} {u}");

                        await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));

                        double posAfter = GetCurrentDisplayPosition(wrapper, axisType);
                        double actualMoveUi = posAfter - posBefore;

                        Log($"[MoveToTarget] Step {i + 1} completed: Actually moved {actualMoveUi:F4} {u}, now at {posAfter:F4} {u}");
                    }

                    double finalDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                    double actualTotalMoveUi = finalDisplay - currentDisplay;

                    Log($"[MoveToTarget] FINAL: Position {finalDisplay:F4} {u}, Total moved: {actualTotalMoveUi:F4} {u}");
                    MovementStatus = $"Success: Moved {actualTotalMoveUi:F3} {u} (now {finalDisplay:F3} {u})";

                    OnPropertyChanged(nameof(CalculatedEndPosition));
                }
                else
                {
                    double distanceUi = axisConfig.StepSize;
                    double distanceAmount = GetStepAmountForController(wrapper, distanceUi, axisType);
                    int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                    double totalMoveUi = distanceUi * stepCount;

                    Log($"========== RELATIVE MOVE DEBUG ==========");
                    Log($"[MoveToTarget] Mode: RELATIVE");
                    Log($"[MoveToTarget] Axis: {axisConfig.Label} ({axisType})");
                    Log($"[MoveToTarget] StepSize(UI)={distanceUi}, StepAmount(sent)={distanceAmount}");
                    Log($"[MoveToTarget] Current Position: {currentDisplay:F4} {u}");
                    Log($"[MoveToTarget] Distance per step: {distanceUi:F4} {u}");
                    Log($"[MoveToTarget] Step Count: {stepCount}");
                    Log($"[MoveToTarget] Total Expected Move: {totalMoveUi:F4} {u}");
                    Log($"==========================================");

                    MovementStatus = $"Moving by {distanceUi:F3} {u} × {stepCount} steps...";

                    for (int i = 0; i < stepCount; i++)
                    {
                        double posBefore = GetCurrentDisplayPosition(wrapper, axisType);

                        Log($"[MoveToTarget] Step {i + 1}/{stepCount}: At {posBefore:F4} {u}, moving by {distanceUi:F4} {u}");

                        await Task.Run(() => wrapper.MoveAxis(axisType, distanceAmount));

                        double posAfter = GetCurrentDisplayPosition(wrapper, axisType);
                        double actualMoveUi = posAfter - posBefore;

                        Log($"[MoveToTarget] Step {i + 1} completed: Actually moved {actualMoveUi:F4} {u}, now at {posAfter:F4} {u}");
                        await Task.Delay(50);
                    }

                    double finalDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                    double actualTotalMoveUi = finalDisplay - currentDisplay;

                    Log($"[MoveToTarget] FINAL: Position {finalDisplay:F4} {u}, Total moved: {actualTotalMoveUi:F4} {u}");
                    MovementStatus = $"Success: At {finalDisplay:F3} {u} (moved {actualTotalMoveUi:F3} {u})";

                    OnPropertyChanged(nameof(CalculatedEndPosition));
                }
            }
            catch (Exception ex)
            {
                MovementStatus = $"Error: {ex.Message}";
                Log($"[MoveToTarget] Error: {ex.Message}");
            }
        }

        private static bool IsRotationalAxis(AxisType axis) =>
            axis is AxisType.Rx or AxisType.Ry or AxisType.Rz;

        private static double GetCurrentDisplayPosition(StageWrapper wrapper, AxisType axisType)
        {
            double controllerUnits = wrapper.GetAxisPosition(axisType);
            return IsRotationalAxis(axisType) ? controllerUnits : controllerUnits * 1000.0;
        }

        private static string DisplayUnitForAxis(AxisType axisType) =>
            IsRotationalAxis(axisType) ? "deg" : "µm";

        /// <summary>Convert UI step (µm linear or deg rotational) to units expected by <see cref="StageWrapper.MoveAxis"/>.</summary>
        private static double GetStepAmountForController(StageWrapper wrapper, double uiStepDisplayUnits, AxisType axisType)
        {
            if (wrapper.Controller is Hsc103RotationStageController)
                return uiStepDisplayUnits;
            if (wrapper.Controller is SigmakokiController)
                return uiStepDisplayUnits;
            if (IsRotationalAxis(axisType))
                return uiStepDisplayUnits;
            return uiStepDisplayUnits / 1000.0;
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
                var axisType = axisConfig.Axis;
                double currentDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                string u = DisplayUnitForAxis(axisType);

                if (MovementModeAbsolute)
                {
                    double stepSizeUi = axisConfig.AutoStepSize;
                    Log($"[Step Size UI = {stepSizeUi} {u}]");
                    double stepAmount = GetStepAmountForController(wrapper, stepSizeUi, axisType);
                    int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                    double totalMoveUi = stepSizeUi * stepCount;

                    Log($"========== MoveAxisLocal ABSOLUTE ==========");
                    Log($"[MoveAxisLocal] Axis: {axisConfig.Label}");
                    Log($"[MoveAxisLocal] StepAmount(sent): {stepAmount}");
                    Log($"[MoveAxisLocal] axisConfig.TargetPosition: {axisConfig.TargetPosition} {u}");
                    Log($"[MoveAxisLocal] axisConfig.NumSteps: {axisConfig.NumSteps}");
                    Log($"[MoveAxisLocal] Current Position: {currentDisplay:F4} {u}");
                    Log($"[MoveAxisLocal] Step Size: {stepSizeUi:F4} {u}");
                    Log($"[MoveAxisLocal] Step Count: {stepCount}");
                    Log($"[MoveAxisLocal] Total Move: {totalMoveUi:F4} {u}");
                    Log($"=============================================");

                    MovementStatus = $"Moving {axisConfig.Label} by {totalMoveUi:F1} {u}...";

                    for (int step = 0; step < stepCount; step++)
                    {
                        Log($"[MoveAxisLocal] Step {step + 1}/{stepCount}: Moving by {stepSizeUi:F4} {u}");

                        double posBefore = GetCurrentDisplayPosition(wrapper, axisType);
                        await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));
                        double posAfter = GetCurrentDisplayPosition(wrapper, axisType);
                        double actualMoveUi = posAfter - posBefore;

                        RefreshPositionsNow();

                        Log($"[MoveAxisLocal] Step {step + 1} done: moved {actualMoveUi:F4} {u}, now at {posAfter:F4} {u}");

                        MovementStatus = $"{axisConfig.Label}: Step {step + 1}/{stepCount}";
                        await Task.Delay(50);
                    }

                    double finalDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                    Log($"[MoveAxisLocal] FINAL: {finalDisplay:F4} {u}");
                    MovementStatus = $"Done: {axisConfig.Label} at {finalDisplay:F1} {u}";
                }
                else
                {
                    double stepSizeUi = axisConfig.StepSize;
                    double stepAmount = GetStepAmountForController(wrapper, stepSizeUi, axisType);
                    int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                    double totalMoveUi = stepSizeUi * stepCount;

                    Log($"========== MoveAxisLocal RELATIVE ==========");
                    Log($"[MoveAxisLocal] Axis: {axisConfig.Label}");
                    Log($"[MoveAxisLocal] StepAmount(sent): {stepAmount}");
                    Log($"[MoveAxisLocal] axisConfig.StepSize: {axisConfig.StepSize} {u}");
                    Log($"[MoveAxisLocal] axisConfig.NumSteps: {axisConfig.NumSteps}");
                    Log($"[MoveAxisLocal] Current Position: {currentDisplay:F4} {u}");
                    Log($"[MoveAxisLocal] Step Size: {stepSizeUi:F4} {u}");
                    Log($"[MoveAxisLocal] Step Count: {stepCount}");
                    Log($"[MoveAxisLocal] Total Move: {totalMoveUi:F4} {u}");
                    Log($"=============================================");

                    MovementStatus = $"Moving {axisConfig.Label} by {totalMoveUi:F1} {u}...";

                    for (int step = 0; step < stepCount; step++)
                    {
                        double posBefore = GetCurrentDisplayPosition(wrapper, axisType);

                        Log($"[MoveAxisLocal] Step {step + 1}/{stepCount}: At {posBefore:F4} {u}, moving by {stepSizeUi:F4} {u}");

                        await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));

                        double posAfter = GetCurrentDisplayPosition(wrapper, axisType);
                        double actualMoveUi = posAfter - posBefore;

                        RefreshPositionsNow();

                        Log($"[MoveAxisLocal] Step {step + 1} done: actually moved {actualMoveUi:F4} {u}, now at {posAfter:F4} {u}");

                        MovementStatus = $"{axisConfig.Label}: Step {step + 1}/{stepCount}";
                        await Task.Delay(50);
                    }

                    double finalDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                    double actualTotalUi = finalDisplay - currentDisplay;
                    Log($"[MoveAxisLocal] FINAL: {finalDisplay:F4} {u} (moved {actualTotalUi:F4} {u})");
                    MovementStatus = $"Done: {axisConfig.Label} moved {actualTotalUi:F1} {u}";
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

                    var axisType = axisConfig.Axis;
                    double currentDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                    string u = DisplayUnitForAxis(axisType);

                    if (MovementModeAbsolute)
                    {
                        double stepSizeUi = axisConfig.AutoStepSize;
                        double stepAmount = GetStepAmountForController(wrapper, stepSizeUi, axisType);
                        int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                        double totalMoveUi = stepSizeUi * stepCount;

                        Log($"[MoveAllAxesLocal] Axis {currentAxis}/{totalAxes}: {axisConfig.Label} ABSOLUTE");
                        Log($"  Current: {currentDisplay:F4} {u}");
                        Log($"  StepSize: {stepSizeUi:F4} {u}, Steps: {stepCount}, Total: {totalMoveUi:F4} {u}");

                        for (int step = 0; step < stepCount; step++)
                        {
                            if (!IsSequenceRunning)
                            {
                                Log($"[MoveAllAxesLocal] Aborted");
                                MovementStatus = "Sequence aborted";
                                return;
                            }

                            await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));

                            RefreshPositionsNow();

                            MovementStatus = $"Axis {currentAxis}/{totalAxes}: {axisConfig.Label} step {step + 1}/{stepCount}";
                            await Task.Delay(50);
                        }

                        double finalDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                        double actualMoveUi = finalDisplay - currentDisplay;
                        Log($"  Done: now at {finalDisplay:F4} {u} (moved {actualMoveUi:F4} {u})");
                    }
                    else
                    {
                        double stepSizeUi = axisConfig.StepSize;
                        double stepAmount = GetStepAmountForController(wrapper, stepSizeUi, axisType);
                        int stepCount = axisConfig.NumSteps > 0 ? axisConfig.NumSteps : 1;
                        double totalMoveUi = stepSizeUi * stepCount;

                        Log($"[MoveAllAxesLocal] Axis {currentAxis}/{totalAxes}: {axisConfig.Label} RELATIVE");
                        Log($"  Current: {currentDisplay:F4} {u}");
                        Log($"  StepSize: {stepSizeUi:F4} {u}, Steps: {stepCount}, Total: {totalMoveUi:F4} {u}");

                        for (int step = 0; step < stepCount; step++)
                        {
                            if (!IsSequenceRunning)
                            {
                                Log($"[MoveAllAxesLocal] Aborted");
                                MovementStatus = "Sequence aborted";
                                return;
                            }

                            await Task.Run(() => wrapper.MoveAxis(axisType, stepAmount));

                            RefreshPositionsNow();

                            MovementStatus = $"Axis {currentAxis}/{totalAxes}: {axisConfig.Label} step {step + 1}/{stepCount}";
                            await Task.Delay(50);
                        }

                        double finalDisplay = GetCurrentDisplayPosition(wrapper, axisType);
                        double actualMoveUi = finalDisplay - currentDisplay;
                        Log($"  Done: now at {finalDisplay:F4} {u} (moved {actualMoveUi:F4} {u})");
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
