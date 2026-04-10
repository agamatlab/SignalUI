using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;

namespace singalUI.ViewModels
{
    public partial class ChartCardViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private double _rmsValue;

        [ObservableProperty]
        private string _color = "#FF5252";

        [ObservableProperty]
        private ObservableCollection<BarItem> _bars = new();

        [ObservableProperty]
        private string _barColor = "#FF5252";

        [ObservableProperty]
        private string _statLabel = string.Empty;

        [ObservableProperty]
        private string _statValue = string.Empty;

        [ObservableProperty]
        private ObservableCollection<double> _seriesValues = new();

        [ObservableProperty]
        private string _unit = string.Empty;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private int _rowIndex;

        [ObservableProperty]
        private int _columnIndex;

        public ChartCardViewModel()
        {
        }

        public ChartCardViewModel(string title, double rmsValue, string color,
                                 ObservableCollection<BarItem> bars,
                                 string statLabel, string statValue,
                                 int rowIndex, int columnIndex,
                                 string unit = "",
                                 ObservableCollection<double>? seriesValues = null)
        {
            Title = title;
            RmsValue = rmsValue;
            Color = color;
            Bars = bars;
            StatLabel = statLabel;
            StatValue = statValue;
            RowIndex = rowIndex;
            ColumnIndex = columnIndex;
            Unit = unit;
            SeriesValues = seriesValues ?? new ObservableCollection<double>();
        }
    }

    public partial class BarItem : ObservableObject
    {
        [ObservableProperty]
        private double _height;

        [ObservableProperty]
        private string _color = "#FF5252";

        public BarItem(double height)
        {
            Height = height;
        }

        public BarItem(double height, string color)
        {
            Height = height;
            Color = color;
        }
    }
}
