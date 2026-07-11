using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Packets;
using Bao1702.Transport.Framing;

namespace Bao1702.Protocol;

/// <summary>
/// In-memory simulated DM-1702 radio for testing protocol sessions without hardware.
/// </summary>
public sealed class MockRadioDevice
{
    private readonly byte[] _codeplug;
    private readonly byte[] _firmware;
    private DateTimeOffset _rtc;

    public MockRadioDevice()
    {
        Identity = new RadioIdentity(
            RadioFamily.Bao1702,
            RadioVariant.Bao1702B,
            "Baofeng 1702B",
            new FirmwareVersion("V02.07.001"),
            new BootloaderVersion("BL01.03"),
            "1702B-SYNTHETIC-0001",
            new RadioCapabilities(true, true, true, false, true, ProtocolAssumptions.AssumedDefaultCodeplugSize, ProtocolAssumptions.AssumedDefaultFirmwareSize, ConfidenceLevel.Inferred),
            ConfidenceLevel.Inferred);

        _codeplug = Enumerable.Range(0, ProtocolAssumptions.AssumedDefaultCodeplugSize).Select(value => (byte)(value % 251)).ToArray();
        _firmware = Enumerable.Range(0, ProtocolAssumptions.AssumedDefaultFirmwareSize).Select(value => (byte)(255 - (value % 251))).ToArray();
        _rtc = DateTimeOffset.Parse("2025-01-01T00:00:00+00:00", null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    public RadioIdentity Identity { get; }

    public byte[] Handle(byte[] frame)
    {
        if (!TransportFrameCodec.TryDecode(frame, out var payload, out var error))
        {
            throw new ProtocolException($"Mock device received invalid frame: {error}");
        }

        var packet = Bao1702PacketSerializer.Deserialize(payload);
        var response = packet.CommandId switch
        {
            Bao1702CommandIds.ReadRadioInfo => HandleRadioInfo(packet),
            Bao1702CommandIds.EnterProgrammingMode => new Bao1702Packet(packet.CommandId, 0x00, 0, []),
            Bao1702CommandIds.ExitProgrammingMode => new Bao1702Packet(packet.CommandId, 0x00, 0, []),
            Bao1702CommandIds.ReadCodeplugBlock => HandleReadBlock(packet, _codeplug),
            Bao1702CommandIds.WriteCodeplugBlock => HandleWriteBlock(packet, _codeplug),
            Bao1702CommandIds.ReadFirmwareBlock => HandleReadBlock(packet, _firmware),
            Bao1702CommandIds.ReadRtc => HandleReadRtc(packet),
            Bao1702CommandIds.WriteRtc => HandleWriteRtc(packet),
            _ => throw new ProtocolException($"Mock device does not recognize command 0x{packet.CommandId:X2}."),
        };

        return TransportFrameCodec.Encode(Bao1702PacketSerializer.Serialize(response));
    }

    private Bao1702Packet HandleRadioInfo(Bao1702Packet packet)
    {
        var payload = Encoding.UTF8.GetBytes(string.Join('|',
            Identity.ModelName,
            Identity.Variant,
            Identity.FirmwareVersion.RawValue,
            Identity.BootloaderVersion.RawValue,
            Identity.SerialNumber ?? string.Empty));
        return new Bao1702Packet(packet.CommandId, 0x00, 0, [.. payload]);
    }

    private static Bao1702Packet HandleReadBlock(Bao1702Packet packet, byte[] source)
    {
        var address = packet.Address;
        var length = packet.Payload.Length >= 2
            ? BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan())
            : packet.PayloadLength;

        if (length == 0)
        {
            length = packet.PayloadLength;
        }

        if (address + length > source.Length)
        {
            throw new ProtocolException("Read exceeds available mock image.");
        }

        return new Bao1702Packet(packet.CommandId, 0x00, packet.Address, [.. source.AsSpan(address, length)]);
    }

    private static Bao1702Packet HandleWriteBlock(Bao1702Packet packet, byte[] destination)
    {
        if (packet.Address + packet.Payload.Length > destination.Length)
        {
            throw new ProtocolException("Write exceeds available mock image.");
        }

        packet.Payload.AsSpan().CopyTo(destination.AsSpan(packet.Address));
        return new Bao1702Packet(packet.CommandId, 0x00, packet.Address, []);
    }

    private Bao1702Packet HandleReadRtc(Bao1702Packet packet)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(payload, _rtc.ToUnixTimeSeconds());
        return new Bao1702Packet(packet.CommandId, 0x00, 0, [.. payload]);
    }

    private Bao1702Packet HandleWriteRtc(Bao1702Packet packet)
    {
        _rtc = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64LittleEndian(packet.Payload.AsSpan()));
        return new Bao1702Packet(packet.CommandId, 0x00, 0, []);
    }
}
