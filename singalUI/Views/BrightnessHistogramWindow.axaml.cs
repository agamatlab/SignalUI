using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace singalUI.Views;

public partial class BrightnessHistogramWindow : Window
{
    private int[]? _counts;
    private int _pixelTotal;

    private static readonly Color BarColor = Color.Parse("#5fcf65");
    private static readonly Color BarDimColor = Color.Parse("#3a5c42");

    public BrightnessHistogramWindow()
    {
        InitializeComponent();
        ChartBorder.SizeChanged += (_, _) => RedrawChart();
        Opened += (_, _) => RedrawChart();
    }

    public void SetHistogram(int[] counts, int pixelTotal)
    {
        _counts = new int[256];
        if (counts != null)
        {
            int n = Math.Min(256, counts.Length);
            for (int i = 0; i < n; i++)
                _counts[i] = counts[i];
        }
        _pixelTotal = pixelTotal;

        PixelCountText.Text = pixelTotal > 0
            ? $"{pixelTotal:N0} pixels · current frame"
            : "No frame — connect camera and start acquisition";

        RedrawChart();
    }

    private void RedrawChart()
    {
        if (_counts == null || !IsLoaded)
            return;

        HistogramCanvas.Children.Clear();

        double cw = ChartBorder.Bounds.Width - ChartBorder.Padding.Left - ChartBorder.Padding.Right;
        double ch = ChartBorder.Bounds.Height - ChartBorder.Padding.Top - ChartBorder.Padding.Bottom;
        if (cw < 32 || ch < 32)
            return;

        HistogramCanvas.Width = cw;
        HistogramCanvas.Height = ch;

        int max = _counts.Max();
        if (max < 1)
            max = 1;

        double barW = cw / 256.0;
        var fillBrush = new SolidColorBrush(BarColor);
        var dimBrush = new SolidColorBrush(BarDimColor);

        for (int i = 0; i < 256; i++)
        {
            double bh = ch * (_counts[i] / (double)max);
            if (_counts[i] > 0 && bh < 1)
                bh = 1;

            var rect = new Rectangle
            {
                Width = Math.Max(barW - 0.35, 0.4),
                Height = bh,
                Fill = _pixelTotal > 0 ? fillBrush : dimBrush,
                Opacity = _pixelTotal > 0 ? 0.9 : 0.35
            };
            Canvas.SetLeft(rect, i * barW);
            Canvas.SetTop(rect, ch - bh);
            HistogramCanvas.Children.Add(rect);
        }
    }
}
