using Avalonia.Controls;
using Avalonia.Input;
using singalUI.ViewModels;
using System;

namespace singalUI.Views;

public partial class CameraSetupView : UserControl
{
    public CameraSetupViewModel? ViewModel => DataContext as CameraSetupViewModel;
    
    private bool _isPanning = false;
    private Avalonia.Point _lastPanPoint;

    public CameraSetupView()
    {
        try
        {
            Console.WriteLine("[CameraSetupView] Constructor START");
            InitializeComponent();
            Console.WriteLine("[CameraSetupView] InitializeComponent completed");

            DataContext = new CameraSetupViewModel();
            LayoutUpdated += OnCameraViewLayoutUpdated;
            Console.WriteLine("[CameraSetupView] DataContext set, Constructor END");
            
            // Wire up mouse events for camera preview zoom/pan
            SetupCameraPreviewEvents();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CameraSetupView] ERROR: {ex.Message}");
            Console.WriteLine($"[CameraSetupView] Stack trace: {ex.StackTrace}");
            throw;
        }
    }
    
    private void SetupCameraPreviewEvents()
    {
        var previewViewport = this.FindControl<Border>("CameraPreviewViewport");
        if (previewViewport != null)
        {
            previewViewport.PointerWheelChanged += OnCameraPreviewWheel;
            previewViewport.PointerPressed += OnCameraPreviewPressed;
            previewViewport.PointerMoved += OnCameraPreviewMoved;
            previewViewport.PointerReleased += OnCameraPreviewReleased;
            Console.WriteLine("[CameraSetupView] Camera preview events wired up");
        }
        else
        {
            Console.WriteLine("[CameraSetupView] Warning: CameraPreviewViewport not found");
        }
    }
    
    private void OnCameraPreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel == null) return;
        
        // Zoom in/out with mouse wheel
        double zoomDelta = e.Delta.Y > 0 ? 0.1 : -0.1;
        double newZoom = Math.Clamp(ViewModel.ZoomLevel + zoomDelta, 1.0, 5.0);
        ViewModel.ZoomLevel = newZoom;

        if (newZoom <= 1.0)
        {
            ViewModel.PanX = 0;
            ViewModel.PanY = 0;
        }
        
        e.Handled = true;
    }
    
    private void OnCameraPreviewPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel == null) return;
        
        var props = e.GetCurrentPoint(sender as Control);
        bool allowLeftDragPan = props.Properties.IsLeftButtonPressed && ViewModel.ZoomLevel > 1.0;
        if (allowLeftDragPan || props.Properties.IsMiddleButtonPressed || props.Properties.IsRightButtonPressed)
        {
            _isPanning = true;
            _lastPanPoint = props.Position;
            e.Handled = true;
        }
    }
    
    private void OnCameraPreviewMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel == null || !_isPanning) return;
        
        var props = e.GetCurrentPoint(sender as Control);
        var currentPoint = props.Position;
        
        // Calculate pan delta
        double deltaX = currentPoint.X - _lastPanPoint.X;
        double deltaY = currentPoint.Y - _lastPanPoint.Y;
        
        // Update pan position
        ViewModel.PanX += deltaX;
        ViewModel.PanY += deltaY;
        
        _lastPanPoint = currentPoint;
        e.Handled = true;
    }
    
    private void OnCameraPreviewReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPanning = false;

        if (ViewModel != null && ViewModel.ZoomLevel <= 1.0)
        {
            ViewModel.PanX = 0;
            ViewModel.PanY = 0;
        }

        e.Handled = true;
    }

    private void OnCameraViewLayoutUpdated(object? sender, EventArgs e)
    {
        if (ViewModel == null) return;
        var tb = this.FindControl<TextBlock>("FocusLevelLabelMeasure");
        if (tb == null) return;
        double w = tb.Bounds.Width;
        if (w <= 0) return;
        double target = w * 2;
        if (System.Math.Abs(ViewModel.FocusMeterTrackWidth - target) > 0.5)
            ViewModel.FocusMeterTrackWidth = target;
    }
}
