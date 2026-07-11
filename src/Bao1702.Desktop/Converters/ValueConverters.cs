using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Bao1702.Desktop.Converters;

/// <summary>Converts a boolean to <see cref="Visibility"/>. True = Visible, False = Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>Inverted boolean to visibility. True = Collapsed, False = Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>Formats a frequency in Hz as MHz with 6 decimal places.</summary>
public sealed class FrequencyDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long hz && hz > 0)
        {
            return (hz / 1_000_000.0).ToString("F6", CultureInfo.InvariantCulture) + " MHz";
        }

        return "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a log level string to a <see cref="Brush"/> color.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToUpperInvariant() switch
        {
            "ERROR" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),  // red-500
            "WARN" => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),   // amber-500
            "INFO" => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),   // blue-500
            "DEBUG" => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),   // gray-500
            _ => new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)),        // blue-300
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a double percentage (0–100) to a progress bar width or display string.</summary>
public sealed class PercentDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double pct)
        {
            return pct.ToString("F1", CultureInfo.InvariantCulture) + "%";
        }

        return "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns Visibility.Visible when string is not null or empty.</summary>
public sealed class NonEmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value?.ToString()) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
