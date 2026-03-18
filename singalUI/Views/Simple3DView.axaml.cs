using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using singalUI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace singalUI.Views
{
    public partial class Simple3DView : UserControl
    {
        private Simple3DProjection _projection = new();
        private Point _lastMousePosition;
        private bool _isRotating = false;
        private bool _isPanning = false;

        public static readonly StyledProperty<Visualization3DData?> DataProperty =
            AvaloniaProperty.Register<Simple3DView, Visualization3DData?>(nameof(Data));

        public Visualization3DData? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly StyledProperty<bool> ShowGridProperty =
            AvaloniaProperty.Register<Simple3DView, bool>(nameof(ShowGrid), true);

        public bool ShowGrid
        {
            get => GetValue(ShowGridProperty);
            set => SetValue(ShowGridProperty, value);
        }

        public static readonly StyledProperty<bool> ShowPointsProperty =
            AvaloniaProperty.Register<Simple3DView, bool>(nameof(ShowPoints), true);

        public bool ShowPoints
        {
            get => GetValue(ShowPointsProperty);
            set => SetValue(ShowPointsProperty, value);
        }

        public static readonly StyledProperty<bool> ShowLinesProperty =
            AvaloniaProperty.Register<Simple3DView, bool>(nameof(ShowLines), true);

        public bool ShowLines
        {
            get => GetValue(ShowLinesProperty);
            set => SetValue(ShowLinesProperty, value);
        }

        public static readonly StyledProperty<bool> ShowMeshProperty =
            AvaloniaProperty.Register<Simple3DView, bool>(nameof(ShowMesh), false);

        public bool ShowMesh
        {
            get => GetValue(ShowMeshProperty);
            set => SetValue(ShowMeshProperty, value);
        }

        public Simple3DView()
        {
            InitializeComponent();
            
            // Set initial rotation for better view
            _projection.RotationX = 0.5;
            _projection.RotationY = 0.5;
            _projection.Scale = 3.0;

            // Wire up property changes
            DataProperty.Changed.AddClassHandler<Simple3DView>((x, e) => x.InvalidateVisual());
            ShowGridProperty.Changed.AddClassHandler<Simple3DView>((x, e) => x.InvalidateVisual());
            ShowPointsProperty.Changed.AddClassHandler<Simple3DView>((x, e) => x.InvalidateVisual());
            ShowLinesProperty.Changed.AddClassHandler<Simple3DView>((x, e) => x.InvalidateVisual());
            ShowMeshProperty.Changed.AddClassHandler<Simple3DView>((x, e) => x.InvalidateVisual());

            // Mouse events
            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
            PointerWheelChanged += OnPointerWheelChanged;
        }

        private void InitializeComponent()
        {
            Background = Brushes.Transparent;
            ClipToBounds = true;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (Data == null || Bounds.Width == 0 || Bounds.Height == 0)
                return;

            var width = Bounds.Width;
            var height = Bounds.Height;

            // Draw background
            context.FillRectangle(new SolidColorBrush(Color.Parse("#0d0d0d")), new Rect(0, 0, width, height));

            // Draw grid
            if (ShowGrid && Data.GridLines.Count > 0)
            {
                foreach (var line in Data.GridLines)
                {
                    DrawLine3D(context, line, width, height);
                }
            }

            // Draw mesh triangles (before lines and points for proper layering)
            if (ShowMesh && Data.Triangles.Count > 0)
            {
                // Sort triangles by depth (painter's algorithm)
                var sortedTriangles = Data.Triangles
                    .OrderBy(t => _projection.GetDepth(t.GetCenter()))
                    .ToList();

                foreach (var triangle in sortedTriangles)
                {
                    DrawTriangle3D(context, triangle, width, height);
                }
            }

            // Draw lines (error vectors, trajectories)
            if (ShowLines && Data.Lines.Count > 0)
            {
                // Sort by depth for proper rendering
                var sortedLines = Data.Lines
                    .OrderBy(l => _projection.GetDepth(l.Start) + _projection.GetDepth(l.End))
                    .ToList();

                foreach (var line in sortedLines)
                {
                    DrawLine3D(context, line, width, height);
                }
            }

            // Draw points
            if (ShowPoints && Data.Points.Count > 0)
            {
                // Sort by depth (painter's algorithm - draw far points first)
                var sortedPoints = Data.Points
                    .OrderBy(p => _projection.GetDepth(p))
                    .ToList();

                foreach (var point in sortedPoints)
                {
                    DrawPoint3D(context, point, width, height);
                }
            }

            // Draw info overlay
            DrawInfoOverlay(context, width, height);
        }

        private void DrawPoint3D(DrawingContext context, Point3D point3D, double width, double height)
        {
            var point2D = _projection.Project(point3D, width, height);

            // Check if point is within bounds
            if (point2D.X < -50 || point2D.X > width + 50 || point2D.Y < -50 || point2D.Y > height + 50)
                return;

            var brush = new SolidColorBrush(Color.Parse(point3D.Color));
            var size = point3D.Size;

            // Draw point as circle
            context.DrawEllipse(brush, null, point2D, size, size);

            // Draw label if present
            if (!string.IsNullOrEmpty(point3D.Label))
            {
                var text = new FormattedText(
                    point3D.Label,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Consolas"),
                    10,
                    brush);

                context.DrawText(text, new Point(point2D.X + size + 2, point2D.Y - size));
            }
        }

        private void DrawLine3D(DrawingContext context, Line3D line3D, double width, double height)
        {
            var start2D = _projection.Project(line3D.Start, width, height);
            var end2D = _projection.Project(line3D.End, width, height);

            var pen = new Pen(new SolidColorBrush(Color.Parse(line3D.Color)), line3D.Thickness);
            context.DrawLine(pen, start2D, end2D);
        }

        private void DrawTriangle3D(DrawingContext context, Triangle3D triangle, double width, double height)
        {
            var p1 = _projection.Project(triangle.P1, width, height);
            var p2 = _projection.Project(triangle.P2, width, height);
            var p3 = _projection.Project(triangle.P3, width, height);

            // Create geometry for the triangle
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(p1, true);
                ctx.LineTo(p2);
                ctx.LineTo(p3);
                ctx.EndFigure(true);
            }

            // Fill the triangle
            var fillColor = Color.Parse(triangle.FillColor);
            fillColor = Color.FromArgb((byte)(triangle.Opacity * 255), fillColor.R, fillColor.G, fillColor.B);
            var fillBrush = new SolidColorBrush(fillColor);
            context.DrawGeometry(fillBrush, null, geometry);

            // Draw edges if enabled
            if (triangle.ShowEdges)
            {
                var edgePen = new Pen(new SolidColorBrush(Color.Parse(triangle.EdgeColor)), 0.5);
                context.DrawLine(edgePen, p1, p2);
                context.DrawLine(edgePen, p2, p3);
                context.DrawLine(edgePen, p3, p1);
            }
        }

        private void DrawInfoOverlay(DrawingContext context, double width, double height)
        {
            if (Data == null) return;

            var overlayBrush = new SolidColorBrush(Color.Parse("#1c1c1c"));
            var borderBrush = new SolidColorBrush(Color.Parse("#333333"));
            var textBrush = new SolidColorBrush(Color.Parse("#aaaaaa"));
            var valueBrush = new SolidColorBrush(Color.Parse("#dddddd"));

            var overlayRect = new Rect(16, 16, 280, 100);
            context.FillRectangle(overlayBrush, overlayRect);
            context.DrawRectangle(new Pen(borderBrush, 2), overlayRect);

            var typeface = new Typeface("Consolas");
            double y = 28;

            // Title
            var titleText = new FormattedText(
                "3D CALIBRATION VISUALIZATION",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                12,
                textBrush);
            titleText.SetFontWeight(FontWeight.Bold);
            context.DrawText(titleText, new Point(28, y));
            y += 20;

            // Point count
            var pointsText = new FormattedText(
                $"Points: {Data.Points.Count}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                11,
                valueBrush);
            context.DrawText(pointsText, new Point(28, y));
            y += 18;

            // Controls hint
            var controlsText = new FormattedText(
                "Left: Rotate | Wheel: Zoom | Right: Pan",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                10,
                new SolidColorBrush(Color.Parse("#777777")));
            context.DrawText(controlsText, new Point(28, y));
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this);
            _lastMousePosition = props.Position;

            if (props.Properties.IsLeftButtonPressed)
            {
                _isRotating = true;
                e.Handled = true;
            }
            else if (props.Properties.IsRightButtonPressed || props.Properties.IsMiddleButtonPressed)
            {
                _isPanning = true;
                e.Handled = true;
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            var props = e.GetCurrentPoint(this);
            var currentPosition = props.Position;

            if (_isRotating)
            {
                double deltaX = currentPosition.X - _lastMousePosition.X;
                double deltaY = currentPosition.Y - _lastMousePosition.Y;

                _projection.RotationY += deltaX * 0.01;
                _projection.RotationX += deltaY * 0.01;

                InvalidateVisual();
                e.Handled = true;
            }
            else if (_isPanning)
            {
                double deltaX = currentPosition.X - _lastMousePosition.X;
                double deltaY = currentPosition.Y - _lastMousePosition.Y;

                _projection.Offset = new Point(
                    _projection.Offset.X + deltaX,
                    _projection.Offset.Y + deltaY);

                InvalidateVisual();
                e.Handled = true;
            }

            _lastMousePosition = currentPosition;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _isRotating = false;
            _isPanning = false;
        }

        private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            double zoomDelta = e.Delta.Y > 0 ? 1.1 : 0.9;
            _projection.Scale = Math.Clamp(_projection.Scale * zoomDelta, 0.5, 10.0);

            InvalidateVisual();
            e.Handled = true;
        }

        public void ResetView()
        {
            _projection.RotationX = 0.5;
            _projection.RotationY = 0.5;
            _projection.RotationZ = 0;
            _projection.Scale = 3.0;
            _projection.Offset = new Point(0, 0);
            InvalidateVisual();
        }
    }
}
