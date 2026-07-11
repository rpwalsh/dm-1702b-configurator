using System.Globalization;
using System.Text;
using Bao1702.Codeplug.Model;
using Bao1702.Codeplug.Validation;

namespace Bao1702.Codeplug.Csv;

/// <summary>Definition of a single column in a CSV schema.</summary>
public sealed record CsvColumnDefinition(string Name, bool Required);

public sealed record CsvSchema(string Name, IReadOnlyList<CsvColumnDefinition> Columns);

public sealed record CsvRow(int RowNumber, IReadOnlyDictionary<string, string> Values)
{
    public string this[string columnName] => Values[columnName];

    public string GetValueOrDefault(string columnName, string defaultValue)
        => Values.TryGetValue(columnName, out var value) ? value : defaultValue;
}

public sealed record CsvDocument(IReadOnlyList<string> Headers, IReadOnlyList<CsvRow> Rows);

public sealed record ImportResult<T>(T? Value, IReadOnlyList<ValidationIssue> Issues)
{
    public bool Success => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

public static class Bao1702CsvSchemas
{
    public static CsvSchema Channels { get; } = new(
        "channels.csv",
        [
            new CsvColumnDefinition("Name", true),
            new CsvColumnDefinition("ChannelType", true),
            new CsvColumnDefinition("RxFrequencyMHz", true),
            new CsvColumnDefinition("TxFrequencyMHz", true),
            new CsvColumnDefinition("Power", true),
            new CsvColumnDefinition("Bandwidth", true),
            new CsvColumnDefinition("AdmitCriteria", true),
            new CsvColumnDefinition("ReceiveOnly", false),
            new CsvColumnDefinition("ZoneNames", false),
            new CsvColumnDefinition("RxTone", false),
            new CsvColumnDefinition("TxTone", false),
            new CsvColumnDefinition("ColorCode", false),
            new CsvColumnDefinition("TimeSlot", false),
            new CsvColumnDefinition("ContactName", false),
            new CsvColumnDefinition("RxGroupName", false),
        ]);

    public static CsvSchema Contacts { get; } = new(
        "contacts.csv",
        [
            new CsvColumnDefinition("Name", true),
            new CsvColumnDefinition("CallId", true),
            new CsvColumnDefinition("ContactType", true),
        ]);

    public static CsvSchema Zones { get; } = new(
        "zones.csv",
        [
            new CsvColumnDefinition("ZoneName", true),
            new CsvColumnDefinition("ChannelNames", true),
        ]);

    public static CsvSchema RxGroups { get; } = new(
        "rxgroups.csv",
        [
            new CsvColumnDefinition("Name", true),
            new CsvColumnDefinition("ContactNames", true),
        ]);

    public static CsvSchema ScanLists { get; } = new(
        "scanlists.csv",
        [
            new CsvColumnDefinition("Name", true),
            new CsvColumnDefinition("ChannelNames", true),
        ]);

    public static CsvSchema GroupLists { get; } = new(
        "grouplists.csv",
        [
            new CsvColumnDefinition("Name", true),
            new CsvColumnDefinition("ContactNames", true),
        ]);
}

public abstract class CsvImporter<T>
{
    public abstract CsvSchema Schema { get; }

    public abstract ImportResult<T> Import(string csvText);

    protected static CsvDocument ParseDocument(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        using var reader = new StringReader(csvText.Replace("\r\n", "\n", StringComparison.Ordinal));
        var headerLine = reader.ReadLine() ?? throw new InvalidDataException("CSV is empty.");
        var headers = ParseLine(headerLine);
        var rows = new List<CsvRow>();
        string? line;
        var rowNumber = 2;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                rowNumber++;
                continue;
            }

            var values = ParseLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Count; index++)
            {
                row[headers[index]] = index < values.Count ? values[index] : string.Empty;
            }

            rows.Add(new CsvRow(rowNumber, row));
            rowNumber++;
        }

        return new CsvDocument(headers, rows);
    }

    protected static IReadOnlyList<ValidationIssue> ValidateHeaders(CsvDocument document, CsvSchema schema)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(schema);

        var headerSet = new HashSet<string>(document.Headers, StringComparer.OrdinalIgnoreCase);
        var issues = new List<ValidationIssue>();
        foreach (var column in schema.Columns.Where(column => column.Required))
        {
            if (!headerSet.Contains(column.Name))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"Required column '{column.Name}' is missing from {schema.Name}.",
                    1,
                    column.Name));
            }
        }

        return issues;
    }

    protected static IReadOnlyList<string> ParseLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var value = line[index];
            if (value == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (value == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(value);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    protected static long ParseFrequencyHz(string text)
        => (long)Math.Round(decimal.Parse(text, CultureInfo.InvariantCulture) * 1_000_000m, MidpointRounding.AwayFromZero);
}

public abstract class CsvExporter<T>
{
    public abstract CsvSchema Schema { get; }

    public abstract string Export(T value);

    protected static string Escape(string? value)
    {
        value ??= string.Empty;
        // Prevent spreadsheet applications from interpreting user-controlled text as a formula.
        // A leading apostrophe is the conventional CSV neutralization and remains visible on import.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
