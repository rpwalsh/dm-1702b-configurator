using Bao1702.Transport.Diagnostics;

namespace Bao1702.Transport.Framing;

/// <summary>
/// Transport framing codec for the DM-1702 USB communication protocol.
/// Encodes and decodes length-prefixed frames with checksum validation.
/// </summary>
/// <remarks>
/// <para>Frame layout:</para>
/// <list type="bullet">
///   <item>Byte 0: sync marker (0xA5)</item>
///   <item>Bytes 1–2: payload length, little-endian</item>
///   <item>Bytes 3..N: payload</item>
///   <item>Last byte: Sum8 checksum over length + payload bytes</item>
/// </list>
/// </remarks>
public static class TransportFrameCodec
{
    public const byte SyncByte = 0xA5;
    public const int HeaderLength = 3;
    public const int TrailerLength = 1;

    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        var frame = new byte[HeaderLength + payload.Length + TrailerLength];
        frame[0] = SyncByte;
        BitConverter.TryWriteBytes(frame.AsSpan(1, 2), (ushort)payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderLength));
        frame[^1] = Checksums.ComputeSum8(frame.AsSpan(1, frame.Length - 2));
        return frame;
    }

    public static bool TryDecode(ReadOnlySpan<byte> frame, out byte[] payload, out string error)
    {
        payload = [];
        error = string.Empty;

        if (frame.Length < HeaderLength + TrailerLength)
        {
            error = "Frame too short.";
            return false;
        }

        if (frame[0] != SyncByte)
        {
            error = $"Unexpected sync byte 0x{frame[0]:X2}.";
            return false;
        }

        var payloadLength = BitConverter.ToUInt16(frame.Slice(1, 2));
        var expectedLength = HeaderLength + payloadLength + TrailerLength;
        if (frame.Length != expectedLength)
        {
            error = $"Frame length mismatch. Expected {expectedLength} bytes, got {frame.Length}.";
            return false;
        }

        var expectedChecksum = Checksums.ComputeSum8(frame.Slice(1, frame.Length - 2));
        if (frame[^1] != expectedChecksum)
        {
            error = $"Checksum mismatch. Expected 0x{expectedChecksum:X2}, got 0x{frame[^1]:X2}.";
            return false;
        }

        payload = frame.Slice(HeaderLength, payloadLength).ToArray();
        return true;
    }

    public static string Describe(ReadOnlySpan<byte> frame)
    {
        return HexDump.Format(frame);
    }
}
