using Avalonia.Data.Converters;
using singalUI.Models;
using System;
using System.Globalization;

namespace singalUI.Converters
{
    public class BinningConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is BinningMode binning)
            {
                return binning switch
                {
                    BinningMode.Bin1x1 => "1x1",
                    BinningMode.Bin2x2 => "2x2",
                    BinningMode.Bin4x4 => "4x4",
                    _ => "1x1"
                };
            }
            return "1x1";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                return str switch
                {
                    "1x1" => BinningMode.Bin1x1,
                    "2x2" => BinningMode.Bin2x2,
                    "4x4" => BinningMode.Bin4x4,
                    _ => BinningMode.Bin1x1
                };
            }
            return BinningMode.Bin1x1;
        }
    }

    public class BinningIndexConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is BinningMode binning)
            {
                return binning switch
                {
                    BinningMode.Bin1x1 => 0,
                    BinningMode.Bin2x2 => 1,
                    BinningMode.Bin4x4 => 2,
                    _ => 0
                };
            }
            return 0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return index switch
                {
                    0 => BinningMode.Bin1x1,
                    1 => BinningMode.Bin2x2,
                    2 => BinningMode.Bin4x4,
                    _ => BinningMode.Bin1x1
                };
            }
            return BinningMode.Bin1x1;
        }
    }

    public class ActiveForegroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                return "#2563eb";
            }
            return "#64748b";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
