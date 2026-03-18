using Avalonia.Controls;
using singalUI.ViewModels;
using System;

namespace singalUI.Views;

public partial class AnalysisView : UserControl
{
    public AnalysisView()
    {
        try
        {
            Console.WriteLine("[AnalysisView] Constructor START");
            InitializeComponent();
            DataContext = new AnalysisViewModel();
            Console.WriteLine("[AnalysisView] Constructor END");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnalysisView] ERROR: {ex.Message}");
            Console.WriteLine($"[AnalysisView] Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}
