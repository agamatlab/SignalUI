using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace singalUI.ViewModels
{
    public partial class StatisticsCardViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ObservableCollection<StatItem> _statItems = new();

        [ObservableProperty]
        private bool _hasOverall = false;

        [ObservableProperty]
        private string _overallLabel = "Overall:";

        [ObservableProperty]
        private string _overallValue = string.Empty;

        public StatisticsCardViewModel()
        {
        }

        public StatisticsCardViewModel(ObservableCollection<StatItem> statItems,
                                        bool hasOverall = false,
                                        string overallLabel = "Overall:",
                                        string overallValue = "")
        {
            StatItems = statItems;
            HasOverall = hasOverall;
            OverallLabel = overallLabel;
            OverallValue = overallValue;
        }
    }

    public partial class StatItem : ObservableObject
    {
        [ObservableProperty]
        private string _label = string.Empty;

        [ObservableProperty]
        private string _value = string.Empty;

        public StatItem(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }
}
