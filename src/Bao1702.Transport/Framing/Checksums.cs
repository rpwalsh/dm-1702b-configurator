namespace Bao1702.Transport.Framing;

/// <summary>
/// Checksum algorithms used by the DM-1702 transport framing layer (Sum8, CRC-16/CCITT).
/// </summary>
public static class Checksums
{
    public static byte ComputeSum8(ReadOnlySpan<byte> data)
    {
        var sum = 0;
        foreach (var value in data)
        {
            sum = (sum + value) & 0xFF;
        }

        return (byte)sum;
    }

    public static ushort ComputeCrc16Ccitt(ReadOnlySpan<byte> data, ushort seed = 0xFFFF)
    {
        ushort crc = seed;
        foreach (var value in data)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
            }
        }

        return crc;
    }
}
