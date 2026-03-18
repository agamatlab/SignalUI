using CommunityToolkit.Mvvm.ComponentModel;

namespace singalUI.Services;

public partial class SharedConfigParameters : ObservableObject
{
    [ObservableProperty] private int _selectedPatternType;
    [ObservableProperty] private double _pitchX = 1.2;
    [ObservableProperty] private double _pitchY = 1.2;
    [ObservableProperty] private double _componentsX = 11.0;
    [ObservableProperty] private double _componentsY = 11.0;
    [ObservableProperty] private double _focalLength = 16.0;
    [ObservableProperty] private double _pixelSize = 0.0055;
    [ObservableProperty] private double _fx = 16.0 / 0.0055;
    [ObservableProperty] private double _fy = 16.0 / 0.0055;
    [ObservableProperty] private double _cx = 320.0;
    [ObservableProperty] private double _cy = 240.0;
    [ObservableProperty] private double _windowSize = 5.0;
    [ObservableProperty] private double _codePitchBlocks = 4.0;
    [ObservableProperty] private int _imageWidth = 640;
    [ObservableProperty] private int _imageHeight = 480;

    public string PatternTypeLabel => SelectedPatternType switch
    {
        0 => "Checker Board (Calibration)",
        1 => "Dot Grid (HMF Coded Target)",
        2 => "Circular Grid",
        3 => "Concentric Circles",
        4 => "Asymmetric Circles",
        _ => $"Unknown ({SelectedPatternType})"
    };

    partial void OnSelectedPatternTypeChanged(int value)
    {
        OnPropertyChanged(nameof(PatternTypeLabel));
    }
}

public static class SharedConfigParametersStore
{
    public static SharedConfigParameters Instance { get; } = new();
}
