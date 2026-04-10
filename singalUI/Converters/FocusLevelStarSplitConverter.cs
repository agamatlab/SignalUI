using Avalonia.Controls;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace singalUI.Converters;

/// <summary>
/// Splits focus level 0–100 into proportional star columns: primary (filled) vs remainder (track).
/// Use <see cref="ConverterParameter"/> <c>Remainder</c> for the unfilled side.
/// </summary>
public sealed class FocusLevelStarSplitConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value is double d ? Math.Clamp(d, 0, 100) : 0;
        bool remainder = string.Equals(parameter?.ToString(), "Remainder", StringComparison.OrdinalIgnoreCase);
        double stars = remainder ? 100 - pct : pct;
        // Avoid 0* which can confuse layout; tiny epsilon keeps column visible.
        stars = Math.Max(stars, 0.0001);
        return new GridLength(stars, GridUnitType.Star);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
