using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Detected channel record table location within a codeplug image.</summary>
public sealed record ChannelRecordTable(
    int StartOffset,
    int Stride,
    int EntryCount,
    byte[] RepeatingValue,
    CodeplugConfidence Confidence);

public static class ChannelRecordTableDetector
{
    public static IReadOnlyList<ChannelRecordTable> Detect(ReadOnlySpan<byte> image, IReadOnlyList<InferredStringTable> channelTables)
    {
        ArgumentNullException.ThrowIfNull(channelTables);
        var results = new List<ChannelRecordTable>();
        foreach (var table in channelTables.Where(static candidate => candidate.Kind == InferredStringTableKind.ChannelName))
        {
            var searchStart = table.StartOffset + (table.EntryCount * table.Stride);
            var searchEnd = Math.Min(image.Length, searchStart + 2048);
            var zeroRunLength = 0;
            for (var offset = searchStart; offset <= searchEnd - table.Stride; offset++)
            {
                if (image[offset] == 0)
                {
                    zeroRunLength++;
                    continue;
                }

                if (zeroRunLength > 0 && zeroRunLength < table.Stride)
                {
                    zeroRunLength = 0;
                    continue;
                }

                zeroRunLength = 0;

                var pattern = image.Slice(offset, table.Stride).ToArray();
                if (pattern.All(static value => value == 0))
                {
                    continue;
                }

                var count = CountRepeatingEntries(image, offset, pattern);
                if (count < Math.Min(8, table.EntryCount / 2))
                {
                    continue;
                }

                results.Add(new ChannelRecordTable(offset, table.Stride, count, pattern, CodeplugConfidence.RequiresHardwareVerification));
                break;
            }
        }

        return results;
    }

    private static int CountRepeatingEntries(ReadOnlySpan<byte> image, int offset, byte[] pattern)
    {
        var count = 0;
        while (offset + pattern.Length <= image.Length && image.Slice(offset, pattern.Length).SequenceEqual(pattern))
        {
            count++;
            offset += pattern.Length;
        }

        return count;
    }
}
