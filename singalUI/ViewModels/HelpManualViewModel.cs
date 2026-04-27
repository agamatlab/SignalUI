using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace singalUI.ViewModels;

public partial class HelpManualViewModel : ViewModelBase
{
    public ObservableCollection<HelpManualSection> Sections { get; } = new();

    [ObservableProperty]
    private HelpManualSection? _selectedSection;

    public HelpManualViewModel()
    {
        Sections.Add(new HelpManualSection(
            "Overview",
            "NanoMeas Calibrator guides you through camera setup, stage calibration, and visualization of pose results. Use the Camera, Stage, and Visualization tabs in order (chevrons in the tab bar hint the workflow)."));
        Sections.Add(new HelpManualSection(
            "Save / Open",
            "SAVE exports the current visualization or calculation results (CSV, JSON, PNG, MATLAB, etc.). OPEN loads a previously saved CSV or an image captured elsewhere for plotting on the Visualization tab."));
        Sections.Add(new HelpManualSection(
            "Camera tab",
            "Focus Level (0–100%) = 50%·L₁₀₀ + 50%·F₁₀₀: L₁₀₀ is Laplacian variance (3×3 kernel) on the center 60% ROI after half-size downscale, log-mapped to 0–100; F₁₀₀ is mean FFT magnitude in the high-frequency ring (spectrum radius beyond 60% of the half-size), log-mapped to 0–100. The preview shows a live image for framing, focus, and zoom/pan. The pose strip shows latest DLL output: X/Y/Z in millimeters and Rx/Ry/Rz in degrees."));
        Sections.Add(new HelpManualSection(
            "Stage tab",
            "Sampling modes: Manual (single sample), Triggered (sample on new frame with delay), Timed (sample on an interval), and Control (move stages by configured steps). Default is Control for motion workflows. Add stage controllers, select hardware and COM, Connect, verify status, then run your sequence."));
        Sections.Add(new HelpManualSection(
            "Visualization tab",
            "Use 2D for per-axis trend cards or 3D for spatial point/mesh views. Grid, Points, and Mesh toggles control rendering. OPEN/SAVE in the menu bar apply to data in this tab."));
        Sections.Add(new HelpManualSection(
            "Camera parameters",
            "Intrinsics, pattern pitch, and algorithm settings used by pose estimation can be adjusted from the configuration workflow (see project documentation for default values and logs)."));

        SelectedSection = Sections.Count > 0 ? Sections[0] : null;
    }
}

public sealed class HelpManualSection
{
    public string Title { get; }
    public string Body { get; }

    public HelpManualSection(string title, string body)
    {
        Title = title;
        Body = body;
    }
}
