using System.Text;

namespace Bao1702.Firmware.Analysis;

/// <summary>
/// Extracts printable strings (ASCII and UTF-16LE) from raw firmware image bytes,
/// reporting offset and encoding for each match.
/// </summary>
public static class FirmwareStringExtractor
{
    public static IReadOnlyList<FirmwareStringEntry> ExtractDetailed(ReadOnlySpan<byte> bytes, int minimumLength = 4)
    {
        if (minimumLength < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLength));
        }

        var results = new List<FirmwareStringEntry>();
        results.AddRange(ExtractAscii(bytes, minimumLength));
        results.AddRange(ExtractUtf16Le(bytes, minimumLength));
        return results
            .OrderBy(entry => entry.Offset)
            .ThenBy(entry => entry.Encoding, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> ExtractValues(ReadOnlySpan<byte> bytes, int minimumLength = 4)
    {
        return ExtractDetailed(bytes, minimumLength)
            .Select(entry => entry.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<FirmwareStringEntry> ExtractAscii(ReadOnlySpan<byte> bytes, int minimumLength)
    {
        var buffer = bytes.ToArray();
        var results = new List<FirmwareStringEntry>();
        var current = new StringBuilder();
        var startOffset = -1;
        for (var index = 0; index < buffer.Length; index++)
        {
            var value = buffer[index];
            if (value is >= 32 and <= 126)
            {
                if (startOffset < 0)
                {
                    startOffset = index;
                }

                current.Append((char)value);
                continue;
            }

            if (current.Length >= minimumLength)
            {
                results.Add(new FirmwareStringEntry(startOffset, "ASCII", current.ToString()));
            }

            current.Clear();
            startOffset = -1;
        }

        if (current.Length >= minimumLength)
        {
            results.Add(new FirmwareStringEntry(startOffset, "ASCII", current.ToString()));
        }

        return results;
    }

    private static IEnumerable<FirmwareStringEntry> ExtractUtf16Le(ReadOnlySpan<byte> bytes, int minimumLength)
    {
        var buffer = bytes.ToArray();
        var results = new List<FirmwareStringEntry>();
        var current = new StringBuilder();
        var startOffset = -1;
        for (var index = 0; index + 1 < buffer.Length; index += 2)
        {
            var low = buffer[index];
            var high = buffer[index + 1];
            var isPrintable = high == 0x00 && low is >= 32 and <= 126;
            if (isPrintable)
            {
                if (startOffset < 0)
                {
                    startOffset = index;
                }

                current.Append((char)low);
                continue;
            }

            if (current.Length >= minimumLength)
            {
                results.Add(new FirmwareStringEntry(startOffset, "UTF16-LE", current.ToString()));
            }

            current.Clear();
            startOffset = -1;
        }

        if (current.Length >= minimumLength)
        {
            results.Add(new FirmwareStringEntry(startOffset, "UTF16-LE", current.ToString()));
        }

        return results;
    }
}
