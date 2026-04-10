using System;
using System.Drawing;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace singalUI.libs;

public partial class StageControllerWrapper : ObservableObject
{
    public StageController Controller;
    
    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private string[]? _axis = null;
    
    StageControllerWrapper(StageController controller)
    {
        Controller = controller;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (s, e) => UpdateStageStatus();
        timer.Start();
    }

    private void UpdateStageStatus()
    {
        IsConnected = Controller.CheckConnected();
        if (Axis == null && IsConnected)
        {
            Axis = Controller.GetAxesList();
        }
    }
}