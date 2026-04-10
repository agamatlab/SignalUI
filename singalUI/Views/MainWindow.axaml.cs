using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using singalUI.ViewModels;
using System;

namespace singalUI.Views;

public partial class MainWindow : Window
{
    private Carousel? _carousel;
    private readonly DispatcherTimer _tabChevronTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1000.0 / 120.0),
    };
    private DateTime _tabChevronAnimStartUtc = DateTime.UtcNow;
    private const double TabChevronCycleSeconds = 2.6;
    private const double TabChevronSweepWidth = 22.0;
    private const double TabChevronNaturalHeight = 50.0;
    private const double TabChevronClipOvershootPx = 10.0;
    private const double TabChevronVerticalBiasPx = 4.0;
    private WindowState _previousWindowState = WindowState.Normal;

    private static double EaseInOutCubic(double u)
    {
        u = Math.Clamp(u, 0.0, 1.0);
        return u < 0.5 ? 4.0 * u * u * u : 1.0 - Math.Pow(-2.0 * u + 2.0, 3) / 2.0;
    }

    public MainWindow()
    {
        try
        {
            Console.WriteLine("[MainWindow] Constructor START");
            InitializeComponent();
            
            // Add F11 key handler for fullscreen toggle
            this.KeyDown += OnKeyDown;
            
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
            _tabChevronAnimStartUtc = DateTime.UtcNow;
            _tabChevronTimer.Tick += (_, _) => UpdateTabChevronAnimation();
            _tabChevronTimer.Start();
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
        }

        if (activeTab != null)
        {
            activeTab.Background = new SolidColorBrush(Color.Parse("#0d0d0d")); // Match content background
            activeTab.CornerRadius = new Avalonia.CornerRadius(0, 0, 6, 6); // Rounded bottom corners
        }
    }

    /// <summary>
    /// Continuous L→R chevron over the tab strip (non-interactive hint for Camera → … → Visualization).
    /// </summary>
    private void UpdateTabChevronAnimation()
    {
        var host = this.FindControl<Grid>("TabBarContainer");
        var path = this.FindControl<Path>("TabChevronPath");
        if (host == null || path == null)
            return;

        double w = host.Bounds.Width;
        double h = host.Bounds.Height;
        if (w < 1 || h < 1)
            return;

        double u = (DateTime.UtcNow - _tabChevronAnimStartUtc).TotalSeconds % TabChevronCycleSeconds / TabChevronCycleSeconds;
        double t = EaseInOutCubic(u);
        double x = -TabChevronSweepWidth + t * (w + TabChevronSweepWidth);
        double scale = (h + TabChevronClipOvershootPx) / TabChevronNaturalHeight;
        path.RenderTransform = new ScaleTransform(scale, scale);
        Canvas.SetLeft(path, x);
        Canvas.SetTop(path, (h - TabChevronNaturalHeight * scale) / 2.0 + TabChevronVerticalBiasPx);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // F11 toggles fullscreen mode
        if (e.Key == Key.F11)
        {
            if (WindowState == WindowState.FullScreen)
            {
                // Exit fullscreen - restore to previous state
                WindowState = _previousWindowState;
            }
            else
            {
                // Enter fullscreen - save current state
                _previousWindowState = WindowState;
                WindowState = WindowState.FullScreen;
            }
            e.Handled = true;
        }
    }
}
