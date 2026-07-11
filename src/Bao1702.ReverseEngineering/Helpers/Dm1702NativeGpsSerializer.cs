using System.Text;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// GPS system serializer. Stride 16 at 0x5500.
/// OEM evidence: baseline has 16 default entries ("GPS 1".."GPS 16") each 16 bytes.
///   Stock defaults: [0x09]=0x1B, [0x0C]=0x01, [0x0D]=entry# (1-based).
/// Strategy: write 16 factory-default entries, then overwrite entry 0 with user GPS config if callsign is set.
/// </summary>
public static class Dm1702NativeGpsSerializer
{
    private const int EntryStride = 0x10;
    private const int MaxEntries = 16;

    public static void Write(Span<byte> image, CodeplugImage model)
    {
        var gps = image.Slice(Dm1702NativeImageAssumptions.GpsSystemsStart, Dm1702NativeImageAssumptions.GpsSystemsLength);
        gps.Fill(0x00);

        // Write 16 factory-default GPS entries matching OEM baseline
        for (var i = 0; i < MaxEntries; i++)
        {
            var entry = gps.Slice(i * EntryStride, EntryStride);
            entry.Fill(0x00);
            WriteAscii(entry[..9], $"GPS {i + 1}", 9);
            entry[0x09] = 0x1B;
            entry[0x0C] = 0x01;
            entry[0x0D] = (byte)(i + 1);
        }

        // If the user has a callsign, overwrite entry 0 with their APRS config
        if (!string.IsNullOrWhiteSpace(model.RadioIdentity.Callsign)
            && !string.Equals(model.RadioIdentity.Callsign, "NOCALL", StringComparison.OrdinalIgnoreCase))
        {
            var entry0 = gps[..EntryStride];
            entry0.Fill(0x00);
            WriteAscii(entry0[..9], model.RadioIdentity.Callsign, 9);
            entry0[0x09] = 0x06;
            entry0[0x0C] = 0x02;
            entry0[0x0D] = 0x2F;
            entry0[0x0E] = 0xDA;
            entry0[0x0F] = 0x04;
        }
    }

    private static void WriteAscii(Span<byte> destination, string value, int maxLength)
    {
        destination.Fill(0x00);
        var bytes = Encoding.ASCII.GetBytes(value.Length >= maxLength ? value[..(maxLength - 1)] : value);
        bytes.CopyTo(destination);
    }
}
