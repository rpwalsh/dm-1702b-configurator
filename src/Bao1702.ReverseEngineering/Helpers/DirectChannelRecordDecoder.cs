using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Raw decoded channel record with direct byte-level field values before semantic mapping.</summary>
public sealed record DirectChannelRecord(
    int Index,
    int Offset,
    double RxFrequencyMHz,
    double TxFrequencyMHz,
    int ModeOrFlags,
    string Evidence,
    CodeplugConfidence Confidence);

public static class DirectChannelRecordDecoder
{
    public static IReadOnlyList<DirectChannelRecord> Decode(ReadOnlySpan<byte> image, int startOffset, int count, int stride)
    {
        if (startOffset < 0 || count < 0 || stride <= 0)
        {
            throw new ArgumentOutOfRangeException();
        }

        var records = new List<DirectChannelRecord>();
        for (var index = 0; index < count; index++)
        {
            var offset = startOffset + (index * stride);
            if (offset + 8 > image.Length)
            {
                break;
            }

            if (!TryDecodeLittleEndianPackedFrequency(image.Slice(offset, 4), out var rxFrequencyMHz)
                || !TryDecodeLittleEndianPackedFrequency(image.Slice(offset + 4, 4), out var txFrequencyMHz))
            {
                continue;
            }

            if (!IsPlausibleFrequency(rxFrequencyMHz) || !IsPlausibleFrequency(txFrequencyMHz))
            {
                continue;
            }

            var flags = offset + 8 < image.Length ? image[offset + 8] : 0;
            records.Add(new DirectChannelRecord(
                index,
                offset,
                rxFrequencyMHz,
                txFrequencyMHz,
                flags,
                "Decoded from repeated 0x30-byte record structure using the edited saved-codeplug frequency delta as evidence.",
                CodeplugConfidence.RequiresHardwareVerification));
        }

        return records;
    }

    public static bool TryDecodeLittleEndianPackedFrequency(ReadOnlySpan<byte> bytes, out double frequencyMHz)
    {
        frequencyMHz = 0d;
        if (bytes.Length != 4)
        {
            return false;
        }

        var raw = BitConverter.ToUInt32(bytes);
        if (raw == 0 || raw > 99_999_999)
        {
            return false;
        }

        frequencyMHz = raw / 100000d;
        return true;
    }

    private static bool IsPlausibleFrequency(double value)
        => (value >= 108d && value <= 174d) || (value >= 400d && value <= 520d);
}
