using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using singalUI.ViewModels;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace singalUI.Views
{
    public partial class ChartCardView : UserControl
    {
        private ChartCardViewModel? _subscribedViewModel;

        public static readonly StyledProperty<ICommand?> CommandProperty =
            AvaloniaProperty.Register<ChartCardView, ICommand?>(nameof(Command));

        public static readonly StyledProperty<object?> CommandParameterProperty =
            AvaloniaProperty.Register<ChartCardView, object?>(nameof(CommandParameter));

        public ICommand? Command
        {
            get => GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public object? CommandParameter
        {
            get => GetValue(CommandParameterProperty);
            set => SetValue(CommandParameterProperty, value);
        }

        public ChartCardView()
        {
            InitializeComponent();
            ClipToBounds = true;
            DataContextChanged += OnDataContextChanged;
            PointerPressed += OnPointerPressed;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Unsubscribe(_subscribedViewModel);
            base.OnDetachedFromVisualTree(e);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var vm = DataContext as ChartCardViewModel;
            if (vm == null || Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return;
            }

            var outer = new Rect(Bounds.Size);
            var panelBrush = new SolidColorBrush(Color.Parse("#171717"));
            var borderColor = vm.IsSelected ? "#2d8cff" : "#343434";
            var borderThickness = vm.IsSelected ? 2.5 : 1.0;
            var panelBorder = new Pen(new SolidColorBrush(Color.Parse(borderColor)), borderThickness);
            context.DrawRectangle(panelBrush, panelBorder, outer, 8, 8);

            var titleBrush = new SolidColorBrush(Color.Parse("#f5f5f5"));
            var dimBrush = new SolidColorBrush(Color.Parse("#9a9a9a"));
            var valueBrush = new SolidColorBrush(Color.Parse(vm.Color));
            var gridPen = new Pen(new SolidColorBrush(Color.Parse("#2c2c2c")), 1);
            var axisPen = new Pen(new SolidColorBrush(Color.Parse("#555555")), 1);
            var seriesPen = new Pen(new SolidColorBrush(Color.Parse(vm.Color)), 2.2);

            const double headerY = 6;
            var titleText = CreateText(vm.Title.ToUpperInvariant(), 12, FontWeight.Bold, titleBrush);
            var rmsText = CreateText($"RMS {vm.RmsValue:F3} {vm.Unit}".Trim(), 10, FontWeight.SemiBold, valueBrush);
            context.DrawText(titleText, new Point(8, headerY));
            context.DrawText(rmsText, new Point(Math.Max(8, Bounds.Width - rmsText.Width - 8), headerY + 1));

            var series = vm.SeriesValues;
            if (series == null || series.Count == 0)
            {
                var emptyText = CreateText("No plot data", 12, FontWeight.Normal, dimBrush);
                context.DrawText(emptyText, new Point(16, 80));
                return;
            }

            double left = 52;
            double top = 28;
            double right = 10;
            double bottom = 28;
            var plotRect = new Rect(left, top, Math.Max(80, Bounds.Width - left - right), Math.Max(80, Bounds.Height - top - bottom));

            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#202020")), 1), plotRect);

            double min = series.Min();
            double max = series.Max();
            if (Math.Abs(max - min) < 0.000001)
            {
                max = min + 1;
            }

            for (int i = 0; i < 4; i++)
            {
                double t = i / 3.0;
                double y = plotRect.Bottom - t * plotRect.Height;
                context.DrawLine(gridPen, new Point(plotRect.Left, y), new Point(plotRect.Right, y));

                var tickValue = min + ((max - min) * t);
                var tickLabel = tickValue.ToString("F2", CultureInfo.CurrentCulture);
                var tickText = CreateText(tickLabel, 9, FontWeight.Normal, dimBrush);
                context.DrawText(tickText, new Point(Math.Max(4, plotRect.Left - tickText.Width - 6), y - (tickText.Height / 2)));
            }

            context.DrawLine(axisPen, new Point(plotRect.Left, plotRect.Top), new Point(plotRect.Left, plotRect.Bottom));
            context.DrawLine(axisPen, new Point(plotRect.Left, plotRect.Bottom), new Point(plotRect.Right, plotRect.Bottom));

            var geometry = new StreamGeometry();
            using (var geo = geometry.Open())
            {
                for (int i = 0; i < series.Count; i++)
                {
                    double x = plotRect.Left + (plotRect.Width * i / Math.Max(1, series.Count - 1));
                    double normalized = (series[i] - min) / (max - min);
                    double y = plotRect.Bottom - normalized * plotRect.Height;
                    var point = new Point(x, y);

                    if (i == 0)
                    {
                        geo.BeginFigure(point, false);
                    }
                    else
                    {
                        geo.LineTo(point);
                    }
                }
            }

            context.DrawGeometry(null, seriesPen, geometry);

            int markerStep = Math.Max(1, series.Count / 8);
            for (int i = 0; i < series.Count; i += markerStep)
            {
                double x = plotRect.Left + (plotRect.Width * i / Math.Max(1, series.Count - 1));
                double normalized = (series[i] - min) / (max - min);
                double y = plotRect.Bottom - normalized * plotRect.Height;
                context.DrawEllipse(new SolidColorBrush(Color.Parse("#0f0f0f")), seriesPen, new Point(x, y), 3, 3);
            }

            var unitText = CreateText(vm.Unit, 9, FontWeight.Normal, dimBrush);
            context.DrawText(unitText, new Point(Math.Max(4, plotRect.Left - unitText.Width - 6), Math.Max(4, plotRect.Top - unitText.Height - 2)));

            if (series.Count > 1)
            {
                var midIndex = ((series.Count - 1) / 2.0) + 1;
                DrawXAxisLabel(context, "1", plotRect.Left, plotRect.Bottom + 4, dimBrush);
                DrawXAxisLabel(context, ((int)Math.Round(midIndex)).ToString(CultureInfo.CurrentCulture), plotRect.Left + (plotRect.Width / 2), plotRect.Bottom + 4, dimBrush);
                DrawXAxisLabel(context, series.Count.ToString(CultureInfo.CurrentCulture), plotRect.Right, plotRect.Bottom + 4, dimBrush);
            }
            else
            {
                DrawXAxisLabel(context, "1", plotRect.Left, plotRect.Bottom + 4, dimBrush);
            }

        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            Unsubscribe(_subscribedViewModel);
            _subscribedViewModel = DataContext as ChartCardViewModel;
            Subscribe(_subscribedViewModel);
            InvalidateVisual();
        }

        private void Subscribe(ChartCardViewModel? vm)
        {
            if (vm == null)
            {
                return;
            }

            vm.PropertyChanged += OnViewModelChanged;
            vm.SeriesValues.CollectionChanged += OnSeriesChanged;
        }

        private void Unsubscribe(ChartCardViewModel? vm)
        {
            if (vm == null)
            {
                return;
            }

            vm.PropertyChanged -= OnViewModelChanged;
            vm.SeriesValues.CollectionChanged -= OnSeriesChanged;
        }

        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            InvalidateVisual();
        }

        private void OnSeriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            InvalidateVisual();
        }

        private static FormattedText CreateText(string text, double size, FontWeight weight, IBrush brush)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"),
                size,
                brush);
            formatted.SetFontWeight(weight);
            return formatted;
        }

        private static void DrawXAxisLabel(DrawingContext context, string text, double centerX, double topY, IBrush brush)
        {
            var formatted = CreateText(text, 9, FontWeight.Normal, brush);
            context.DrawText(formatted, new Point(centerX - (formatted.Width / 2), topY));
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                Command?.CanExecute(CommandParameter) == true)
            {
                Command.Execute(CommandParameter);
                e.Handled = true;
            }
        }
    }
}
