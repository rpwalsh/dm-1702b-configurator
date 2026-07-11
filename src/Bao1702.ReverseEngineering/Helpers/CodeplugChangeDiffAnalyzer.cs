namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>A single byte-level difference between two codeplug images.</summary>
public sealed record CodeplugByteChange(
    int Offset,
    byte Before,
    byte After);

public sealed record CodeplugChangedField(
    int Offset,
    string Category,
    string Summary,
    string Evidence);

public sealed record CodeplugChangeAnalysis(
    string OriginalPath,
    string ModifiedPath,
    int Length,
    IReadOnlyList<CodeplugByteChange> ByteChanges,
    IReadOnlyList<CodeplugChangedField> ChangedFields);

public static class CodeplugChangeDiffAnalyzer
{
    public static CodeplugChangeAnalysis Analyze(string originalPath, byte[] originalBytes, string modifiedPath, byte[] modifiedBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modifiedPath);
        ArgumentNullException.ThrowIfNull(originalBytes);
        ArgumentNullException.ThrowIfNull(modifiedBytes);

        if (originalBytes.Length != modifiedBytes.Length)
        {
            throw new InvalidOperationException("Saved codeplug diff requires equal-length files.");
        }

        var changes = new List<CodeplugByteChange>();
        for (var index = 0; index < originalBytes.Length; index++)
        {
            if (originalBytes[index] != modifiedBytes[index])
            {
                changes.Add(new CodeplugByteChange(index, originalBytes[index], modifiedBytes[index]));
            }
        }

        var fields = new List<CodeplugChangedField>();
        foreach (var change in changes)
        {
            if (change.Offset >= 0x4000 && change.Offset < 0x5000)
            {
                var slotIndex = (change.Offset - 0x4000) / 11;
                fields.Add(new CodeplugChangedField(
                    change.Offset,
                    "ChannelNameTable",
                    $"Channel-name table byte changed for slot {slotIndex}.",
                    "Offset falls within the first inferred 11-byte channel-name table region."));
                continue;
            }

            if (change.Offset >= 0x3012 && ((change.Offset - 0x3012) % 0x30) == 0)
            {
                var slotIndex = (change.Offset - 0x3012) / 0x30;
                fields.Add(new CodeplugChangedField(
                    change.Offset,
                    "ChannelRecordRxFrequency",
                    $"Likely packed RX frequency field changed for slot {slotIndex}.",
                    "Offset matches the edited slot-1 diff and repeats on a 0x30-byte stride."));
                continue;
            }

            if (change.Offset >= 0x5019)
            {
                fields.Add(new CodeplugChangedField(
                    change.Offset,
                    "MetadataOrTimestamp",
                    "Likely metadata/timestamp or derived field changed.",
                    "Observed modified bytes align with the saved-file timestamp region near the header metadata area."));
                continue;
            }

            fields.Add(new CodeplugChangedField(
                change.Offset,
                "Unknown",
                "Changed byte outside currently mapped regions.",
                "No current region mapping matches this offset."));
        }

        return new CodeplugChangeAnalysis(originalPath, modifiedPath, originalBytes.Length, changes, fields);
    }
}
