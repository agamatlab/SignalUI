using Avalonia.Controls;
using singalUI.ViewModels;
using System;

namespace singalUI.Views;

public partial class CalibrationSetupView : UserControl
{
    public CalibrationSetupView()
    {
        try
        {
            Console.WriteLine("[CalibrationSetupView] Constructor START");
            InitializeComponent();
            DataContext = new CalibrationSetupViewModel();
            Console.WriteLine("[CalibrationSetupView] Constructor END");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalibrationSetupView] ERROR: {ex.Message}");
            Console.WriteLine($"[CalibrationSetupView] Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}
