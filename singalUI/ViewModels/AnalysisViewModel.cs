using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using singalUI.libs;
using singalUI.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace singalUI.ViewModels
{
    public partial class AnalysisViewModel : ViewModelBase
    {
        [ObservableProperty]
        private bool _showGrid = true;

        [ObservableProperty]
        private bool _showPoints = true;

        [ObservableProperty]
        private bool _showMesh = false;

        /// <summary>Extra polylines in <see cref="Visualization3DData.Lines"/> (rotation calib uses points only).</summary>
        [ObservableProperty]
        private bool _showLines3D = false;

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

        public AnalysisViewModel()
        {
            InitializeEmptyVisualization3D();
            GenerateMockData();
            LogError("AnalysisViewModel initialized - Error log ready");
        }

        /// <summary>Empty 3D scene (grid only) — no mock point cloud.</summary>
        private void InitializeEmptyVisualization3D()
        {
            var data = new Visualization3DData { Title = "3D Visualization" };
            data.MinX = data.MinY = data.MinZ = -1;
            data.MaxX = data.MaxY = data.MaxZ = 1;
            data.GenerateGrid(0.5);
            Visualization3DData = data;
            ShowMesh = false;
            ShowLines3D = false;
        }

        /// <summary>
        /// Replaces the 3D scene with rotation-calibration poses. Uses <paramref name="frames"/>[i].PoseAbs[3..5] as (x,y,z)
        /// (same 1×6 layout as in the configure error log: Rx,Ry,Rz then x,y,z). Clears lines and mesh.
        /// </summary>
        public void ApplyRotationCalibrationPoses3D(IReadOnlyList<NanoMeasFrameTrackResult> frames)
        {
            if (frames == null || frames.Count == 0)
                return;

            var data = new Visualization3DData
            {
                Title = "Rotation calib — pose_abs xyz (indices 3–5 of 1×6)",
            };

            string[] palette =
            {
                "#E91E63", "#9C27B0", "#673AB7", "#3F51B5", "#2196F3", "#00BCD4",
                "#009688", "#4CAF50", "#8BC34A", "#CDDC39", "#FF9800", "#FF5722",
            };

            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                if (f.PoseAbs == null || f.PoseAbs.Length < 6)
                    continue;

                double x = f.PoseAbs[3];
                double y = f.PoseAbs[4];
                double z = f.PoseAbs[5];
                string color = palette[i % palette.Length];
                data.Points.Add(new Point3D(x, y, z, color, 9.0) { Label = (i + 1).ToString() });
            }

            if (data.Points.Count == 0)
                return;

            data.CalculateBounds();
            double span = Math.Max(
                data.MaxX - data.MinX,
                Math.Max(data.MaxY - data.MinY, data.MaxZ - data.MinZ));
            double pad = span > 1e-15 ? span * 0.2 : 0.001;
            data.MinX -= pad;
            data.MaxX += pad;
            data.MinY -= pad;
            data.MaxY += pad;
            data.MinZ -= pad;
            data.MaxZ += pad;

            double spacing = span > 1e-15
                ? Math.Clamp(span / 6.0, 1e-6, Math.Max(span, 1e-6))
                : 0.001;
            data.GenerateGrid(spacing);

            Visualization3DData = data;
            ShowMesh = false;
            ShowLines3D = false;
            ShowPoints = true;
            ShowGrid = true;

            LogError($"Analysis 3D: {data.Points.Count} rotation-calibration points (x,y,z = pose_abs[3..5], same as configure log). Open Analysis tab to view.");
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
                    axis.ColumnIndex
                ));
            }

            // Overall RMS (combined position errors)
            double overallRms = CalculateRms(allData);
            Overall_Rms = $"{overallRms:F3} mm RMS";
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
            // TODO: Implement CSV export
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
        private void ToggleFullScreen()
        {
            IsFullScreen = !IsFullScreen;
        }

        [RelayCommand]
        private void ResetView()
        {
            InitializeEmptyVisualization3D();
            LogError("3D view cleared (grid only)");
        }

        [RelayCommand]
        private void BrowsePreviewFolder()
        {
            // TODO: Open folder dialog to select data folder
        }

        [RelayCommand]
        private void LoadPreviewData()
        {
            // TODO: Load preview data from selected folder
            HasPreviewData = true;
            PreviewImageCount = 42;
            PreviewDataPoints = 100;
            PreviewDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }

        private void LogError(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logMessage = $"[{timestamp}] {message}";

                ErrorLog = string.IsNullOrEmpty(ErrorLog) ? logMessage : $"{ErrorLog}\n{logMessage}";

                // Keep only last MaxErrorLogLines lines
                var lines = ErrorLog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > MaxErrorLogLines)
                {
                    ErrorLog = string.Join("\n", lines.Skip(lines.Length - MaxErrorLogLines));
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
    }
}
