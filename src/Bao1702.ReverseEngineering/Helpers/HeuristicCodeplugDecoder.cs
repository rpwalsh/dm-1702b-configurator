using System.Text;
using System.Text.RegularExpressions;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Encoding strategy used for BCD frequency fields in channel records.</summary>
public enum FrequencyEncodingKind
{
    BcdAsStored,
    BcdByteReversed,
}

public sealed record DecodedStringEntry(int Offset, string Text, string Category);

public sealed record FrequencyPairCandidate(
    int Offset,
    double RxFrequencyMHz,
    double TxFrequencyMHz,
    FrequencyEncodingKind Encoding,
    CodeplugConfidence Confidence);

public sealed record ChannelCandidate(
    int NameOffset,
    string Name,
    int? FrequencyOffset,
    double? RxFrequencyMHz,
    double? TxFrequencyMHz,
    string Evidence,
    CodeplugConfidence Confidence);

public sealed record HeuristicDecodedCodeplug(
    IReadOnlyList<DecodedStringEntry> Strings,
    IReadOnlyList<FrequencyPairCandidate> FrequencyPairs,
    IReadOnlyList<ChannelCandidate> ChannelCandidates);

/// <summary>
/// Heuristic decoder for the stock-read reconstructed image.
/// This does not claim final format knowledge. It surfaces likely channel names and nearby frequency pairs
/// so the recovered radio data can be reviewed and iterated toward a proper typed codeplug decoder.
/// </summary>
public static partial class HeuristicCodeplugDecoder
{
    private const int MinimumStringLength = 4;
    private const int MaximumStringLength = 32;
    private const int MaximumNameToFrequencyDistance = 256;

[GeneratedRegex(@"^(?:[A-Z0-9]{1,3}\s)?[A-Z0-9].*", RegexOptions.Compiled)]
    private static partial Regex ChannelLikeRegex();

    public static HeuristicDecodedCodeplug Decode(ReadOnlySpan<byte> image)
    {
        var strings = ExtractStrings(image);
        var frequencyPairs = ScanFrequencyPairs(image);
        var channelCandidates = CorrelateChannels(strings, frequencyPairs);
        return new HeuristicDecodedCodeplug(strings, frequencyPairs, channelCandidates);
    }

    public static IReadOnlyList<DecodedStringEntry> ExtractStrings(ReadOnlySpan<byte> image)
    {
        var results = new List<DecodedStringEntry>();
        var builder = new StringBuilder();
        var startOffset = 0;

        for (var index = 0; index < image.Length; index++)
        {
            var value = image[index];
            if (value is >= 0x20 and <= 0x7E)
            {
                if (builder.Length == 0)
                {
                    startOffset = index;
                }

                builder.Append((char)value);
                if (builder.Length >= MaximumStringLength)
                {
                    FlushString(results, builder, startOffset);
                }

                continue;
            }

            FlushString(results, builder, startOffset);
        }

        FlushString(results, builder, startOffset);
        return results;
    }

    public static IReadOnlyList<FrequencyPairCandidate> ScanFrequencyPairs(ReadOnlySpan<byte> image)
    {
        var results = new List<FrequencyPairCandidate>();
        for (var offset = 0; offset <= image.Length - 8; offset++)
        {
            TryAddCandidate(image.Slice(offset, 8), offset, FrequencyEncodingKind.BcdAsStored, reverse: false, results);
            TryAddCandidate(image.Slice(offset, 8), offset, FrequencyEncodingKind.BcdByteReversed, reverse: true, results);
        }

        return results
            .GroupBy(static candidate => new { candidate.Offset, candidate.Encoding, candidate.RxFrequencyMHz, candidate.TxFrequencyMHz })
            .Select(static group => group.First())
            .OrderBy(static candidate => candidate.Offset)
            .ToArray();
    }

    private static IReadOnlyList<ChannelCandidate> CorrelateChannels(
        IReadOnlyList<DecodedStringEntry> strings,
        IReadOnlyList<FrequencyPairCandidate> frequencyPairs)
    {
        var candidates = new List<ChannelCandidate>();
        foreach (var entry in strings)
        {
            if (!LooksLikeChannelName(entry.Text))
            {
                continue;
            }

            var nearestFrequency = frequencyPairs
                .Select(candidate => new { Candidate = candidate, Distance = Math.Abs(candidate.Offset - entry.Offset) })
                .Where(static item => item.Distance <= MaximumNameToFrequencyDistance)
                .OrderBy(static item => item.Distance)
                .ThenBy(static item => item.Candidate.Offset)
                .FirstOrDefault();

            if (nearestFrequency is null)
            {
                candidates.Add(new ChannelCandidate(entry.Offset, entry.Text, null, null, null, "Name-like ASCII entry with no nearby plausible frequency pair.", CodeplugConfidence.Unknown));
                continue;
            }

            candidates.Add(new ChannelCandidate(
                entry.Offset,
                entry.Text,
                nearestFrequency.Candidate.Offset,
                nearestFrequency.Candidate.RxFrequencyMHz,
                nearestFrequency.Candidate.TxFrequencyMHz,
                $"Name-like ASCII entry correlated with nearest plausible {nearestFrequency.Candidate.Encoding} frequency pair at distance {nearestFrequency.Distance}.",
                CodeplugConfidence.RequiresHardwareVerification));
        }

        return candidates
            .GroupBy(static candidate => new { candidate.NameOffset, candidate.Name })
            .Select(static group => group.OrderBy(static candidate => candidate.FrequencyOffset ?? int.MaxValue).First())
            .OrderBy(static candidate => candidate.NameOffset)
            .ToArray();
    }

    private static void TryAddCandidate(
        ReadOnlySpan<byte> bytes,
        int offset,
        FrequencyEncodingKind encoding,
        bool reverse,
        List<FrequencyPairCandidate> results)
    {
        if (!TryDecodeBcdFrequency(bytes[..4], reverse, out var rx) || !TryDecodeBcdFrequency(bytes[4..8], reverse, out var tx))
        {
            return;
        }

        if (!IsPlausibleFrequency(rx) || !IsPlausibleFrequency(tx))
        {
            return;
        }

        var delta = Math.Abs(rx - tx);
        if (delta > 20d)
        {
            return;
        }

        var confidence = delta is 0d or <= 5d
            ? CodeplugConfidence.Inferred
            : CodeplugConfidence.RequiresHardwareVerification;
        results.Add(new FrequencyPairCandidate(offset, rx, tx, encoding, confidence));
    }

    private static bool TryDecodeBcdFrequency(ReadOnlySpan<byte> bytes, bool reverse, out double frequencyMHz)
    {
        frequencyMHz = 0d;
        Span<byte> working = stackalloc byte[4];
        bytes.CopyTo(working);
        if (reverse)
        {
            working.Reverse();
        }

        Span<char> digits = stackalloc char[8];
        for (var index = 0; index < working.Length; index++)
        {
            var value = working[index];
            var high = (value >> 4) & 0x0F;
            var low = value & 0x0F;
            if (high > 9 || low > 9)
            {
                return false;
            }

            digits[index * 2] = (char)('0' + high);
            digits[(index * 2) + 1] = (char)('0' + low);
        }

        if (!int.TryParse(digits[..3], out var whole))
        {
            return false;
        }

        if (!int.TryParse(digits[3..], out var fractional))
        {
            return false;
        }

        frequencyMHz = whole + (fractional / 100000d);
        return true;
    }

    private static bool IsPlausibleFrequency(double value)
        => (value >= 108d && value <= 174d) || (value >= 400d && value <= 520d);

    private static bool LooksLikeChannelName(string text)
    {
        if (text.Length < 4 || text.Length > 24)
        {
            return false;
        }

        if (text.All(static character => character == '@' || character == ' ' || character == '.'))
        {
            return false;
        }

        return ChannelLikeRegex().IsMatch(text);
    }

    private static void FlushString(List<DecodedStringEntry> results, StringBuilder builder, int offset)
    {
        if (builder.Length < MinimumStringLength)
        {
            builder.Clear();
            return;
        }

        var text = builder.ToString().Trim();
        if (text.Length >= MinimumStringLength)
        {
            results.Add(new DecodedStringEntry(offset, text, Categorize(text)));
        }

        builder.Clear();
    }

    private static string Categorize(string text)
    {
        if (text.Contains('[', StringComparison.Ordinal) || text.Contains(']', StringComparison.Ordinal))
        {
            return "NamedListOrGroup";
        }

        if (text.Any(char.IsDigit) && text.Any(char.IsLetter))
        {
            return "ChannelLike";
        }

        return "AsciiString";
    }
}
