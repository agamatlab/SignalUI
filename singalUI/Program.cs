using Avalonia;
using Avalonia.Diagnostics;
using System;
using HotAvalonia;
namespace singalUI;
using libs;

sealed class Program
{
    // Stage controller instance - now managed by CalibrationSetupViewModel
    // This is kept for backward compatibility but should not be used directly
    [Obsolete("Use CalibrationSetupViewModel.CurrentStageController instead")]
    public static StageController? CurrentStageController { get; set; } = null;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
            // .UseHotReload() // Temporarily disabled for Windows compatibility testing
}