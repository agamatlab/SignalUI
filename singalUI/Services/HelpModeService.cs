using System;

namespace singalUI.Services
{
    public static class HelpModeService
    {
        public const string TabCamera = "camera";
        public const string TabStage = "setup";
        public const string TabAnalysis = "analysis";

        private static bool _isEnabled;
        private static string _activeMainTab = TabCamera;

        public static bool IsEnabled => _isEnabled;

        /// <summary>Main window tab key (<see cref="MainWindowViewModel.ActiveTab"/>).</summary>
        public static string ActiveMainTab => _activeMainTab;

        public static event Action<bool>? HelpModeChanged;

        /// <summary>Fired when the main window switches tabs (camera / stage / visualization).</summary>
        public static event Action? ActiveMainTabChanged;

        public static void SetEnabled(bool enabled)
        {
            if (_isEnabled == enabled)
                return;

            _isEnabled = enabled;
            HelpModeChanged?.Invoke(enabled);
        }

        public static void Toggle()
        {
            SetEnabled(!_isEnabled);
        }

        public static void SetActiveMainTab(string tab)
        {
            if (string.IsNullOrEmpty(tab) || _activeMainTab == tab)
                return;

            _activeMainTab = tab;
            ActiveMainTabChanged?.Invoke();
        }
    }
}
