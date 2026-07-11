using System.Security.Cryptography;
using System.Text;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>An unwritten gap within a reassembled codeplug image.</summary>
public sealed record ReassembledImageGap(int StartAddress, int Length);

public sealed record ReassembledImageResult(
    int BaseAddress,
    byte[] Image,
    int BlockCount,
    IReadOnlyList<ReassembledImageGap> Gaps,
    string Sha256,
    IReadOnlyList<string> ExtractedStrings);

public static class StockCpsCodeplugReassembler
{
    public static ReassembledImageResult Reassemble(IReadOnlyList<StockCpsDataBlockDump> dataBlocks)
    {
        ArgumentNullException.ThrowIfNull(dataBlocks);

        var addressedBlocks = dataBlocks
            .Where(static block => block.DataLength > 0)
            .Select(block => new
            {
                Block = block,
                Address = ParseAddress(block.SelectorHex),
                Data = Convert.FromHexString(block.DataHex),
            })
            .OrderBy(static item => item.Address)
            .ToArray();

        if (addressedBlocks.Length == 0)
        {
            return new ReassembledImageResult(0, [], 0, [], string.Empty, []);
        }

        var baseAddress = addressedBlocks.Min(static item => item.Address);
        var endExclusive = addressedBlocks.Max(static item => item.Address + item.Data.Length);
        var image = Enumerable.Repeat((byte)0xFF, endExclusive - baseAddress).ToArray();
        var populated = new bool[image.Length];

        foreach (var item in addressedBlocks)
        {
            var offset = item.Address - baseAddress;
            item.Data.CopyTo(image.AsSpan(offset));
            for (var index = 0; index < item.Data.Length; index++)
            {
                populated[offset + index] = true;
            }
        }

        var gaps = FindGaps(baseAddress, populated);
        var sha256 = Convert.ToHexString(SHA256.HashData(image));
        var strings = ExtractAsciiStrings(image);
        return new ReassembledImageResult(baseAddress, image, addressedBlocks.Length, gaps, sha256, strings);
    }

    public static int ParseAddress(string selectorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorHex);
        if (selectorHex.Length < 6)
        {
            throw new ArgumentException("SelectorHex must contain at least three address bytes.", nameof(selectorHex));
        }

        var low = selectorHex[..2];
        var mid = selectorHex.Substring(2, 2);
        var high = selectorHex.Substring(4, 2);
        return Convert.ToInt32(high + mid + low, 16);
    }

    private static IReadOnlyList<ReassembledImageGap> FindGaps(int baseAddress, bool[] populated)
    {
        var gaps = new List<ReassembledImageGap>();
        var index = 0;
        while (index < populated.Length)
        {
            if (populated[index])
            {
                index++;
                continue;
            }

            var start = index;
            while (index < populated.Length && !populated[index])
            {
                index++;
            }

            gaps.Add(new ReassembledImageGap(baseAddress + start, index - start));
        }

        return gaps;
    }

    private static IReadOnlyList<string> ExtractAsciiStrings(ReadOnlySpan<byte> bytes)
    {
        var strings = new List<string>();
        var builder = new StringBuilder();
        foreach (var value in bytes)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                builder.Append((char)value);
                continue;
            }

            Flush(builder, strings);
        }

        Flush(builder, strings);
        return strings
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static void Flush(StringBuilder builder, List<string> strings)
    {
        if (builder.Length >= 4)
        {
            strings.Add(builder.ToString());
        }

        builder.Clear();
    }
}
