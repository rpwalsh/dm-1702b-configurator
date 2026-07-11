using System.Buffers.Binary;
using System.Collections.Immutable;

namespace Bao1702.Protocol.Packets;

/// <summary>
/// Represents a single DM-1702 protocol packet with command, flags, address, and payload.
/// Uses <see cref="ImmutableArray{T}"/> for payload to preserve record value equality semantics.
/// </summary>
public sealed record Bao1702Packet(byte CommandId, byte Flags, ushort Address, ImmutableArray<byte> Payload)
{
    public ushort PayloadLength => checked((ushort)Payload.Length);
}

/// <summary>
/// Protocol command identifiers for DM-1702 USB communication.
/// </summary>
public static class Bao1702CommandIds
{
    public const byte ReadRadioInfo = 0x01;
    public const byte EnterProgrammingMode = 0x10;
    public const byte ExitProgrammingMode = 0x11;
    public const byte ReadCodeplugBlock = 0x20;
    public const byte WriteCodeplugBlock = 0x21;
    public const byte ReadFirmwareBlock = 0x30;
    public const byte WriteFirmwareBlock = 0x31;
    public const byte ReadRtc = 0x40;
    public const byte WriteRtc = 0x41;
}

public static class Bao1702PacketSerializer
{
    public static byte[] Serialize(Bao1702Packet packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var buffer = new byte[6 + packet.Payload.Length];
        buffer[0] = packet.CommandId;
        buffer[1] = packet.Flags;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), packet.Address);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), packet.PayloadLength);
        packet.Payload.AsSpan().CopyTo(buffer.AsSpan(6));
        return buffer;
    }

    public static Bao1702Packet Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 6)
        {
            throw new InvalidDataException("Protocol packet is shorter than the minimum header size.");
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2));
        if (buffer.Length != 6 + length)
        {
            throw new InvalidDataException($"Protocol packet length mismatch. Expected {6 + length} bytes, got {buffer.Length}.");
        }

        return new Bao1702Packet(
            buffer[0],
            buffer[1],
            BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2)),
            [.. buffer.Slice(6, length)]);
    }
}
