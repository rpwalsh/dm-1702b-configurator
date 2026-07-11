using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace Bao1702.Desktop.Converters;

/// <summary>
/// Converts an <see cref="IEnumerable"/> of strings to a comma-separated display string.
/// Used as a singleton via <see cref="Instance"/> for x:Static binding in XAML.
/// </summary>
public sealed class JoinConverter : IValueConverter
{
    public static readonly JoinConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable<string> items)
        {
            return string.Join(", ", items);
        }

        if (value is IEnumerable enumerable)
        {
            return string.Join(", ", enumerable.Cast<object>().Select(o => o?.ToString() ?? string.Empty));
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
