using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace singalUI.Converters
{
    public class FocusLevelWidthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double focusLevel)
            {
                // Return star value representing the percentage (e.g., "0.72*" for 72%)
                return $"{focusLevel / 100.0}*";
            }
            return "0*";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
