using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace singalUI.Converters
{
    public class BoolToArrowConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isExpanded)
            {
                // Return "▼" when expanded, "▲" when collapsed
                return isExpanded ? "▼" : "▲";
            }
            return "▼";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
