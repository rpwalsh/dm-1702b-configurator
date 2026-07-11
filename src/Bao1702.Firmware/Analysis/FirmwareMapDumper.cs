using System.Text;

namespace Bao1702.Firmware.Analysis;

/// <summary>
/// Generates a memory-map view of a firmware image with per-region entropy values
/// and formatted hex-dump output.
/// </summary>
public static class FirmwareMapDumper
{
    public static IReadOnlyList<FirmwareMapEntry> BuildMap(FirmwareImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var entries = new List<FirmwareMapEntry>
        {
            new("header", 0, Math.Min(16, image.RawBytes.Length), ComputeEntropy(image.RawBytes.AsSpan(0, Math.Min(16, image.RawBytes.Length))), "Provisional fixed-width header region."),
        };

        entries.AddRange(image.Segments.Select(segment =>
            new FirmwareMapEntry(
                segment.Name,
                segment.Offset,
                segment.Length,
                ComputeEntropy(segment.Data),
                segment.Name.Equals("payload", StringComparison.OrdinalIgnoreCase)
                    ? "Payload or image body region."
                    : "Parser-defined segment.")));

        return entries.OrderBy(entry => entry.Offset).ToArray();
    }

    public static string DumpText(FirmwareImage image)
    {
        var map = BuildMap(image);
        var builder = new StringBuilder();
        builder.AppendLine("Firmware map");
        builder.AppendLine("============");
        foreach (var entry in map)
        {
            builder.AppendLine($"{entry.Name}: offset=0x{entry.Offset:X8}, length=0x{entry.Length:X8} ({entry.Length}), entropy={entry.Entropy:F3}");
            builder.AppendLine($"  {entry.Notes}");
        }

        return builder.ToString().TrimEnd();
    }

    private static double ComputeEntropy(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return 0d;
        }

        Span<int> counts = stackalloc int[256];
        foreach (var value in bytes)
        {
            counts[value]++;
        }

        var entropy = 0d;
        foreach (var count in counts)
        {
            if (count == 0)
            {
                continue;
            }

            var probability = (double)count / bytes.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }
}
