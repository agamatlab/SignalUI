using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using singalUI.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace singalUI.ViewModels
{
    public partial class AnalysisViewModel : ViewModelBase
    {
        // Static instance for cross-ViewModel communication
        public static AnalysisViewModel? Instance { get; private set; }

        [ObservableProperty]
        private bool _showGrid = true;

        [ObservableProperty]
        private bool _showPoints = true;

        [ObservableProperty]
        private bool _showMesh = false;

        [ObservableProperty]
        private bool _showX = true;

        [ObservableProperty]
        private bool _showY = true;

        [ObservableProperty]
        private bool _showZ = true;

        [ObservableProperty]
        private bool _showRx = true;

        [ObservableProperty]
        private bool _showRy = true;

        [ObservableProperty]
        private bool _showRz = true;

        [ObservableProperty]
        private int _selectedPlotLayout;

        // Modular Chart Cards Collection
        [ObservableProperty]
        private ObservableCollection<ChartCardViewModel> _chartCards = new();

        [ObservableProperty]
        private string _overall_Rms = string.Empty;

        [ObservableProperty]
        private Visualization3DData? _visualization3DData;

        // Full Screen Mode
        [ObservableProperty]
        private bool _isFullScreen = false;

        [ObservableProperty]
        private ChartCardViewModel? _selected2DChart;

        // Preview Mode
        [ObservableProperty]
        private string _previewDataFolder = string.Empty;

        [ObservableProperty]
        private bool _hasPreviewData = false;

        [ObservableProperty]
        private int _previewImageCount = 0;

        [ObservableProperty]
        private int _previewDataPoints = 0;

        [ObservableProperty]
        private string _previewDate = string.Empty;

        // Error log for UI display
        private const int MaxErrorLogLines = 100;

        [ObservableProperty]
        private string _errorLog = "";
        
        [ObservableProperty]
        private bool _hasData = false;
        
        [ObservableProperty]
        private string _statusMessage = "";
        
        [ObservableProperty]
        private bool _hasStatusMessage = false;
        
        [ObservableProperty]
        private string _currentSessionPath = "";
        
        [ObservableProperty]
        private string _currentSessionName = "";
        
        [ObservableProperty]
        private int _currentSessionResultCount = 0;
        
        // Store current session data
        private Models.PoseEstimationSession? _currentSession = null;
        private List<(double errorX, double errorY, double errorZ, double errorRx, double errorRy, double errorRz, double stageX, double stageY, double stageZ, double stageRx, double stageRy, double stageRz, double estX, double estY, double estZ, double estRx, double estRy, double estRz)> _latestLoadedResults = new();

        public bool IsSix2DMode => SelectedPlotLayout == 0;

        public bool IsSingle3DMode => SelectedPlotLayout == 1;

        public bool IsTwoDGridMode => IsSix2DMode && (!IsFullScreen || Selected2DChart == null);

        public bool IsSelected2DChartFullScreen => IsSix2DMode && IsFullScreen && Selected2DChart != null;

        public string FullScreenButtonLabel => IsFullScreen ? "Minimize" : "Full Screen";

        public AnalysisViewModel()
        {
            Instance = this; // Set static instance for cross-ViewModel access
            SelectedPlotLayout = 0;
            // Don't generate mock data - wait for real pose estimation results
            LogError("AnalysisViewModel initialized - Waiting for pose estimation data");
        }

        partial void OnSelectedPlotLayoutChanged(int value)
        {
            OnPropertyChanged(nameof(IsSix2DMode));
            OnPropertyChanged(nameof(IsSingle3DMode));
            OnPropertyChanged(nameof(IsTwoDGridMode));
            OnPropertyChanged(nameof(IsSelected2DChartFullScreen));
        }

        partial void OnIsFullScreenChanged(bool value)
        {
            OnPropertyChanged(nameof(IsTwoDGridMode));
            OnPropertyChanged(nameof(IsSelected2DChartFullScreen));
            OnPropertyChanged(nameof(FullScreenButtonLabel));
        }

        partial void OnSelected2DChartChanged(ChartCardViewModel? value)
        {
            foreach (var card in ChartCards)
            {
                card.IsSelected = ReferenceEquals(card, value);
            }

            OnPropertyChanged(nameof(IsTwoDGridMode));
            OnPropertyChanged(nameof(IsSelected2DChartFullScreen));
        }

        private void Generate3DData()
        {
            var data = new Visualization3DData();
            var random = new Random(42);
            const int pointCount = 50;

            for (int i = 0; i < pointCount; i++)
            {
                double t = i / (double)pointCount;
                double angle = t * 4 * Math.PI;
                double radius = 30 + t * 20;

                double x = radius * Math.Cos(angle) + (random.NextDouble() - 0.5) * 5;
                double y = radius * Math.Sin(angle) + (random.NextDouble() - 0.5) * 5;
                double z = t * 50 + (random.NextDouble() - 0.5) * 3;

                data.Points.Add(new Point3D(x, y, z, "#4CAF50", 4.0));

                double xError = (random.NextDouble() - 0.5) * 3;
                data.Lines.Add(new Line3D(
                    new Point3D(x, y, z),
                    new Point3D(x + xError, y, z),
                    "#FF5252", 1.5));

                double yError = (random.NextDouble() - 0.5) * 3;
                data.Lines.Add(new Line3D(
                    new Point3D(x, y, z),
                    new Point3D(x, y + yError, z),
                    "#4CAF50", 1.5));

                double zError = (random.NextDouble() - 0.5) * 2;
                data.Lines.Add(new Line3D(
                    new Point3D(x, y, z),
                    new Point3D(x, y, z + zError),
                    "#2196F3", 1.5));
            }

            for (int i = 0; i < pointCount - 1; i++)
            {
                data.Lines.Add(new Line3D(
                    data.Points[i],
                    data.Points[i + 1],
                    "#FF9800", 2.0));
            }

            data.CalculateBounds();
            data.GenerateGrid(10);
            data.GenerateMeshFromScatteredPoints();
            Visualization3DData = data;
        }

        private void GenerateMockData()
        {
            var random = new Random(42); // Fixed seed for reproducibility

            // Generate 40 bars per axis
            int barCount = 40;

            // Define all axes in one place - easy to modify!
            var axisConfigs = new[]
            {
                new
                {
                    Name = "X",
                    Title = "X Position Error",
                    Unit = "mm",
                    Color = "#FF5252",
                    RowIndex = 0,
                    ColumnIndex = 0,
                    Min = 0.008, Max = 0.025
                },
                new
                {
                    Name = "Y",
                    Title = "Y Position Error",
                    Unit = "mm",
                    Color = "#4CAF50",
                    RowIndex = 0,
                    ColumnIndex = 2,
                    Min = 0.010, Max = 0.030
                },
                new
                {
                    Name = "Z",
                    Title = "Z Position Error",
                    Unit = "mm",
                    Color = "#2196F3",
                    RowIndex = 2,
                    ColumnIndex = 0,
                    Min = 0.005, Max = 0.015
                },
                new
                {
                    Name = "Rx",
                    Title = "Rx Angle Error (Pitch)",
                    Unit = "deg",
                    Color = "#FF9800",
                    RowIndex = 2,
                    ColumnIndex = 2,
                    Min = 0.003, Max = 0.010
                },
                new
                {
                    Name = "Ry",
                    Title = "Ry Angle Error (Yaw)",
                    Unit = "deg",
                    Color = "#9C27B0",
                    RowIndex = 4,
                    ColumnIndex = 0,
                    Min = 0.004, Max = 0.012
                },
                new
                {
                    Name = "Rz",
                    Title = "Rz Angle Error (Roll)",
                    Unit = "deg",
                    Color = "#00BCD4",
                    RowIndex = 4,
                    ColumnIndex = 2,
                    Min = 0.006, Max = 0.018
                }
            };

            var allData = new List<double>();

            foreach (var axis in axisConfigs)
            {
                var data = GenerateData(barCount, axis.Min, axis.Max, random);
                var bars = CreateBars(data, 100, axis.Color);
                var rms = CalculateRms(data);
                var std = CalculateStd(data);

                allData.AddRange(data);

                ChartCards.Add(new ChartCardViewModel(
                    axis.Title,
                    rms,
                    axis.Color,
                    bars,
                    $"{axis.Name} Error:",
                    $"{rms:F3} +/- {std:F3} {axis.Unit}",
                    axis.RowIndex,
                    axis.ColumnIndex,
                    axis.Unit,
                    new ObservableCollection<double>(data)
                ));
            }

            // Overall RMS (combined position errors)
            double overallRms = CalculateRms(allData);
            Overall_Rms = $"{overallRms:F3} mm RMS";

            if (ChartCards.Count > 0)
            {
                Selected2DChart = ChartCards[0];
            }
        }

        private List<double> GenerateData(int count, double min, double max, Random random)
        {
            var data = new List<double>();
            for (int i = 0; i < count; i++)
            {
                // Mix of sine wave and random noise
                double trend = Math.Sin(i * 0.2) * (max - min) / 2;
                double noise = (random.NextDouble() - 0.5) * (max - min) * 0.5;
                double value = Math.Abs((max + min) / 2 + trend + noise);
                value = Math.Max(min, Math.Min(max, value));
                data.Add(value);
            }
            return data;
        }

        private ObservableCollection<BarItem> CreateBars(List<double> data, double maxHeight, string color)
        {
            var bars = new ObservableCollection<BarItem>();
            double maxVal = data.Max();
            double minVal = data.Min();
            double range = maxVal - minVal;

            foreach (double value in data)
            {
                double normalized = (value - minVal) / (range + 0.001);
                double height = 20 + normalized * (maxHeight - 20);
                bars.Add(new BarItem(height, color));
            }
            return bars;
        }

        private double CalculateRms(List<double> data)
        {
            return Math.Sqrt(data.Average(v => v * v));
        }

        private double CalculateStd(List<double> data)
        {
            double avg = data.Average();
            double sumOfSquares = data.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sumOfSquares / data.Count);
        }

        [RelayCommand]
        private void ExportCsv()
        {
            try
            {
                string exportDir = Path.Combine(AppContext.BaseDirectory, "storage", "exports");
                Directory.CreateDirectory(exportDir);
                string path = Path.Combine(exportDir, $"analysis_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                _ = SaveFromGlobalMenuAsync(path);
                LogError($"[ExportCsv] Exported to {path}");
            }
            catch (Exception ex)
            {
                LogError($"[ExportCsv] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ExportJson()
        {
            // TODO: Implement JSON export
        }

        [RelayCommand]
        private void ExportPng()
        {
            // TODO: Implement PNG export
        }

        [RelayCommand]
        private void GenerateReport()
        {
            // TODO: Implement report generation
        }

        [RelayCommand]
        private void RunStageCalibration()
        {
            LogError("Run stage calibration requested (stub — connect 6-DoF batch pipeline here).");
        }

        /// <summary>
        /// Load pose estimation results into the Visualization tab
        /// </summary>
        public void LoadPoseEstimationResults(List<(double errorX, double errorY, double errorZ, double errorRx, double errorRy, double errorRz, double stageX, double stageY, double stageZ, double stageRx, double stageRy, double stageRz, double estX, double estY, double estZ, double estRx, double estRy, double estRz)> results)
        {
            // Ensure we're on the UI thread since we're modifying ObservableCollections
            if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                LogError($"[LoadPoseEstimationResults] Not on UI thread, dispatching with {results?.Count ?? 0} results");
                Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadPoseEstimationResults(results));
                return;
            }
            
            LogError($"[LoadPoseEstimationResults] *** UPDATING CHARTS *** with {results?.Count ?? 0} results");
            
            if (results == null || results.Count == 0)
            {
                LogError("[LoadPoseEstimationResults] No results to load");
                return;
            }

            _latestLoadedResults = results.ToList();

            // Clear any status message since we have data
            HasStatusMessage = false;
            StatusMessage = "";
            
            LogError($"[LoadPoseEstimationResults] Clearing {ChartCards.Count} existing charts, rebuilding with new data");
            ChartCards.Clear();

            // Create charts: 3 Stage positions + 6 DLL outputs = 9 total charts
            var chartConfigs = new[]
            {
                // Row 0: Stage Commanded Positions (X, Y) - Stage only exposes 3 axes
                new { Name = "StageX", Title = "Stage X Position (Commanded)", Unit = "µm", Color = "#E91E63", Row = 0, Col = 0, Data = results.Select(r => r.stageX).ToList() },
                new { Name = "StageY", Title = "Stage Y Position (Commanded)", Unit = "µm", Color = "#9C27B0", Row = 0, Col = 1, Data = results.Select(r => r.stageY).ToList() },
                
                // Row 1: Stage Commanded Position (Z only)
                new { Name = "StageZ", Title = "Stage Z Position (Commanded)", Unit = "µm", Color = "#673AB7", Row = 1, Col = 0, Data = results.Select(r => r.stageZ).ToList() },
                
                // Row 2: DLL Estimated Positions (Tx, Ty) - from DLL
                new { Name = "EstX", Title = "DLL Estimated Tx (X)", Unit = "mm", Color = "#4CAF50", Row = 2, Col = 0, Data = results.Select(r => r.estX).ToList() },
                new { Name = "EstY", Title = "DLL Estimated Ty (Y)", Unit = "mm", Color = "#8BC34A", Row = 2, Col = 1, Data = results.Select(r => r.estY).ToList() },
                
                // Row 3: DLL Estimated Position (Tz) and Rotation (Rx) - from DLL
                new { Name = "EstZ", Title = "DLL Estimated Tz (Z)", Unit = "mm", Color = "#CDDC39", Row = 3, Col = 0, Data = results.Select(r => r.estZ).ToList() },
                new { Name = "EstRx", Title = "DLL Estimated Rx (Pitch)", Unit = "rad", Color = "#FFEB3B", Row = 3, Col = 1, Data = results.Select(r => r.estRx).ToList() },
                
                // Row 4: DLL Estimated Rotations (Ry, Rz) - from DLL
                new { Name = "EstRy", Title = "DLL Estimated Ry (Yaw)", Unit = "rad", Color = "#FFC107", Row = 4, Col = 0, Data = results.Select(r => r.estRy).ToList() },
                new { Name = "EstRz", Title = "DLL Estimated Rz (Roll)", Unit = "rad", Color = "#FF9800", Row = 4, Col = 1, Data = results.Select(r => r.estRz).ToList() },
            };

            foreach (var chart in chartConfigs)
            {
                var bars = CreateBars(chart.Data, 100, chart.Color);
                var rms = CalculateRms(chart.Data);
                var std = CalculateStd(chart.Data);

                ChartCards.Add(new ChartCardViewModel(
                    chart.Title,
                    rms,
                    chart.Color,
                    bars,
                    $"{chart.Name}:",
                    $"{rms:F3} +/- {std:F3} {chart.Unit}",
                    chart.Row,
                    chart.Col,
                    chart.Unit,
                    new ObservableCollection<double>(chart.Data)
                ));
            }

            // Calculate RMS from position errors
            var posErrors = new List<double>();
            for (int i = 0; i < results.Count; i++)
            {
                posErrors.Add(Math.Abs(results[i].errorX));
                posErrors.Add(Math.Abs(results[i].errorY));
                posErrors.Add(Math.Abs(results[i].errorZ));
            }
            double overallRms = CalculateRms(posErrors);
            Overall_Rms = $"{overallRms:F3} µm RMS";
            
            LogError($"[LoadPoseEstimationResults] Overall RMS: {Overall_Rms}");

            // Generate mock 3D visualization (like original version)
            LogError("[LoadPoseEstimationResults] Generating 3D visualization");
            Generate3DData();

            if (ChartCards.Count > 0)
            {
                Selected2DChart = ChartCards[0];
                LogError($"[LoadPoseEstimationResults] Selected first chart: {ChartCards[0].Title}");
            }
            
            HasData = true;
            LogError($"[LoadPoseEstimationResults] COMPLETE - Loaded {results.Count} results, HasData={HasData}, Created {ChartCards.Count} charts");
        }

        /// <summary>
        /// Generate 3D visualization from real pose estimation error data
        /// </summary>
        private void Generate3DDataFromErrors(List<(double errorX, double errorY, double errorZ, double errorRx, double errorRy, double errorRz, double stageX, double stageY, double stageZ, double stageRx, double stageRy, double stageRz, double estX, double estY, double estZ, double estRx, double estRy, double estRz)> results)
        {
            var data = new Visualization3DData();

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                
                // Use actual stage positions
                double stageX = result.stageX;
                double stageY = result.stageY;
                double stageZ = result.stageZ;

                // Add point at stage position (green)
                data.Points.Add(new Point3D(stageX, stageY, stageZ, "#4CAF50", 4.0));
                
                // Add point at estimated position (orange)
                data.Points.Add(new Point3D(result.estX, result.estY, result.estZ, "#FF9800", 4.0));

                // Add error vector from stage to estimated position (red line)
                data.Lines.Add(new Line3D(
                    new Point3D(stageX, stageY, stageZ),
                    new Point3D(result.estX, result.estY, result.estZ),
                    "#FF5252", 2.0));
            }

            data.CalculateBounds();
            data.GenerateGrid(10);
            data.GenerateMeshFromScatteredPoints();
            Visualization3DData = data;
        }

        /// <summary>
        /// Show a status message when pose estimation fails
        /// </summary>
        public void ShowFailureStatus(int totalAttempts, int failedAttempts)
        {
            // Ensure we're on the UI thread
            if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowFailureStatus(totalAttempts, failedAttempts));
                return;
            }
            
            HasData = false;
            HasStatusMessage = true;
            StatusMessage = $"Pose estimation completed: {failedAttempts} of {totalAttempts} attempts failed.\n\n" +
                          "Possible causes:\n" +
                          "• Calibration pattern not visible or out of focus\n" +
                          "• Pattern too small or too large in frame\n" +
                          "• Poor lighting conditions\n" +
                          "• Pattern partially occluded";
            
            LogError($"[ShowFailureStatus] Displayed failure status: {failedAttempts}/{totalAttempts} failed");
        }

        [RelayCommand]
        private void ToggleFullScreen()
        {
            IsFullScreen = !IsFullScreen;
        }

        [RelayCommand]
        private void ResetView()
        {
            Generate3DData();
            LogError("3D view reset");
        }

        [RelayCommand]
        private void ShowSix2DPlots()
        {
            SelectedPlotLayout = 0;
        }

        [RelayCommand]
        private void ShowSingle3DPlot()
        {
            SelectedPlotLayout = 1;
        }

        [RelayCommand]
        private void Select2DChart(ChartCardViewModel? chart)
        {
            if (chart == null)
            {
                return;
            }

            Selected2DChart = chart;
        }

        [RelayCommand]
        private async void BrowsePreviewFolder()
        {
            try
            {
                var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select Pose Estimation CSV File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("CSV Files")
                        {
                            Patterns = new[] { "*.csv" }
                        }
                    }
                };

                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (topLevel != null)
                {
                    var storageProvider = topLevel.StorageProvider;
                    var result = await storageProvider.OpenFilePickerAsync(dialog);

                    if (result.Count > 0)
                    {
                        PreviewDataFolder = result[0].Path.LocalPath;
                        LogError($"[BrowsePreviewFolder] Selected: {PreviewDataFolder}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"[BrowsePreviewFolder] Error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void LoadPreviewData()
        {
            try
            {
                if (string.IsNullOrEmpty(PreviewDataFolder) || !System.IO.File.Exists(PreviewDataFolder))
                {
                    LogError("[LoadPreviewData] No valid CSV file selected");
                    return;
                }

                LogError($"[LoadPreviewData] Loading CSV: {PreviewDataFolder}");
                
                // Load session using the model
                _currentSession = Models.PoseEstimationSession.LoadFromCsv(PreviewDataFolder);
                
                if (_currentSession.SuccessfulResults > 0)
                {
                    LogError($"[LoadPreviewData] Loaded {_currentSession.SuccessfulResults} successful results from CSV");
                    LoadPoseEstimationResults(ConvertSessionResultsToAnalysisData(_currentSession.Results));
                    
                    // Update session info
                    CurrentSessionPath = PreviewDataFolder;
                    CurrentSessionName = _currentSession.SessionName;
                    CurrentSessionResultCount = _currentSession.SuccessfulResults;
                    
                    HasPreviewData = true;
                    PreviewImageCount = _currentSession.SuccessfulResults;
                    PreviewDataPoints = _currentSession.SuccessfulResults;
                    PreviewDate = _currentSession.SessionDate.ToString("yyyy-MM-dd HH:mm");
                    
                    LogError($"[LoadPreviewData] Session loaded: {CurrentSessionName} with {CurrentSessionResultCount} results");
                }
                else
                {
                    LogError("[LoadPreviewData] No valid results found in CSV");
                }
            }
            catch (Exception ex)
            {
                LogError($"[LoadPreviewData] Error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get image path for a specific step number
        /// </summary>
        public string? GetImagePathForStep(int stepNumber)
        {
            if (_currentSession == null) return null;
            
            var result = _currentSession.Results.FirstOrDefault(r => r.StepNumber == stepNumber);
            return result?.ImagePath;
        }
        
        /// <summary>
        /// Get full result data for a specific step number
        /// </summary>
        public Models.PoseEstimationResult? GetResultForStep(int stepNumber)
        {
            if (_currentSession == null) return null;
            
            return _currentSession.Results.FirstOrDefault(r => r.StepNumber == stepNumber);
        }

        private void LogError(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logMessage = $"[{timestamp}] {message}";

                // Prepend to error log string
                ErrorLog = logMessage + "\n" + ErrorLog;

                // Keep only last MaxErrorLogLines lines
                var lines = ErrorLog.Split('\n');
                if (lines.Length > MaxErrorLogLines)
                {
                    ErrorLog = string.Join("\n", lines.Take(MaxErrorLogLines));
                }

                Console.WriteLine($"[ErrorLog] {logMessage}");
            }
            catch
            {
                Console.WriteLine($"[ErrorLog] Failed to write error log: {message}");
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

        /// <summary>
        /// Save analysis data from global menu (stub for compatibility)
        /// </summary>
        public async Task SaveFromGlobalMenuAsync(string filePath)
        {
            await Task.Run(() =>
            {
                var results = BuildExportResults();
                if (results.Count == 0)
                {
                    LogError("[SaveFromGlobalMenu] No results available to export");
                    return;
                }

                WritePoseResultsCsv(filePath, results);
                LogError($"[SaveFromGlobalMenu] Exported {results.Count} rows to {filePath}");
            });
        }

        public async Task LoadFromGlobalMenuAsync(string filePath)
        {
            await Task.Run(() =>
            {
                if (!File.Exists(filePath))
                {
                    LogError($"[LoadFromGlobalMenu] File not found: {filePath}");
                    return;
                }

                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext != ".csv")
                {
                    LogError($"[LoadFromGlobalMenu] Unsupported format: {ext}. Please load CSV.");
                    return;
                }

                _currentSession = PoseEstimationSession.LoadFromCsv(filePath);
                var successful = _currentSession.Results.Where(r => r.Success).ToList();
                var data = ConvertSessionResultsToAnalysisData(successful);
                LoadPoseEstimationResults(data);

                CurrentSessionPath = filePath;
                CurrentSessionName = _currentSession.SessionName;
                CurrentSessionResultCount = successful.Count;
                HasPreviewData = true;
                PreviewImageCount = successful.Count;
                PreviewDataPoints = successful.Count;
                PreviewDate = _currentSession.SessionDate.ToString("yyyy-MM-dd HH:mm");
                LogError($"[LoadFromGlobalMenu] Loaded {successful.Count} rows from {filePath}");
            });
        }

        private List<Models.PoseEstimationResult> BuildExportResults()
        {
            if (_currentSession != null && _currentSession.Results.Count > 0)
                return _currentSession.Results.ToList();

            if (_latestLoadedResults.Count == 0)
                return new List<Models.PoseEstimationResult>();

            var results = new List<Models.PoseEstimationResult>(_latestLoadedResults.Count);
            int step = 1;
            foreach (var r in _latestLoadedResults)
            {
                results.Add(new Models.PoseEstimationResult
                {
                    StepNumber = step++,
                    Timestamp = DateTime.Now,
                    ImagePath = "",
                    StageX = r.stageX,
                    StageY = r.stageY,
                    StageZ = r.stageZ,
                    StageRx = r.stageRx,
                    StageRy = r.stageRy,
                    StageRz = r.stageRz,
                    EstimatedX = r.estX,
                    EstimatedY = r.estY,
                    EstimatedZ = r.estZ,
                    EstimatedRx = r.estRx,
                    EstimatedRy = r.estRy,
                    EstimatedRz = r.estRz,
                    ErrorX = r.errorX,
                    ErrorY = r.errorY,
                    ErrorZ = r.errorZ,
                    ErrorRx = r.errorRx,
                    ErrorRy = r.errorRy,
                    ErrorRz = r.errorRz,
                    Success = true,
                    ErrorMessage = ""
                });
            }

            return results;
        }

        private static List<(double errorX, double errorY, double errorZ, double errorRx, double errorRy, double errorRz, double stageX, double stageY, double stageZ, double stageRx, double stageRy, double stageRz, double estX, double estY, double estZ, double estRx, double estRy, double estRz)> ConvertSessionResultsToAnalysisData(IEnumerable<Models.PoseEstimationResult> results)
        {
            return results
                .Where(r => r.Success)
                .Select(r => (
                    r.ErrorX, r.ErrorY, r.ErrorZ,
                    r.ErrorRx, r.ErrorRy, r.ErrorRz,
                    r.StageX, r.StageY, r.StageZ,
                    r.StageRx, r.StageRy, r.StageRz,
                    r.EstimatedX, r.EstimatedY, r.EstimatedZ,
                    r.EstimatedRx, r.EstimatedRy, r.EstimatedRz))
                .ToList();
        }

        private static void WritePoseResultsCsv(string filePath, IReadOnlyList<Models.PoseEstimationResult> results)
        {
            var iv = CultureInfo.InvariantCulture;
            var lines = new List<string>(results.Count + 1)
            {
                "StepNumber,Timestamp,ImagePath,StageX,StageY,StageZ,StageRx,StageRy,StageRz,EstimatedX,EstimatedY,EstimatedZ,EstimatedRx,EstimatedRy,EstimatedRz,ErrorX,ErrorY,ErrorZ,ErrorRx,ErrorRy,ErrorRz,Success,ErrorMessage"
            };

            foreach (var r in results)
            {
                string img = $"\"{(r.ImagePath ?? string.Empty).Replace("\"", "\"\"")}\"";
                string msg = $"\"{(r.ErrorMessage ?? string.Empty).Replace("\"", "\"\"")}\"";
                lines.Add(string.Join(",",
                    r.StepNumber.ToString(iv),
                    r.Timestamp.ToString("O", iv),
                    img,
                    r.StageX.ToString("G17", iv),
                    r.StageY.ToString("G17", iv),
                    r.StageZ.ToString("G17", iv),
                    r.StageRx.ToString("G17", iv),
                    r.StageRy.ToString("G17", iv),
                    r.StageRz.ToString("G17", iv),
                    r.EstimatedX.ToString("G17", iv),
                    r.EstimatedY.ToString("G17", iv),
                    r.EstimatedZ.ToString("G17", iv),
                    r.EstimatedRx.ToString("G17", iv),
                    r.EstimatedRy.ToString("G17", iv),
                    r.EstimatedRz.ToString("G17", iv),
                    r.ErrorX.ToString("G17", iv),
                    r.ErrorY.ToString("G17", iv),
                    r.ErrorZ.ToString("G17", iv),
                    r.ErrorRx.ToString("G17", iv),
                    r.ErrorRy.ToString("G17", iv),
                    r.ErrorRz.ToString("G17", iv),
                    r.Success ? "True" : "False",
                    msg));
            }

            File.WriteAllLines(filePath, lines);
        }
    }
}
