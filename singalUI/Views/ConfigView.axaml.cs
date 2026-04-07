using Avalonia.Controls;
using singalUI.ViewModels;
using System;

namespace singalUI.Views;

public partial class ConfigView : UserControl
{
    public ConfigView()
    {
        try
        {
            Console.WriteLine("[ConfigView] Constructor START");
            InitializeComponent();
            DataContext = App.SharedConfigViewModel;
            Console.WriteLine("[ConfigView] Constructor END");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigView] ERROR: {ex.Message}");
            Console.WriteLine($"[ConfigView] Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}
