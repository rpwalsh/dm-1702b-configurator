using System.Globalization;
using Bao1702.Codeplug.Validation;

namespace Bao1702.Codeplug.Csv;

/// <summary>Exception thrown when a CSV import encounters invalid data at a specific row and column.</summary>
internal sealed class CsvImportException : FormatException
{
    public CsvImportException(string message, int rowNumber, string columnName)
        : base(message)
    {
        RowNumber = rowNumber;
        ColumnName = columnName;
    }

    public int RowNumber { get; }

    public string ColumnName { get; }

    public ValidationIssue ToValidationIssue()
        => new(ValidationSeverity.Error, Message, RowNumber, ColumnName);
}

internal static class CsvImportHelpers
{
    public static string GetRequired(CsvRow row, string columnName)
    {
        if (!row.Values.TryGetValue(columnName, out var value))
        {
            throw new CsvImportException($"Required column '{columnName}' is missing.", row.RowNumber, columnName);
        }

        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CsvImportException($"Column '{columnName}' is required.", row.RowNumber, columnName);
        }

        return value;
    }

    public static string GetOptional(CsvRow row, string columnName)
    {
        return row.Values.TryGetValue(columnName, out var value)
            ? value.Trim()
            : string.Empty;
    }

    public static TEnum ParseEnum<TEnum>(CsvRow row, string columnName)
        where TEnum : struct, Enum
    {
        var value = GetRequired(row, columnName);
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            throw new CsvImportException($"Column '{columnName}' value '{value}' is not a valid {typeof(TEnum).Name}.", row.RowNumber, columnName);
        }

        return parsed;
    }

    public static long ParseFrequencyHz(CsvRow row, string columnName)
    {
        var value = GetRequired(row, columnName);
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedMHz))
        {
            throw new CsvImportException($"Column '{columnName}' value '{value}' is not a valid MHz frequency.", row.RowNumber, columnName);
        }

        if (parsedMHz <= 0)
        {
            throw new CsvImportException($"Column '{columnName}' must be greater than zero.", row.RowNumber, columnName);
        }

        return (long)Math.Round(parsedMHz * 1_000_000m, MidpointRounding.AwayFromZero);
    }

    public static int ParseInt(CsvRow row, string columnName, int minValue, int maxValue, int defaultValue)
    {
        var value = GetOptional(row, columnName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new CsvImportException($"Column '{columnName}' value '{value}' is not a valid integer.", row.RowNumber, columnName);
        }

        if (parsed < minValue || parsed > maxValue)
        {
            throw new CsvImportException($"Column '{columnName}' value '{value}' must be between {minValue} and {maxValue}.", row.RowNumber, columnName);
        }

        return parsed;
    }

    public static IReadOnlyList<string> ParseDelimitedNames(CsvRow row, string columnName)
    {
        return GetOptional(row, columnName)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool ParseBool(CsvRow row, string columnName, bool defaultValue = false)
    {
        var value = GetOptional(row, columnName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw new CsvImportException($"Column '{columnName}' value '{value}' is not a valid boolean.", row.RowNumber, columnName);
        }

        return parsed;
    }

    public static string? ParseOptionalReference(CsvRow row, string columnName)
    {
        var value = GetOptional(row, columnName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static void EnsureBlank(CsvRow row, string columnName, string message)
    {
        var value = GetOptional(row, columnName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            throw new CsvImportException(message, row.RowNumber, columnName);
        }
    }
}
