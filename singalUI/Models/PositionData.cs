using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace singalUI.Models
{
    public class PositionData : INotifyPropertyChanged
    {
        private double _x;
        private double _y;
        private double _z;
        private double _rx;
        private double _ry;
        private double _rz;

        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        public double Z
        {
            get => _z;
            set { _z = value; OnPropertyChanged(); }
        }

        public double Rx
        {
            get => _rx;
            set { _rx = value; OnPropertyChanged(); }
        }

        public double Ry
        {
            get => _ry;
            set { _ry = value; OnPropertyChanged(); }
        }

        public double Rz
        {
            get => _rz;
            set { _rz = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PlotDataPoint
    {
        public int Step { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Rx { get; set; }
        public double Ry { get; set; }
        public double Rz { get; set; }
    }
}
