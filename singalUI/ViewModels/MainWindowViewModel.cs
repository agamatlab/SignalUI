using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace singalUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _activeTab = "camera";

    [ObservableProperty]
    private bool _stageConnected = true;

    [ObservableProperty]
    private bool _cameraReady = true;

    [ObservableProperty]
    private string _applicationVersion = "NanoMeas Calibrator 0.8.0";

    [ObservableProperty]
    private string _statusBarText = "System Ready";

    private static TopLevel? ShellTopLevel() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow
            : null;

    // Session Name from shared store
    public string SessionName
    {
        get => singalUI.Services.SharedConfigParametersStore.Instance.SessionName;
        set
        {
            if (singalUI.Services.SharedConfigParametersStore.Instance.SessionName != value)
            {
                singalUI.Services.SharedConfigParametersStore.Instance.SessionName = value;
                OnPropertyChanged();
            }
        }
    }

    partial void OnActiveTabChanged(string value)
    {
        StatusBarText = value switch
        {
            "setup" => "Viewing Calibration Setup",
            "camera" => "Live Camera Feed Active - Adjusting Setup",
            "analysis" => "Visualization — plots and 3D view",
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
    private async Task GlobalSaveAsync()
    {
        var top = ShellTopLevel();
        if (top == null)
            return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save analysis / calculation result",
            DefaultExtension = "csv",
            SuggestedFileName = "nanomeas_result.csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
                new FilePickerFileType("PNG plot") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("MATLAB") { Patterns = new[] { "*.mat" } },
                FilePickerFileTypes.All,
            },
        });

        if (file != null)
            await singalUI.App.SharedAnalysisViewModel.SaveFromGlobalMenuAsync(file.Path.LocalPath);
    }

    [RelayCommand]
    private async Task GlobalLoadAsync()
    {
        var top = ShellTopLevel();
        if (top == null)
            return;

        var picks = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load image or calculation result",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
                new FilePickerFileType("MATLAB") { Patterns = new[] { "*.mat" } },
                FilePickerFileTypes.All,
            },
        });

        if (picks.Count > 0)
            await singalUI.App.SharedAnalysisViewModel.LoadFromGlobalMenuAsync(picks[0].Path.LocalPath);
    }
}
