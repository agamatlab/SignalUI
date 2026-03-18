using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace singalUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusBarText = "System Ready";

    [ObservableProperty]
    private string _activeTab = "camera";

    [ObservableProperty]
    private bool _stageConnected = true;

    [ObservableProperty]
    private bool _cameraReady = true;

    [ObservableProperty]
    private string _applicationVersion = "6-DOF Calibrator Pro v2.4.1";

    partial void OnActiveTabChanged(string value)
    {
        StatusBarText = value switch
        {
            "setup" => "Viewing Calibration Setup",
            "camera" => "Live Camera Feed Active - Adjusting Setup",
            "analysis" => "Data Visualization Mode",
            "config" => "Pose Estimation Configuration",
            _ => "System Ready"
        };
    }

    [RelayCommand]
    private void NavigateToSetup()
    {
        ActiveTab = "setup";
    }

    [RelayCommand]
    private void NavigateToCamera()
    {
        ActiveTab = "camera";
    }

    [RelayCommand]
    private void NavigateToAnalysis()
    {
        ActiveTab = "analysis";
    }
    
    [RelayCommand]
    private void NavigateToConfig()
    {
        ActiveTab = "config";
    }

    [RelayCommand]
    private void OpenConfig()
    {
        StatusBarText = "Opening configuration file...";
    }

    [RelayCommand]
    private void SaveConfig()
    {
        StatusBarText = "Saving configuration...";
    }
}
