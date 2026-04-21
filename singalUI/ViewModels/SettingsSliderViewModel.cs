using CommunityToolkit.Mvvm.ComponentModel;

namespace singalUI.ViewModels
{
    public partial class SettingsSliderViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _label = string.Empty;

        [ObservableProperty]
        private double _value;

        [ObservableProperty]
        private double _minimum = 0;

        [ObservableProperty]
        private double _maximum = 100;

        [ObservableProperty]
        private double _tickFrequency = 1;

        [ObservableProperty]
        private bool _snapToTick = false;

        public SettingsSliderViewModel()
        {
        }

        public SettingsSliderViewModel(string label, double value, double minimum, double maximum,
                                      double tickFrequency = 1, bool snapToTick = false)
        {
            Label = label;
            Value = value;
            Minimum = minimum;
            Maximum = maximum;
            TickFrequency = tickFrequency;
            SnapToTick = snapToTick;
        }
    }
}
