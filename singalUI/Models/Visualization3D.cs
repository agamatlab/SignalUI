using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace singalUI.Models
{
    /// <summary>
    /// Simple 3D point for visualization
    /// </summary>
    public class Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Color { get; set; } = "#4CAF50";
        public double Size { get; set; } = 4.0;
        public string Label { get; set; } = "";

        public Point3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Point3D(double x, double y, double z, string color, double size = 4.0)
        {
            X = x;
            Y = y;
            Z = z;
            Color = color;
            Size = size;
        }
    }

    /// <summary>
    /// 3D line for visualization
    /// </summary>
    public class Line3D
    {
        public Point3D Start { get; set; }
        public Point3D End { get; set; }
        public string Color { get; set; } = "#FFFFFF";
        public double Thickness { get; set; } = 1.0;

        public Line3D(Point3D start, Point3D end, string color = "#FFFFFF", double thickness = 1.0)
        {
            Start = start;
            End = end;
            Color = color;
            Thickness = thickness;
        }
    }

    /// <summary>
    /// 3D mesh triangle for surface visualization
    /// </summary>
    public class Triangle3D
    {
        public Point3D P1 { get; set; }
        public Point3D P2 { get; set; }
        public Point3D P3 { get; set; }
        public string FillColor { get; set; } = "#2196F3";
        public string EdgeColor { get; set; } = "#1976D2";
        public double Opacity { get; set; } = 0.6;
        public bool ShowEdges { get; set; } = true;

        public Triangle3D(Point3D p1, Point3D p2, Point3D p3, string fillColor = "#2196F3", double opacity = 0.6)
        {
            P1 = p1;
            P2 = p2;
            P3 = p3;
            FillColor = fillColor;
            Opacity = opacity;
        }

        /// <summary>
        /// Calculate center point for depth sorting
        /// </summary>
        public Point3D GetCenter()
        {
            return new Point3D(
                (P1.X + P2.X + P3.X) / 3.0,
                (P1.Y + P2.Y + P3.Y) / 3.0,
                (P1.Z + P2.Z + P3.Z) / 3.0
            );
        }
    }

    /// <summary>
    /// Simple 3D to 2D projection helper
    /// </summary>
    public class Simple3DProjection
    {
        public double CameraDistance { get; set; } = 500;
        public double RotationX { get; set; } = 0.3; // Radians
        public double RotationY { get; set; } = 0.3; // Radians
        public double RotationZ { get; set; } = 0;   // Radians
        public double Scale { get; set; } = 1.0;
        public Point Offset { get; set; } = new Point(0, 0);

        /// <summary>World-space point that left-drag rotation orbits around (default origin).</summary>
        public Point3D RotationPivot { get; set; } = new Point3D(0, 0, 0);

        /// <summary>
        /// Project 3D point to 2D screen coordinates
        /// </summary>
        public Point Project(Point3D point3D, double canvasWidth, double canvasHeight)
        {
            // Apply rotation
            var rotated = RotatePoint(point3D);

            // Apply perspective projection
            double factor = CameraDistance / (CameraDistance + rotated.Z);
            double x2D = rotated.X * factor * Scale + canvasWidth / 2 + Offset.X;
            double y2D = -rotated.Y * factor * Scale + canvasHeight / 2 + Offset.Y; // Flip Y for screen coordinates

            return new Point(x2D, y2D);
        }

        /// <summary>
        /// Rotate point around <see cref="RotationPivot"/> (same as world origin when pivot is 0,0,0).
        /// </summary>
        private Point3D RotatePoint(Point3D p)
        {
            double px = RotationPivot.X, py = RotationPivot.Y, pz = RotationPivot.Z;
            double x = p.X - px, y = p.Y - py, z = p.Z - pz;

            double y1 = y * Math.Cos(RotationX) - z * Math.Sin(RotationX);
            double z1 = y * Math.Sin(RotationX) + z * Math.Cos(RotationX);

            double x2 = x * Math.Cos(RotationY) + z1 * Math.Sin(RotationY);
            double z2 = -x * Math.Sin(RotationY) + z1 * Math.Cos(RotationY);

            double x3 = x2 * Math.Cos(RotationZ) - y1 * Math.Sin(RotationZ);
            double y3 = x2 * Math.Sin(RotationZ) + y1 * Math.Cos(RotationZ);

            return new Point3D(x3 + px, y3 + py, z2 + pz);
        }

        /// <summary>
        /// Get Z-depth for sorting (painter's algorithm)
        /// </summary>
        public double GetDepth(Point3D point3D)
        {
            var rotated = RotatePoint(point3D);
            return rotated.Z;
        }

        /// <summary>
        /// After changing <see cref="Scale"/> from <paramref name="oldScale"/> to <paramref name="newScale"/>,
        /// adjusts <see cref="Offset"/> so the screen projection of <paramref name="worldPivot"/> stays fixed
        /// (zoom centered on that 3D point, not the world origin). <paramref name="canvasWidth"/> must be positive.
        /// </summary>
        public void ZoomAboutWorldPoint(Point3D worldPivot, double oldScale, double newScale, double canvasWidth)
        {
            if (Math.Abs(oldScale) < 1e-15 || Math.Abs(newScale - oldScale) < 1e-15)
                return;
            if (canvasWidth <= 0)
                return;

            double k = newScale / oldScale;
            var rp = RotatePoint(worldPivot);
            double denom = CameraDistance + rp.Z;
            if (Math.Abs(denom) < 1e-6)
                return;

            double fp = CameraDistance / denom;
            Offset = new Point(
                Offset.X + rp.X * fp * oldScale * (1 - k),
                Offset.Y + rp.Y * fp * oldScale * (k - 1));
        }
    }

    /// <summary>
    /// 3D visualization data container
    /// </summary>
    public class Visualization3DData
    {
        public List<Point3D> Points { get; set; } = new();
        public List<Line3D> Lines { get; set; } = new();
        public List<Line3D> GridLines { get; set; } = new();
        public List<Triangle3D> Triangles { get; set; } = new();
        public string Title { get; set; } = "3D Visualization";
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public double MinZ { get; set; }
        public double MaxZ { get; set; }

        public void CalculateBounds()
        {
            if (Points.Count == 0) return;

            MinX = MaxX = Points[0].X;
            MinY = MaxY = Points[0].Y;
            MinZ = MaxZ = Points[0].Z;

            foreach (var point in Points)
            {
                MinX = Math.Min(MinX, point.X);
                MaxX = Math.Max(MaxX, point.X);
                MinY = Math.Min(MinY, point.Y);
                MaxY = Math.Max(MaxY, point.Y);
                MinZ = Math.Min(MinZ, point.Z);
                MaxZ = Math.Max(MaxZ, point.Z);
            }
        }

        public void GenerateGrid(double spacing = 10)
        {
            GridLines.Clear();

            // Calculate grid bounds (rounded to spacing)
            double gridMinX = Math.Floor(MinX / spacing) * spacing;
            double gridMaxX = Math.Ceiling(MaxX / spacing) * spacing;
            double gridMinY = Math.Floor(MinY / spacing) * spacing;
            double gridMaxY = Math.Ceiling(MaxY / spacing) * spacing;

            // Grid at Z=0 plane
            // Lines parallel to X axis
            for (double y = gridMinY; y <= gridMaxY; y += spacing)
            {
                GridLines.Add(new Line3D(
                    new Point3D(gridMinX, y, 0),
                    new Point3D(gridMaxX, y, 0),
                    "#333333", 1.0));
            }

            // Lines parallel to Y axis
            for (double x = gridMinX; x <= gridMaxX; x += spacing)
            {
                GridLines.Add(new Line3D(
                    new Point3D(x, gridMinY, 0),
                    new Point3D(x, gridMaxY, 0),
                    "#333333", 1.0));
            }

            // Axis lines (thicker)
            GridLines.Add(new Line3D(
                new Point3D(0, gridMinY, 0),
                new Point3D(0, gridMaxY, 0),
                "#FF5252", 2.0)); // X axis - Red

            GridLines.Add(new Line3D(
                new Point3D(gridMinX, 0, 0),
                new Point3D(gridMaxX, 0, 0),
                "#4CAF50", 2.0)); // Y axis - Green

            GridLines.Add(new Line3D(
                new Point3D(0, 0, MinZ),
                new Point3D(0, 0, MaxZ),
                "#2196F3", 2.0)); // Z axis - Blue
        }

        /// <summary>
        /// Generate mesh surface from points arranged in a grid
        /// </summary>
        public void GenerateMeshFromGrid(int gridWidth, int gridHeight)
        {
            Triangles.Clear();

            if (Points.Count < gridWidth * gridHeight)
                return;

            // Create triangles connecting adjacent grid points
            for (int row = 0; row < gridHeight - 1; row++)
            {
                for (int col = 0; col < gridWidth - 1; col++)
                {
                    int idx1 = row * gridWidth + col;
                    int idx2 = row * gridWidth + (col + 1);
                    int idx3 = (row + 1) * gridWidth + col;
                    int idx4 = (row + 1) * gridWidth + (col + 1);

                    if (idx1 >= Points.Count || idx2 >= Points.Count || 
                        idx3 >= Points.Count || idx4 >= Points.Count)
                        continue;

                    // First triangle (upper-left)
                    Triangles.Add(new Triangle3D(
                        Points[idx1],
                        Points[idx2],
                        Points[idx3],
                        "#2196F3", 0.5));

                    // Second triangle (lower-right)
                    Triangles.Add(new Triangle3D(
                        Points[idx2],
                        Points[idx4],
                        Points[idx3],
                        "#1976D2", 0.5));
                }
            }
        }

        /// <summary>
        /// Generate mesh surface from scattered points using Delaunay-like triangulation
        /// </summary>
        public void GenerateMeshFromScatteredPoints()
        {
            Triangles.Clear();

            if (Points.Count < 3)
                return;

            // Simple nearest-neighbor mesh generation
            // For each point, connect to nearby points
            for (int i = 0; i < Points.Count; i++)
            {
                var p1 = Points[i];
                
                // Find 6 nearest neighbors
                var neighbors = Points
                    .Select((p, idx) => new { Point = p, Index = idx, Distance = Distance(p1, p) })
                    .Where(x => x.Index != i)
                    .OrderBy(x => x.Distance)
                    .Take(6)
                    .ToList();

                // Create triangles with pairs of neighbors
                for (int j = 0; j < neighbors.Count - 1; j++)
                {
                    var p2 = neighbors[j].Point;
                    var p3 = neighbors[j + 1].Point;

                    // Only add triangle if it's not too large
                    if (Distance(p2, p3) < 30)
                    {
                        // Color based on Z height
                        double avgZ = (p1.Z + p2.Z + p3.Z) / 3.0;
                        double zNorm = (avgZ - MinZ) / (MaxZ - MinZ + 0.001);
                        string color = GetHeightColor(zNorm);

                        Triangles.Add(new Triangle3D(p1, p2, p3, color, 0.6));
                    }
                }
            }
        }

        private double Distance(Point3D p1, Point3D p2)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            double dz = p1.Z - p2.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private string GetHeightColor(double normalized)
        {
            // Blue (low) -> Cyan -> Green -> Yellow -> Red (high)
            if (normalized < 0.25)
                return "#2196F3"; // Blue
            else if (normalized < 0.5)
                return "#00BCD4"; // Cyan
            else if (normalized < 0.75)
                return "#4CAF50"; // Green
            else
                return "#FF9800"; // Orange
        }
    }
}
