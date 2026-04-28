using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using singalUI.ViewModels;
using singalUI.Views;
using singalUI.Models;
using singalUI.Services;

namespace singalUI;

public partial class App : Application
{
    // Using StageManager instead of GrpcStageService for direct USB/serial stage control
    public static MatrixVisionCameraService CameraService { get; } = new();
    public static ConfigViewModel SharedConfigViewModel { get; } = new();

    public static AnalysisViewModel SharedAnalysisViewModel { get; } = new();

    private static CalibrationAppMode _calibrationAppMode = CalibrationAppMode.SixDofStage;

    /// <summary>Global calibration mode (6-DoF vs rotation-stage batch pipeline).</summary>
    public static CalibrationAppMode CalibrationAppMode
    {
        get => _calibrationAppMode;
        set
        {
            if (_calibrationAppMode == value) return;
            _calibrationAppMode = value;
            CalibrationAppModeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static event EventHandler? CalibrationAppModeChanged;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Console.WriteLine("[App] OnFrameworkInitializationCompleted START");

        // Initialize plugin system in background (non-blocking)
        Console.WriteLine("[App] Starting plugin system initialization (async)...");
        Task.Run(() =>
        {
            try
            {
                StageControllerFactory.InitializePlugins();
                Console.WriteLine("[App] Plugin system initialized");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Plugin initialization error: {ex.Message}");
            }
        });

        // Initialize camera service in background (non-blocking)
        Task.Run(() =>
        {
            try
            {
                // Also log to file for Rider debugging
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "singalUI", "camera_log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, $"=== Camera Log Started {DateTime.Now:O} ===\n");

                bool connected = CameraService.Initialize();
                File.AppendAllText(logPath,
                    $"Camera connected: {connected}\n" +
                    $"moduleBase: {CameraService.LastInitModuleBase}\n" +
                    $"mvGenTLProducer.cti found: {CameraService.LastInitCtiFound}\n" +
                    $"deviceCount (after updateDeviceList): {CameraService.LastInitDeviceCount}\n");

                if (connected)
                {
                    Console.WriteLine("[App] Camera connected, starting acquisition...");
                    File.AppendAllText(logPath, "Starting acquisition...\n");
                    CameraService.StartAcquisition();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Camera service initialization failed: {ex.Message}");
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "singalUI", "camera_error.txt");
                File.WriteAllText(logPath, $"{DateTime.Now:O}: {ex.Message}\n{ex.StackTrace}\n");
            }
        });

        // Initialize static StageManager with default stages
        StageManager.InitializeDefaultStages();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Console.WriteLine("[App] Creating MainWindow...");

            desktop.Exit += (_, __) =>
            {
                try
                {
                    libs.NanoMeasCalibNative.TryShutdown();
                    // Disconnect all stages before exit
                    StageManager.DisconnectAll();
                    CameraService?.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[App] Disposal error: {ex.Message}");
                }
            };

            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
            Console.WriteLine("[App] MainWindow created and assigned");
        }

        base.OnFrameworkInitializationCompleted();
        Console.WriteLine("[App] OnFrameworkInitializationCompleted END");
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}