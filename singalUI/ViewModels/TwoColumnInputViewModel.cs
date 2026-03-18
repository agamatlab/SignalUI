using CommunityToolkit.Mvvm.ComponentModel;

namespace singalUI.ViewModels
{
    public partial class TwoColumnInputViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _leftLabel = string.Empty;

        [ObservableProperty]
        private double _leftValue;

        [ObservableProperty]
        private double _leftMin;

        [ObservableProperty]
        private double _leftMax;

        [ObservableProperty]
        private double _leftIncrement = 1;

        [ObservableProperty]
        private string _leftFormat = "F0";

        [ObservableProperty]
        private string _rightLabel = string.Empty;

        [ObservableProperty]
        private double _rightValue;

        [ObservableProperty]
        private double _rightMin;

        [ObservableProperty]
        private double _rightMax;

        [ObservableProperty]
        private double _rightIncrement = 1;

        [ObservableProperty]
        private string _rightFormat = "F0";

        public TwoColumnInputViewModel()
        {
        }

        public TwoColumnInputViewModel(string leftLabel, double leftValue, double leftMin, double leftMax, string leftFormat,
                                       string rightLabel, double rightValue, double rightMin, double rightMax, string rightFormat)
        {
            LeftLabel = leftLabel;
            LeftValue = leftValue;
            LeftMin = leftMin;
            LeftMax = leftMax;
            LeftFormat = leftFormat;
            RightLabel = rightLabel;
            RightValue = rightValue;
            RightMin = rightMin;
            RightMax = rightMax;
            RightFormat = rightFormat;
        }
    }
}
