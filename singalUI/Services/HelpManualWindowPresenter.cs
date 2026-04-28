using System;
using Avalonia;
using Avalonia.Controls;
using singalUI.ViewModels;
using singalUI.Views;

namespace singalUI.Services;

/// <summary>Shows the help manual window to the right of the main window and keeps it aligned when the owner moves or resizes.</summary>
public static class HelpManualWindowPresenter
{
    private static HelpManualWindow? _window;
    private static Window? _owner;

    private const int GapPx = 8;

    public static void Attach(Window mainWindow)
    {
        _owner = mainWindow;
        mainWindow.PositionChanged += OnOwnerBoundsChanged;
        mainWindow.Resized += OnOwnerBoundsChanged;
        mainWindow.Closing += (_, _) => Destroy();
    }

    private static void OnOwnerBoundsChanged(object? sender, EventArgs e)
    {
        if (_window != null && _window.IsVisible)
            ApplyPlacement();
    }

    public static void SetVisible(bool visible)
    {
        if (_owner == null)
            return;

        if (visible)
        {
            if (_window == null)
            {
                _window = new HelpManualWindow
                {
                    DataContext = new HelpManualViewModel(),
                };
                _window.Opened += (_, _) => ApplyPlacement();
            }

            // Position before first layout so the window does not flash at a default location.
            ApplyPlacement();
            _window.Show(_owner);
            ApplyPlacement();
        }
        else
        {
            _window?.Hide();
        }
    }

    public static void Hide()
    {
        _window?.Hide();
    }

    public static void Destroy()
    {
        if (_window != null)
        {
            try
            {
                _window.Close();
            }
            catch
            {
                /* ignore */
            }

            _window = null;
        }

        _owner = null;
    }

    /// <summary>Dock the help window to the right edge of the main window (works even when the help window is not yet visible).</summary>
    private static void ApplyPlacement()
    {
        if (_owner == null || _window == null)
            return;

        // Don't try to position if owner is in fullscreen mode - causes crashes on macOS
        if (_owner.WindowState == WindowState.FullScreen)
            return;

        try
        {
            _window.WindowStartupLocation = WindowStartupLocation.Manual;

            var x = _owner.Position.X + (int)Math.Round(_owner.Bounds.Width) + GapPx;
            var y = _owner.Position.Y;
            _window.Position = new PixelPoint(x, y);

            var h = _owner.Bounds.Height;
            if (h > 100 && Math.Abs(_window.Height - h) > 1)
                _window.Height = h;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HelpManualWindowPresenter] Error positioning window: {ex.Message}");
        }
    }
}
