using System.Text;
using Bao1702.Firmware.Analysis;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Computes Shannon entropy for binary data regions.</summary>
public static class EntropyCalculator
{
    public static double CalculateShannonEntropy(ReadOnlySpan<byte> bytes)
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

public static class HexDiffHelper
{
    public static string DescribeDiff(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var diff = FirmwareImageParser.Diff(left, right);
        var builder = new StringBuilder();
        builder.AppendLine($"Total differences: {diff.TotalDifferences}");
        foreach (var entry in diff.Differences.Take(32))
        {
            builder.AppendLine($"0x{entry.Offset:X4}: 0x{entry.Left:X2} -> 0x{entry.Right:X2}");
        }

        return builder.ToString().TrimEnd();
    }

    public static IReadOnlyList<int> FindChangedOffsets(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        return FirmwareImageParser.Diff(left, right)
            .Differences
            .Select(entry => entry.Offset)
            .ToArray();
    }
}

public static class StructureGuessUtilities
{
    public static IReadOnlyList<int> FindRepeatingStrideOffsets(ReadOnlySpan<byte> bytes, int stride)
    {
        if (stride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        var matches = new List<int>();
        for (var offset = 0; offset + (stride * 2) <= bytes.Length; offset += stride)
        {
            if (bytes.Slice(offset, stride).SequenceEqual(bytes.Slice(offset + stride, stride)))
            {
                matches.Add(offset);
            }
        }

        return matches;
    }
}

public static class CaptureImportHelper
{
    public static IReadOnlyList<byte[]> ParseHexLines(string captureText)
    {
        ArgumentNullException.ThrowIfNull(captureText);

        var frames = new List<byte[]>();
        using var reader = new StringReader(captureText);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var tokens = line.Split([' ', '\t', ',', ';', ':'], StringSplitOptions.RemoveEmptyEntries)
                .Where(static token => token.Length == 2 && token.All(Uri.IsHexDigit))
                .ToArray();
            if (tokens.Length == 0)
            {
                continue;
            }

            frames.Add(tokens.Select(token => Convert.ToByte(token, 16)).ToArray());
        }

        return frames;
    }
}
