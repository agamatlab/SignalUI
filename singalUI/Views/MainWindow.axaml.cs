using Avalonia.Controls;
using Avalonia.Media;
using singalUI.ViewModels;
using System;

namespace singalUI.Views;

public partial class MainWindow : Window
{
    private Carousel? _carousel;

    public MainWindow()
    {
        try
        {
            Console.WriteLine("[MainWindow] Constructor START");
            InitializeComponent();
            Console.WriteLine("[MainWindow] InitializeComponent completed");

            // DataContext is already set in App.axaml.cs, don't set it again
            Console.WriteLine($"[MainWindow] DataContext type: {DataContext?.GetType().Name ?? "null"}");

            // Find carousel and set default
            _carousel = this.FindControl<Carousel>("MainCarousel");
            if (_carousel != null)
            {
                _carousel.SelectedIndex = 0; // Start with Camera view
                Console.WriteLine("[MainWindow] Carousel found, SelectedIndex set to 0");
            }
            else
            {
                Console.WriteLine("[MainWindow] WARNING: Carousel not found!");
            }

            // Update tab highlighting
            UpdateTabStates(0);
            Console.WriteLine("[MainWindow] Constructor END");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindow] ERROR in constructor: {ex.Message}");
            Console.WriteLine($"[MainWindow] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private void NavigateToCamera(object sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        SetActiveView(0, "camera");
    }

    private void NavigateToSetup(object sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        SetActiveView(1, "setup");
    }

    private void NavigateToAnalysis(object sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        SetActiveView(2, "analysis");
    }

    private void NavigateToConfig(object sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        SetActiveView(3, "config");
    }
    
    private void SetActiveView(int index, string viewName)
    {
        // Update carousel
        if (_carousel != null)
        {
            _carousel.SelectedIndex = index;
        }

        // Update ViewModel
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ActiveTab = viewName;
        }

        // Update tab states
        UpdateTabStates(index);
    }

    private void UpdateTabStates(int activeIndex)
    {
        var cameraTab = this.FindControl<Border>("CameraTab");
        var setupTab = this.FindControl<Border>("SetupTab");
        var analysisTab = this.FindControl<Border>("AnalysisTab");
        var configTab = this.FindControl<Border>("ConfigTab");

        // Reset all tabs to default state
        if (cameraTab != null)
        {
            cameraTab.Background = Brushes.Transparent;
            cameraTab.CornerRadius = new Avalonia.CornerRadius(0);
        }
        if (setupTab != null)
        {
            setupTab.Background = Brushes.Transparent;
            setupTab.CornerRadius = new Avalonia.CornerRadius(0);
        }
        if (analysisTab != null)
        {
            analysisTab.Background = Brushes.Transparent;
            analysisTab.CornerRadius = new Avalonia.CornerRadius(0);
        }
        if (configTab != null)
        {
            configTab.Background = Brushes.Transparent;
            configTab.CornerRadius = new Avalonia.CornerRadius(0);
        }

        // Set active tab with rounded bottom corners (Microsoft Word style)
        Border? activeTab = null;
        switch (activeIndex)
        {
            case 0:
                activeTab = cameraTab;
                break;
            case 1:
                activeTab = setupTab;
                break;
            case 2:
                activeTab = analysisTab;
                break;
            case 3:
                activeTab = configTab;
                break;
        }

        if (activeTab != null)
        {
            activeTab.Background = new SolidColorBrush(Color.Parse("#0d0d0d")); // Match content background
            activeTab.CornerRadius = new Avalonia.CornerRadius(0, 0, 6, 6); // Rounded bottom corners
        }
    }
}
