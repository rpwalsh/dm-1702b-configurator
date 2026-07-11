using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Maps a captured write block to its corresponding file offset and content preview.</summary>
public sealed record WriteBlockFileMapping(
    int FrameNumber,
    int WriteAddress,
    int? FileOffset,
    string AsciiPreview,
    CodeplugConfidence Confidence);

public static class WriteBlockMapper
{
    public static IReadOnlyList<WriteBlockFileMapping> MapToSavedCodeplug(IReadOnlyList<WriteBlock> blocks, byte[] savedCodeplug)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(savedCodeplug);

        return blocks
            .Select(block =>
            {
                var fileOffset = FindUniqueSubsequence(savedCodeplug, block.Data);
                return new WriteBlockFileMapping(
                    block.FrameNumber,
                    block.Address,
                    fileOffset,
                    block.AsciiPreview,
                    fileOffset.HasValue ? CodeplugConfidence.Inferred : CodeplugConfidence.Unknown);
            })
            .ToArray();
    }

    private static int? FindUniqueSubsequence(byte[] haystack, byte[] needle)
    {
        var first = -1;
        var count = 0;
        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            var match = true;
            for (var inner = 0; inner < needle.Length; inner++)
            {
                if (haystack[index + inner] != needle[inner])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
            {
                continue;
            }

            if (first < 0)
            {
                first = index;
            }

            count++;
            if (count > 1)
            {
                return null;
            }
        }

        return first >= 0 ? first : null;
    }
}
