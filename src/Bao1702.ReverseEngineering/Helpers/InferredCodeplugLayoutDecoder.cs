using System.Text.RegularExpressions;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Category of a heuristically detected string table in the native image.</summary>
public enum InferredStringTableKind
{
    Unknown,
    ChannelName,
    ContactName,
    GroupOrZoneName,
}

public sealed record InferredStringTable(
    InferredStringTableKind Kind,
    int StartOffset,
    int Stride,
    int EntryCount,
    IReadOnlyList<string> SampleEntries,
    CodeplugConfidence Confidence);

public sealed record InferredChannelRecord(
    int Index,
    string Name,
    int NameOffset,
    int TableStartOffset,
    int TableStride,
    int? FrequencyOffset,
    double? RxFrequencyMHz,
    double? TxFrequencyMHz,
    string Evidence,
    CodeplugConfidence Confidence);

public sealed record InferredContactRecord(
    int Index,
    string Name,
    int Offset,
    int TableStartOffset,
    int TableStride,
    CodeplugConfidence Confidence);

public sealed record InferredNamedListRecord(
    int Index,
    string Name,
    int Offset,
    int TableStartOffset,
    int TableStride,
    CodeplugConfidence Confidence);

public sealed record InferredCodeplugLayout(
    IReadOnlyList<InferredStringTable> Tables,
    IReadOnlyList<InferredChannelRecord> Channels,
    IReadOnlyList<InferredContactRecord> Contacts,
    IReadOnlyList<InferredNamedListRecord> NamedLists);

public static partial class InferredCodeplugLayoutDecoder
{
    private const int MinimumStride = 8;
    private const int MaximumStride = 32;
    private const int MinimumEntriesPerTable = 4;
    private const int MaximumChannelNameToFrequencyDistance = 512;

    [GeneratedRegex(@"^Call\s+\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ContactRegex();

    public static InferredCodeplugLayout Decode(HeuristicDecodedCodeplug decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        var tables = InferTables(decoded.Strings);
        var channels = InferChannels(tables, decoded.FrequencyPairs);
        var contacts = InferContacts(tables);
        var namedLists = InferNamedLists(tables);
        return new InferredCodeplugLayout(tables, channels, contacts, namedLists);
    }

    private static IReadOnlyList<InferredStringTable> InferTables(IReadOnlyList<DecodedStringEntry> strings)
    {
        var sorted = strings.OrderBy(static entry => entry.Offset).ToArray();
        var consumed = new HashSet<int>();
        var tables = new List<InferredStringTable>();

        for (var index = 0; index < sorted.Length - 1; index++)
        {
            if (consumed.Contains(index))
            {
                continue;
            }

            var kind = Classify(sorted[index].Text);
            if (kind == InferredStringTableKind.Unknown)
            {
                continue;
            }

            var stride = sorted[index + 1].Offset - sorted[index].Offset;
            if (stride < MinimumStride || stride > MaximumStride)
            {
                continue;
            }

            var entries = new List<DecodedStringEntry> { sorted[index] };
            var probeIndex = index + 1;
            var expectedOffset = sorted[index].Offset + stride;
            while (probeIndex < sorted.Length)
            {
                if (sorted[probeIndex].Offset == expectedOffset && Classify(sorted[probeIndex].Text) == kind)
                {
                    entries.Add(sorted[probeIndex]);
                    expectedOffset += stride;
                    probeIndex++;
                    continue;
                }

                break;
            }

            if (entries.Count < MinimumEntriesPerTable)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var entryIndex = Array.IndexOf(sorted, entry);
                if (entryIndex >= 0)
                {
                    consumed.Add(entryIndex);
                }
            }

            tables.Add(new InferredStringTable(
                kind,
                entries[0].Offset,
                stride,
                entries.Count,
                entries.Take(8).Select(static entry => entry.Text).ToArray(),
                CodeplugConfidence.Inferred));
        }

        return tables
            .OrderBy(static table => table.StartOffset)
            .ToArray();
    }

    private static IReadOnlyList<InferredChannelRecord> InferChannels(
        IReadOnlyList<InferredStringTable> tables,
        IReadOnlyList<FrequencyPairCandidate> frequencyPairs)
    {
        var results = new List<InferredChannelRecord>();
        foreach (var table in tables.Where(static table => table.Kind == InferredStringTableKind.ChannelName))
        {
            for (var index = 0; index < table.EntryCount; index++)
            {
                var nameOffset = table.StartOffset + (index * table.Stride);
                var name = table.SampleEntries.ElementAtOrDefault(index);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = null;
                }

                if (name is null)
                {
                    continue;
                }

                var nearestFrequency = frequencyPairs
                    .Select(candidate => new { Candidate = candidate, Distance = Math.Abs(candidate.Offset - nameOffset) })
                    .Where(static item => item.Distance <= MaximumChannelNameToFrequencyDistance)
                    .OrderBy(static item => item.Distance)
                    .ThenBy(static item => item.Candidate.Offset)
                    .FirstOrDefault();

                results.Add(new InferredChannelRecord(
                    index,
                    name,
                    nameOffset,
                    table.StartOffset,
                    table.Stride,
                    nearestFrequency?.Candidate.Offset,
                    nearestFrequency?.Candidate.RxFrequencyMHz,
                    nearestFrequency?.Candidate.TxFrequencyMHz,
                    nearestFrequency is null
                        ? "No nearby plausible frequency pair was found."
                        : $"Nearest plausible {nearestFrequency.Candidate.Encoding} frequency pair at distance {nearestFrequency.Distance}.",
                    nearestFrequency is null ? CodeplugConfidence.Unknown : CodeplugConfidence.RequiresHardwareVerification));
            }
        }

        return results;
    }

    private static IReadOnlyList<InferredContactRecord> InferContacts(IReadOnlyList<InferredStringTable> tables)
    {
        var results = new List<InferredContactRecord>();
        foreach (var table in tables.Where(static table => table.Kind == InferredStringTableKind.ContactName))
        {
            for (var index = 0; index < table.EntryCount; index++)
            {
                var name = table.SampleEntries.ElementAtOrDefault(index);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                results.Add(new InferredContactRecord(index, name, table.StartOffset + (index * table.Stride), table.StartOffset, table.Stride, CodeplugConfidence.Inferred));
            }
        }

        return results;
    }

    private static IReadOnlyList<InferredNamedListRecord> InferNamedLists(IReadOnlyList<InferredStringTable> tables)
    {
        var results = new List<InferredNamedListRecord>();
        foreach (var table in tables.Where(static table => table.Kind == InferredStringTableKind.GroupOrZoneName))
        {
            for (var index = 0; index < table.EntryCount; index++)
            {
                var name = table.SampleEntries.ElementAtOrDefault(index);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                results.Add(new InferredNamedListRecord(index, name, table.StartOffset + (index * table.Stride), table.StartOffset, table.Stride, CodeplugConfidence.Inferred));
            }
        }

        return results;
    }

    private static InferredStringTableKind Classify(string text)
    {
        if (ContactRegex().IsMatch(text))
        {
            return InferredStringTableKind.ContactName;
        }

        if (text.Contains('[', StringComparison.Ordinal) || text.Contains(']', StringComparison.Ordinal))
        {
            return InferredStringTableKind.GroupOrZoneName;
        }

        return LooksLikeStructuredChannelName(text)
            ? InferredStringTableKind.ChannelName
            : InferredStringTableKind.Unknown;
    }

    private static bool LooksLikeStructuredChannelName(string text)
    {
        if (text.Length < 4 || text.Length > 16)
        {
            return false;
        }

        if (!text.Any(char.IsLetter))
        {
            return false;
        }

        return text.Any(char.IsDigit)
            || text.Contains(' ', StringComparison.Ordinal)
            || text.Contains('/', StringComparison.Ordinal)
            || text.Contains('.', StringComparison.Ordinal);
    }
}
