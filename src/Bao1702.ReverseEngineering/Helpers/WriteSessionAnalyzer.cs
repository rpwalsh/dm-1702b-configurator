namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>A single write block extracted from a captured CPS write session.</summary>
public sealed record WriteBlock(
    int FrameNumber,
    int Address,
    int WindowOffset,
    byte[] Data,
    string AsciiPreview);

public sealed record WriteSessionAnalysis(
    IReadOnlyList<WriteBlock> Blocks,
    IReadOnlyDictionary<int, int> WindowWriteCounts);

public static class WriteSessionAnalyzer
{
    public static WriteSessionAnalysis AnalyzeTsharkFieldLines(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var blocks = new List<WriteBlock>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 5)
            {
                continue;
            }

            if (!string.Equals(parts[1], "0x01", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(parts[2], "0x03", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(parts[3], out var length) || length != 69)
            {
                continue;
            }

            var payloadHex = parts[4].Trim();
            if (payloadHex.Length != 138 || !payloadHex.StartsWith("57", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = Convert.FromHexString(payloadHex);
            if (payload.Length != 69)
            {
                continue;
            }

            var address = payload[1] | (payload[2] << 8) | (payload[3] << 16);
            var windowOffset = payload[1];
            var data = payload.AsSpan(5, 64).ToArray();
            blocks.Add(new WriteBlock(
                int.Parse(parts[0]),
                address,
                windowOffset,
                data,
                BuildAsciiPreview(data)));
        }

        var counts = blocks
            .GroupBy(static block => block.WindowOffset)
            .ToDictionary(static group => group.Key, static group => group.Count());
        return new WriteSessionAnalysis(blocks, counts);
    }

    private static string BuildAsciiPreview(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[bytes.Length];
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            chars[index] = value is >= 0x20 and <= 0x7E ? (char)value : '.';
        }

        return new string(chars).Trim('.');
    }
}
