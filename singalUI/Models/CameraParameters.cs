using CommunityToolkit.Mvvm.ComponentModel;

namespace singalUI.Models
{
    public partial class CameraParameters : ObservableObject
    {
        [ObservableProperty]
        private double _exposure = 3000;

        [ObservableProperty]
        private double _gain = 12;

        [ObservableProperty]
        private double _illumination = 100;

        [ObservableProperty]
        private double _fps = 30;

        [ObservableProperty]
        private BinningMode _binning = BinningMode.Bin1x1;
    }
}
