using Avalonia.Controls;
using singalUI.Services;

namespace singalUI.Views;

public partial class HelpManualWindow : Window
{
    public HelpManualWindow()
    {
        InitializeComponent();
        Closing += (_, _) => HelpModeService.SetEnabled(false);
    }
}
