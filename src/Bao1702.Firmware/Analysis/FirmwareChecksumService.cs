using System.Security.Cryptography;

namespace Bao1702.Firmware.Analysis;

/// <summary>
/// Computes integrity checksums (SHA-256, SUM16, XOR8) for firmware image bytes
/// and optionally validates header-declared checksums.
/// </summary>
public static class FirmwareChecksumService
{
    public static IReadOnlyList<FirmwareChecksum> Analyze(ReadOnlySpan<byte> bytes, FirmwareHeader? header = null)
    {
        var results = new List<FirmwareChecksum>
        {
            new("SHA256", Convert.ToHexString(SHA256.HashData(bytes)), true),
            new("SUM16", ComputeSum16(bytes).ToString("X4"), true),
            new("XOR8", ComputeXor8(bytes).ToString("X2"), true),
        };

        if (header is not null)
        {
            var payloadChecksum = bytes.Length > 10 ? ComputeSum16(bytes[10..]) : (ushort)0;
            results.Add(new FirmwareChecksum("HeaderDeclaredSUM16", header.HeaderChecksum.ToString("X4"), header.HeaderChecksum == payloadChecksum));
        }

        return results;
    }

    public static ushort ComputeSum16(ReadOnlySpan<byte> bytes)
    {
        var sum = 0;
        foreach (var value in bytes)
        {
            sum = (sum + value) & 0xFFFF;
        }

        return (ushort)sum;
    }

    public static byte ComputeXor8(ReadOnlySpan<byte> bytes)
    {
        byte value = 0;
        foreach (var current in bytes)
        {
            value ^= current;
        }

        return value;
    }
}
