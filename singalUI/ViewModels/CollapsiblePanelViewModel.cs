using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace singalUI.ViewModels
{
    public partial class CollapsiblePanelViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _iconPath = string.Empty;

        [ObservableProperty]
        private bool _isExpanded = true;

        [ObservableProperty]
        private object? _content;

        public CollapsiblePanelViewModel()
        {
        }

        public CollapsiblePanelViewModel(string title, string iconPath, object? content = null, bool isExpanded = true)
        {
            Title = title;
            IconPath = iconPath;
            Content = content;
            IsExpanded = isExpanded;
        }

        [RelayCommand]
        private void Toggle()
        {
            IsExpanded = !IsExpanded;
        }
    }
}
