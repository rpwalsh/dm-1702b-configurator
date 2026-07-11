using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Fully decoded channel record with semantic field mapping (frequencies, flags, indices).</summary>
public sealed record ObservedChannelRecord(
    int Index,
    int Offset,
    string? Name,
    double RxFrequencyMHz,
    double TxFrequencyMHz,
    int Byte4,
    int Byte8,
    string Evidence,
    CodeplugConfidence Confidence);

/// <summary>
/// Diagnostic decoder for channel records in native images.
/// Decodes all channel records (linear indices 0-84, paged indices 85+) with correct
/// name table pairing from the contiguous name region at 0x4000 + index*11.
/// Paged:  overflow pages at 0xF000+ (indices 85-371).
/// </summary>
public static class ObservedChannelRecordDecoder
{
    private const int RecordStride = Dm1702NativeImageAssumptions.ChannelRecordStride; // 0x30
    private const int NameStride = Dm1702NativeImageAssumptions.ChannelNameStride;     // 11
    private const int MaxChannels = Dm1702NativeImageAssumptions.MaxSupportedPatchedChannelIndex + 1; // 169

    public static IReadOnlyList<ObservedChannelRecord> DecodeAllChannels(ReadOnlySpan<byte> image)
    {
        var results = new List<ObservedChannelRecord>();
        for (var index = 0; index < MaxChannels; index++)
        {
            var recordOffset = Dm1702NativeImageAssumptions.GetChannelRecordOffset(index);
            var nameOffset = Dm1702NativeImageAssumptions.GetChannelNameOffset(index);

            if (recordOffset + 8 > image.Length || nameOffset + NameStride > image.Length)
            {
                break;
            }

            if (!TryDecodeObservedFrequency(image.Slice(recordOffset, 4), out var rx)
                || !TryDecodeObservedFrequency(image.Slice(recordOffset + 4, 4), out var tx))
            {
                continue;
            }

            var nameBytes = image.Slice(nameOffset, NameStride).ToArray();
            var nullIndex = Array.IndexOf(nameBytes, (byte)0);
            var name = System.Text.Encoding.ASCII.GetString(nameBytes, 0, nullIndex >= 0 ? nullIndex : nameBytes.Length).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = null;
            }

            var region = index < Dm1702NativeImageAssumptions.LinearChannelCapacity ? "Linear" : "Paged";
            results.Add(new ObservedChannelRecord(
                index,
                recordOffset,
                name,
                rx,
                tx,
                image[recordOffset + 3],
                image[recordOffset + 7],
                $"{region} channel record at 0x{recordOffset:X4}, name at 0x{nameOffset:X4}.",
                CodeplugConfidence.Inferred));
        }

        return results;
    }

    public static bool TryDecodeObservedFrequency(ReadOnlySpan<byte> bytes, out double frequencyMHz)
    {
        frequencyMHz = 0d;
        if (bytes.Length != 4)
        {
            return false;
        }

        // OEM encoding: 4-byte little-endian packed BCD. data[3]=MSB pair, data[0]=LSB pair.
        // Read big-endian to get the digit string, then parse as decimal BCD.
        var digits = $"{bytes[3]:X2}{bytes[2]:X2}{bytes[1]:X2}{bytes[0]:X2}";
        if (digits.Any(static character => character is < '0' or > '9'))
        {
            return false;
        }

        if (!int.TryParse(digits, out var raw))
        {
            return false;
        }

        frequencyMHz = raw / 100000d;
        return true;
    }
}
