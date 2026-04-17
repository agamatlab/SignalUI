using System;

namespace singalUI.Services
{
    public static class HelpModeService
    {
        private static bool _isEnabled;

        public static bool IsEnabled => _isEnabled;

        public static event Action<bool>? HelpModeChanged;

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
    }
}
