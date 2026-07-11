using System.Globalization;
using System.Text;
using Bao1702.Codeplug.Model;
using Bao1702.Codeplug.Validation;

namespace Bao1702.Codeplug.Csv;

/// <summary>Imports channel records from CSV files using the standard channel schema.</summary>
public sealed class ChannelCsvImporter : CsvImporter<IReadOnlyList<Channel>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.Channels;

    public override ImportResult<IReadOnlyList<Channel>> Import(string csvText)
    {
        var document = ParseDocument(csvText);
        var issues = ValidateHeaders(document, Schema).ToList();
        var result = new List<Channel>();

        foreach (var row in document.Rows)
        {
            try
            {
                var kind = CsvImportHelpers.ParseEnum<ChannelKind>(row, "ChannelType");
                var zoneNames = CsvImportHelpers.ParseDelimitedNames(row, "ZoneNames");

                var commonArgs = new CommonChannelValues(
                    row.RowNumber - 1,
                    CsvImportHelpers.GetRequired(row, "Name"),
                    CsvImportHelpers.ParseFrequencyHz(row, "RxFrequencyMHz"),
                    CsvImportHelpers.ParseFrequencyHz(row, "TxFrequencyMHz"),
                    CsvImportHelpers.ParseEnum<PowerLevel>(row, "Power"),
                    CsvImportHelpers.ParseEnum<ChannelBandwidth>(row, "Bandwidth"),
                    CsvImportHelpers.ParseEnum<AdmitCriteria>(row, "AdmitCriteria"),
                    zoneNames,
                    CodeplugConfidence.Inferred);

                Channel channel = kind switch
                {
                    ChannelKind.Analog => new AnalogChannel(
                        commonArgs.Index,
                        commonArgs.Name,
                        commonArgs.RxFrequencyHz,
                        commonArgs.TxFrequencyHz,
                        commonArgs.Power,
                        commonArgs.Bandwidth,
                        commonArgs.AdmitCriteria,
                        commonArgs.ZoneNames,
                        ToneValue.Parse(CsvImportHelpers.GetOptional(row, "RxTone")),
                        ToneValue.Parse(CsvImportHelpers.GetOptional(row, "TxTone")),
                        commonArgs.Confidence),
                    ChannelKind.Digital => new DigitalChannel(
                        commonArgs.Index,
                        commonArgs.Name,
                        commonArgs.RxFrequencyHz,
                        commonArgs.TxFrequencyHz,
                        commonArgs.Power,
                        commonArgs.Bandwidth,
                        commonArgs.AdmitCriteria,
                        commonArgs.ZoneNames,
                        CsvImportHelpers.ParseInt(row, "ColorCode", 0, 15, 1),
                        CsvImportHelpers.ParseInt(row, "TimeSlot", 1, 2, 1),
                        CsvImportHelpers.ParseOptionalReference(row, "ContactName"),
                        CsvImportHelpers.ParseOptionalReference(row, "RxGroupName"),
                        commonArgs.Confidence),
                    _ => throw new InvalidOperationException($"Unsupported channel type '{kind}'.")
                };

                switch (channel)
                {
                    case AnalogChannel:
                        CsvImportHelpers.EnsureBlank(row, "ColorCode", "Analog channel rows must not populate ColorCode.");
                        CsvImportHelpers.EnsureBlank(row, "TimeSlot", "Analog channel rows must not populate TimeSlot.");
                        CsvImportHelpers.EnsureBlank(row, "ContactName", "Analog channel rows must not populate ContactName.");
                        CsvImportHelpers.EnsureBlank(row, "RxGroupName", "Analog channel rows must not populate RxGroupName.");
                        break;
                    case DigitalChannel:
                        CsvImportHelpers.EnsureBlank(row, "RxTone", "Digital channel rows must not populate RxTone.");
                        CsvImportHelpers.EnsureBlank(row, "TxTone", "Digital channel rows must not populate TxTone.");
                        break;
                }

                result.Add(channel with { ReceiveOnly = CsvImportHelpers.ParseBool(row, "ReceiveOnly") });
            }
            catch (CsvImportException ex)
            {
                issues.Add(ex.ToValidationIssue());
            }
        }

        return new ImportResult<IReadOnlyList<Channel>>(result, issues);
    }

    private sealed record CommonChannelValues(
        int Index,
        string Name,
        long RxFrequencyHz,
        long TxFrequencyHz,
        PowerLevel Power,
        ChannelBandwidth Bandwidth,
        AdmitCriteria AdmitCriteria,
        IReadOnlyList<string> ZoneNames,
        CodeplugConfidence Confidence);
}

public sealed class ChannelCsvExporter : CsvExporter<IReadOnlyList<Channel>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.Channels;

    public override string Export(IReadOnlyList<Channel> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var lines = new List<string>
        {
            string.Join(',', Schema.Columns.Select(column => column.Name))
        };

        foreach (var channel in value)
        {
            string[] row = channel switch
            {
                AnalogChannel analog =>
                [
                    Escape(analog.Name),
                    "Analog",
                    (analog.RxFrequencyHz / 1_000_000m).ToString("F4", CultureInfo.InvariantCulture),
                    (analog.TxFrequencyHz / 1_000_000m).ToString("F4", CultureInfo.InvariantCulture),
                    analog.Power.ToString(),
                    analog.Bandwidth.ToString(),
                    analog.AdmitCriteria.ToString(),
                    analog.ReceiveOnly.ToString(CultureInfo.InvariantCulture),
                    Escape(string.Join(';', analog.ZoneNames)),
                    Escape(analog.RxTone?.RawValue ?? string.Empty),
                    Escape(analog.TxTone?.RawValue ?? string.Empty),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                ],
                DigitalChannel digital =>
                [
                    Escape(digital.Name),
                    "Digital",
                    (digital.RxFrequencyHz / 1_000_000m).ToString("F4", CultureInfo.InvariantCulture),
                    (digital.TxFrequencyHz / 1_000_000m).ToString("F4", CultureInfo.InvariantCulture),
                    digital.Power.ToString(),
                    digital.Bandwidth.ToString(),
                    digital.AdmitCriteria.ToString(),
                    digital.ReceiveOnly.ToString(CultureInfo.InvariantCulture),
                    Escape(string.Join(';', digital.ZoneNames)),
                    string.Empty,
                    string.Empty,
                    digital.ColorCode.ToString(CultureInfo.InvariantCulture),
                    digital.TimeSlot.ToString(CultureInfo.InvariantCulture),
                    Escape(digital.ContactName),
                    Escape(digital.RxGroupName),
                ],
                _ => throw new InvalidOperationException($"Unsupported channel type {channel.GetType().Name}.")
            };

            lines.Add(string.Join(',', row));
        }

        return string.Join(Environment.NewLine, lines);
    }
}

public sealed class ContactCsvImporter : CsvImporter<IReadOnlyList<Contact>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.Contacts;

    public override ImportResult<IReadOnlyList<Contact>> Import(string csvText)
    {
        var document = ParseDocument(csvText);
        var issues = ValidateHeaders(document, Schema).ToList();
        var contacts = new List<Contact>();
        foreach (var row in document.Rows)
        {
            try
            {
                contacts.Add(new Contact(
                    CsvImportHelpers.GetRequired(row, "Name"),
                    CsvImportHelpers.ParseInt(row, "CallId", 1, 16_777_215, 1),
                    CsvImportHelpers.ParseEnum<ContactType>(row, "ContactType"),
                    CodeplugConfidence.Inferred));
            }
            catch (CsvImportException ex)
            {
                issues.Add(ex.ToValidationIssue());
            }
        }

        return new ImportResult<IReadOnlyList<Contact>>(contacts, issues);
    }
}

public sealed class ContactCsvExporter : CsvExporter<IReadOnlyList<Contact>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.Contacts;

    public override string Export(IReadOnlyList<Contact> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder();
        builder.AppendLine("Name,CallId,ContactType");
        foreach (var contact in value)
        {
            builder.AppendLine($"{Escape(contact.Name)},{contact.CallId.ToString(CultureInfo.InvariantCulture)},{contact.ContactType}");
        }

        return builder.ToString().TrimEnd();
    }
}

public sealed class ZoneCsvImporter : CsvImporter<IReadOnlyList<Zone>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.Zones;

    public override ImportResult<IReadOnlyList<Zone>> Import(string csvText)
    {
        var document = ParseDocument(csvText);
        var issues = ValidateHeaders(document, Schema).ToList();
        var zones = new List<Zone>();

        foreach (var row in document.Rows)
        {
            try
            {
                zones.Add(new Zone(
                    CsvImportHelpers.GetRequired(row, "ZoneName"),
                    CsvImportHelpers.ParseDelimitedNames(row, "ChannelNames"),
                    CodeplugConfidence.Inferred));
            }
            catch (CsvImportException ex)
            {
                issues.Add(ex.ToValidationIssue());
            }
        }

        return new ImportResult<IReadOnlyList<Zone>>(zones, issues);
    }
}

public sealed class ZoneCsvExporter : CsvExporter<IReadOnlyList<Zone>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.Zones;

    public override string Export(IReadOnlyList<Zone> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder();
        builder.AppendLine("ZoneName,ChannelNames");
        foreach (var zone in value)
        {
            builder.AppendLine($"{Escape(zone.Name)},{Escape(string.Join(';', zone.ChannelNames))}");
        }

        return builder.ToString().TrimEnd();
    }
}

public sealed class RxGroupCsvImporter : CsvImporter<IReadOnlyList<RxGroup>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.RxGroups;

    public override ImportResult<IReadOnlyList<RxGroup>> Import(string csvText)
    {
        var document = ParseDocument(csvText);
        var issues = ValidateHeaders(document, Schema).ToList();
        var groups = new List<RxGroup>();

        foreach (var row in document.Rows)
        {
            try
            {
                groups.Add(new RxGroup(
                    CsvImportHelpers.GetRequired(row, "Name"),
                    CsvImportHelpers.ParseDelimitedNames(row, "ContactNames"),
                    CodeplugConfidence.Inferred));
            }
            catch (CsvImportException ex)
            {
                issues.Add(ex.ToValidationIssue());
            }
        }

        return new ImportResult<IReadOnlyList<RxGroup>>(groups, issues);
    }
}

public sealed class RxGroupCsvExporter : CsvExporter<IReadOnlyList<RxGroup>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.RxGroups;

    public override string Export(IReadOnlyList<RxGroup> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder();
        builder.AppendLine("Name,ContactNames");
        foreach (var group in value)
        {
            builder.AppendLine($"{Escape(group.Name)},{Escape(string.Join(';', group.ContactNames))}");
        }

        return builder.ToString().TrimEnd();
    }
}

public sealed class ScanListCsvImporter : CsvImporter<IReadOnlyList<ScanList>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.ScanLists;

    public override ImportResult<IReadOnlyList<ScanList>> Import(string csvText)
    {
        var document = ParseDocument(csvText);
        var issues = ValidateHeaders(document, Schema).ToList();
        var scanLists = new List<ScanList>();

        foreach (var row in document.Rows)
        {
            try
            {
                scanLists.Add(new ScanList(
                    CsvImportHelpers.GetRequired(row, "Name"),
                    CsvImportHelpers.ParseDelimitedNames(row, "ChannelNames"),
                    CodeplugConfidence.Inferred));
            }
            catch (CsvImportException ex)
            {
                issues.Add(ex.ToValidationIssue());
            }
        }

        return new ImportResult<IReadOnlyList<ScanList>>(scanLists, issues);
    }
}

public sealed class ScanListCsvExporter : CsvExporter<IReadOnlyList<ScanList>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.ScanLists;

    public override string Export(IReadOnlyList<ScanList> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder();
        builder.AppendLine("Name,ChannelNames");
        foreach (var list in value)
        {
            builder.AppendLine($"{Escape(list.Name)},{Escape(string.Join(';', list.ChannelNames))}");
        }

        return builder.ToString().TrimEnd();
    }
}

public sealed class GroupListCsvImporter : CsvImporter<IReadOnlyList<GroupList>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.GroupLists;

    public override ImportResult<IReadOnlyList<GroupList>> Import(string csvText)
    {
        var document = ParseDocument(csvText);
        var issues = ValidateHeaders(document, Schema).ToList();
        var groupLists = new List<GroupList>();

        foreach (var row in document.Rows)
        {
            try
            {
                groupLists.Add(new GroupList(
                    CsvImportHelpers.GetRequired(row, "Name"),
                    CsvImportHelpers.ParseDelimitedNames(row, "ContactNames"),
                    CodeplugConfidence.Inferred));
            }
            catch (CsvImportException ex)
            {
                issues.Add(ex.ToValidationIssue());
            }
        }

        return new ImportResult<IReadOnlyList<GroupList>>(groupLists, issues);
    }
}

public sealed class GroupListCsvExporter : CsvExporter<IReadOnlyList<GroupList>>
{
    public override CsvSchema Schema => Bao1702CsvSchemas.GroupLists;

    public override string Export(IReadOnlyList<GroupList> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder();
        builder.AppendLine("Name,ContactNames");
        foreach (var group in value)
        {
            builder.AppendLine($"{Escape(group.Name)},{Escape(string.Join(';', group.ContactNames))}");
        }

        return builder.ToString().TrimEnd();
    }
}

public sealed class CodeplugCsvService
{
    private readonly ChannelCsvExporter _channelExporter = new();
    private readonly ChannelCsvImporter _channelImporter = new();
    private readonly ContactCsvExporter _contactExporter = new();
    private readonly ContactCsvImporter _contactImporter = new();
    private readonly GroupListCsvExporter _groupListExporter = new();
    private readonly GroupListCsvImporter _groupListImporter = new();
    private readonly RxGroupCsvExporter _rxGroupExporter = new();
    private readonly RxGroupCsvImporter _rxGroupImporter = new();
    private readonly ScanListCsvExporter _scanListExporter = new();
    private readonly ScanListCsvImporter _scanListImporter = new();
    private readonly ZoneCsvExporter _zoneExporter = new();
    private readonly ZoneCsvImporter _zoneImporter = new();

    public IReadOnlyDictionary<string, string> Export(CodeplugImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Bao1702CsvSchemas.Channels.Name] = _channelExporter.Export(image.Channels),
            [Bao1702CsvSchemas.Contacts.Name] = _contactExporter.Export(image.Contacts),
            [Bao1702CsvSchemas.GroupLists.Name] = _groupListExporter.Export(image.GroupLists),
            [Bao1702CsvSchemas.RxGroups.Name] = _rxGroupExporter.Export(image.RxGroups),
            [Bao1702CsvSchemas.ScanLists.Name] = _scanListExporter.Export(image.ScanLists),
            [Bao1702CsvSchemas.Zones.Name] = _zoneExporter.Export(image.Zones),
        };
    }

    public ImportResult<CodeplugImage> Import(IReadOnlyDictionary<string, string> csvFiles)
    {
        ArgumentNullException.ThrowIfNull(csvFiles);

        // Normalize keys: accept both "channels" and "channels.csv" as lookup keys.
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in csvFiles)
        {
            normalized[key] = value;
            // Also register the key without .csv extension so callers can use either form.
            if (key.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                normalized[key[..^4]] = value;
            }
            else
            {
                normalized[key + ".csv"] = value;
            }
        }

        var channelResult = _channelImporter.Import(ResolveSchema(normalized, Bao1702CsvSchemas.Channels, string.Empty));
        var contactResult = _contactImporter.Import(ResolveSchema(normalized, Bao1702CsvSchemas.Contacts, "Name,CallId,ContactType"));
        var groupListResult = _groupListImporter.Import(ResolveSchema(normalized, Bao1702CsvSchemas.GroupLists, "Name,ContactNames"));
        var rxGroupResult = _rxGroupImporter.Import(ResolveSchema(normalized, Bao1702CsvSchemas.RxGroups, "Name,ContactNames"));
        var scanListResult = _scanListImporter.Import(ResolveSchema(normalized, Bao1702CsvSchemas.ScanLists, "Name,ChannelNames"));
        var zoneResult = _zoneImporter.Import(ResolveSchema(normalized, Bao1702CsvSchemas.Zones, "ZoneName,ChannelNames"));

        var issues = channelResult.Issues
            .Concat(contactResult.Issues)
            .Concat(groupListResult.Issues)
            .Concat(rxGroupResult.Issues)
            .Concat(scanListResult.Issues)
            .Concat(zoneResult.Issues)
            .ToList();
        var image = CodeplugImage.CreateEmpty() with
        {
            Channels = channelResult.Value ?? [],
            Contacts = contactResult.Value ?? [],
            GroupLists = groupListResult.Value ?? [],
            RxGroups = rxGroupResult.Value ?? [],
            ScanLists = scanListResult.Value ?? [],
            Zones = zoneResult.Value ?? [],
            PreservedRawImage = [],
        };

        issues.AddRange(CodeplugValidator.Validate(image));
        return new ImportResult<CodeplugImage>(image, issues);
    }

    /// <summary>
    /// Exports codeplug CSV files to a directory, one file per schema.
    /// Files are named using the schema name (e.g., channels.csv, contacts.csv).
    /// </summary>
    public void ExportToDirectory(CodeplugImage image, string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        Directory.CreateDirectory(directoryPath);

        foreach (var (schemaName, content) in Export(image))
        {
            var filePath = Path.Combine(directoryPath, schemaName);
            File.WriteAllText(filePath, content, System.Text.Encoding.UTF8);
        }
    }

    /// <summary>
    /// Imports all recognized CSV files from a directory.
    /// Recognizes files by name: channels.csv, contacts.csv, zones.csv, grouplists.csv, scanlists.csv, rxgroups.csv.
    /// </summary>
    public ImportResult<CodeplugImage> ImportFromDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"CSV import directory not found: {directoryPath}");
        }

        var csvFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(directoryPath, "*.csv"))
        {
            csvFiles[Path.GetFileName(file)] = File.ReadAllText(file);
        }

        return Import(csvFiles);
    }

    private static string ResolveSchema(Dictionary<string, string> normalized, CsvSchema schema, string fallback)
    {
        // Try with extension first, then without.
        if (normalized.TryGetValue(schema.Name, out var value))
        {
            return value;
        }

        var nameWithoutExtension = schema.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? schema.Name[..^4]
            : schema.Name;

        return normalized.GetValueOrDefault(nameWithoutExtension, fallback);
    }
}
