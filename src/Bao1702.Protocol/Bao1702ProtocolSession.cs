using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Packets;
using Bao1702.Protocol.Safety;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Framing;

namespace Bao1702.Protocol;

/// <summary>
/// Configuration options for a DM-1702 protocol session including safety policy and transfer block size.
/// </summary>
public sealed record ProtocolSessionOptions(
    SafetyPolicyOptions SafetyPolicy,
    int BlockSize)
{
    public static ProtocolSessionOptions Default { get; } = new(SafetyPolicyOptions.Default, ProtocolAssumptions.AssumedDefaultBlockSize);
}

public sealed class Bao1702ProtocolSession : IRadioProtocolSession
{
    private readonly ITransportConnection _connection;
    private readonly SafetyPolicyEngine _safetyPolicyEngine;
    private readonly ProtocolSessionOptions _options;
    private RadioIdentity? _cachedIdentity;

    public Bao1702ProtocolSession(
        ITransportConnection connection,
        SafetyPolicyEngine? safetyPolicyEngine = null,
        ProtocolSessionOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _safetyPolicyEngine = safetyPolicyEngine ?? new SafetyPolicyEngine();
        _options = options ?? ProtocolSessionOptions.Default;
    }

    public async Task<RadioInfoResult> ReadRadioInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await ExchangeAsync(new Bao1702Packet(Bao1702CommandIds.ReadRadioInfo, 0x00, 0, []), cancellationToken).ConfigureAwait(false);
        var fields = Encoding.UTF8.GetString(response.Payload.AsSpan()).Split('|');
        if (fields.Length < 4)
        {
            throw new ProtocolException("Radio info response did not contain the expected fields.");
        }

        var variant = Enum.TryParse<RadioVariant>(fields[1], ignoreCase: true, out var parsedVariant)
            ? parsedVariant
            : RadioVariant.Unknown;

        _cachedIdentity = new RadioIdentity(
            variant == RadioVariant.Unknown ? RadioFamily.Unknown : RadioFamily.Bao1702,
            variant,
            fields[0],
            new FirmwareVersion(fields[2]),
            new BootloaderVersion(fields[3]),
            fields.Length > 4 ? fields[4] : null,
            new RadioCapabilities(true, true, true, false, true, ProtocolAssumptions.AssumedDefaultCodeplugSize, ProtocolAssumptions.AssumedDefaultFirmwareSize, ConfidenceLevel.Inferred),
            variant == RadioVariant.Unknown ? ConfidenceLevel.RequiresHardwareVerification : ConfidenceLevel.Inferred);

        return new RadioInfoResult(_cachedIdentity, _safetyPolicyEngine.EvaluateCompatibility(_cachedIdentity));
    }

    public Task EnterProgrammingModeAsync(CancellationToken cancellationToken = default)
        => ExchangeNoPayloadAsync(Bao1702CommandIds.EnterProgrammingMode, cancellationToken);

    public Task ExitProgrammingModeAsync(CancellationToken cancellationToken = default)
        => ExchangeNoPayloadAsync(Bao1702CommandIds.ExitProgrammingMode, cancellationToken);

    public async Task<byte[]> ReadCodeplugBlockAsync(int address, int length, CancellationToken cancellationToken = default)
    {
        ValidateAddressAndLength(address, length);
        await EnsureReadAllowedAsync(RadioOperation.ReadCodeplug, cancellationToken).ConfigureAwait(false);
        var lengthBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(lengthBytes, (ushort)length);
        var request = new Bao1702Packet(Bao1702CommandIds.ReadCodeplugBlock, 0x00, checked((ushort)address), [.. lengthBytes]);
        var response = await ExchangeAsync(request, cancellationToken).ConfigureAwait(false);
        return [.. response.Payload];
    }

    public async Task WriteCodeplugBlockAsync(int address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ValidateAddressAndLength(address, data.Length);
        await EnsureWriteAllowedAsync(RadioOperation.WriteCodeplug, cancellationToken).ConfigureAwait(false);
        await ExchangeAsync(new Bao1702Packet(Bao1702CommandIds.WriteCodeplugBlock, 0x00, checked((ushort)address), [.. data.Span]), cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadFirmwareBlockAsync(int address, int length, CancellationToken cancellationToken = default)
    {
        ValidateAddressAndLength(address, length);
        await EnsureReadAllowedAsync(RadioOperation.ReadFirmware, cancellationToken).ConfigureAwait(false);
        var lengthBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(lengthBytes, (ushort)length);
        var request = new Bao1702Packet(Bao1702CommandIds.ReadFirmwareBlock, 0x00, checked((ushort)address), [.. lengthBytes]);
        var response = await ExchangeAsync(request, cancellationToken).ConfigureAwait(false);
        return [.. response.Payload];
    }

    public async Task WriteFirmwareBlockAsync(int address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ValidateAddressAndLength(address, data.Length);
        await EnsureWriteAllowedAsync(RadioOperation.WriteFirmware, cancellationToken).ConfigureAwait(false);
        await ExchangeAsync(new Bao1702Packet(Bao1702CommandIds.WriteFirmwareBlock, 0x00, checked((ushort)address), [.. data.Span]), cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadFullCodeplugAsync(CancellationToken cancellationToken = default)
        => await ReadFullCodeplugAsync(progress: null, cancellationToken).ConfigureAwait(false);

    public async Task<byte[]> ReadFullCodeplugAsync(IProgress<ProtocolProgress>? progress, CancellationToken cancellationToken = default)
    {
        var info = await ReadRadioInfoAsync(cancellationToken).ConfigureAwait(false);
        var totalSize = info.Identity.Capabilities.AssumedCodeplugSize;
        var totalBlocks = (totalSize + _options.BlockSize - 1) / _options.BlockSize;
        using var memory = new MemoryStream(totalSize);
        var blockIndex = 0;
        for (var address = 0; address < totalSize; address += _options.BlockSize)
        {
            var length = Math.Min(_options.BlockSize, totalSize - address);
            var block = await ReadCodeplugBlockAsync(address, length, cancellationToken).ConfigureAwait(false);
            memory.Write(block, 0, block.Length);
            blockIndex++;
            progress?.Report(new ProtocolProgress("ReadCodeplug", blockIndex, totalBlocks, address + length, totalSize));
        }

        return memory.ToArray();
    }

    public async Task WriteFullCodeplugAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken = default)
        => await WriteFullCodeplugAsync(image, progress: null, cancellationToken).ConfigureAwait(false);

    public async Task WriteFullCodeplugAsync(ReadOnlyMemory<byte> image, IProgress<ProtocolProgress>? progress, CancellationToken cancellationToken = default)
    {
        var info = await ReadRadioInfoAsync(cancellationToken).ConfigureAwait(false);
        if (image.Length != info.Identity.Capabilities.AssumedCodeplugSize)
        {
            throw new SafetyException($"Codeplug image length {image.Length} does not match expected size {info.Identity.Capabilities.AssumedCodeplugSize}.");
        }

        var totalBlocks = (image.Length + _options.BlockSize - 1) / _options.BlockSize;
        var blockIndex = 0;
        for (var address = 0; address < image.Length; address += _options.BlockSize)
        {
            var length = Math.Min(_options.BlockSize, image.Length - address);
            await WriteCodeplugBlockAsync(address, image.Slice(address, length), cancellationToken).ConfigureAwait(false);
            blockIndex++;
            progress?.Report(new ProtocolProgress("WriteCodeplug", blockIndex, totalBlocks, address + length, image.Length));
        }
    }

    public async Task<byte[]> BackupFirmwareAsync(CancellationToken cancellationToken = default)
        => await BackupFirmwareAsync(progress: null, cancellationToken).ConfigureAwait(false);

    public async Task<byte[]> BackupFirmwareAsync(IProgress<ProtocolProgress>? progress, CancellationToken cancellationToken = default)
    {
        var info = await ReadRadioInfoAsync(cancellationToken).ConfigureAwait(false);
        var totalSize = info.Identity.Capabilities.AssumedFirmwareSize;
        var totalBlocks = (totalSize + _options.BlockSize - 1) / _options.BlockSize;
        using var memory = new MemoryStream(totalSize);
        var blockIndex = 0;
        for (var address = 0; address < totalSize; address += _options.BlockSize)
        {
            var length = Math.Min(_options.BlockSize, totalSize - address);
            var block = await ReadFirmwareBlockAsync(address, length, cancellationToken).ConfigureAwait(false);
            memory.Write(block, 0, block.Length);
            blockIndex++;
            progress?.Report(new ProtocolProgress("BackupFirmware", blockIndex, totalBlocks, address + length, totalSize));
        }

        return memory.ToArray();
    }

    public async Task<DateTimeOffset> ReadRtcAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadAllowedAsync(RadioOperation.ReadRtc, cancellationToken).ConfigureAwait(false);
        var response = await ExchangeAsync(new Bao1702Packet(Bao1702CommandIds.ReadRtc, 0x00, 0, []), cancellationToken).ConfigureAwait(false);
        return DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64LittleEndian(response.Payload.AsSpan()));
    }

    public async Task WriteRtcAsync(DateTimeOffset value, CancellationToken cancellationToken = default)
    {
        await EnsureWriteAllowedAsync(RadioOperation.WriteRtc, cancellationToken).ConfigureAwait(false);
        var rtcBytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(rtcBytes, value.ToUnixTimeSeconds());
        await ExchangeAsync(new Bao1702Packet(Bao1702CommandIds.WriteRtc, 0x00, 0, [.. rtcBytes]), cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompatibilityResult> ValidateTargetCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        var info = await ReadRadioInfoAsync(cancellationToken).ConfigureAwait(false);
        return info.Compatibility;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task EnsureReadAllowedAsync(RadioOperation operation, CancellationToken cancellationToken)
    {
        var identity = await GetIdentityAsync(cancellationToken).ConfigureAwait(false);
        var decision = _safetyPolicyEngine.Evaluate(identity, operation, _options.SafetyPolicy);
        if (!decision.IsAllowed)
        {
            throw new SafetyException(string.Join(" ", new[] { decision.Summary }.Concat(decision.Reasons)));
        }
    }

    private async Task EnsureWriteAllowedAsync(RadioOperation operation, CancellationToken cancellationToken)
    {
        var identity = await GetIdentityAsync(cancellationToken).ConfigureAwait(false);
        var decision = _safetyPolicyEngine.Evaluate(identity, operation, _options.SafetyPolicy);
        if (!decision.IsAllowed)
        {
            throw new SafetyException(string.Join(" ", new[] { decision.Summary }.Concat(decision.Reasons)));
        }
    }

    private async Task<RadioIdentity> GetIdentityAsync(CancellationToken cancellationToken)
    {
        if (_cachedIdentity is not null)
        {
            return _cachedIdentity;
        }

        return (await ReadRadioInfoAsync(cancellationToken).ConfigureAwait(false)).Identity;
    }

    private async Task ExchangeNoPayloadAsync(byte commandId, CancellationToken cancellationToken)
    {
        await ExchangeAsync(new Bao1702Packet(commandId, 0x00, 0, []), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Bao1702Packet> ExchangeAsync(Bao1702Packet packet, CancellationToken cancellationToken)
    {
        var encodedPacket = Bao1702PacketSerializer.Serialize(packet);
        _connection.TraceSink?.TraceMessage(
            Bao1702.Transport.Abstractions.TransportTraceLevel.Information,
            Bao1702.Transport.Abstractions.TransportTraceDirection.Internal,
            $"Protocol request\n{Bao1702PacketTraceFormatter.Format(packet)}",
            encodedPacket);
        var framed = TransportFrameCodec.Encode(encodedPacket);
        var responseFrame = await _connection.ExchangeAsync(framed, cancellationToken).ConfigureAwait(false);
        if (!TransportFrameCodec.TryDecode(responseFrame, out var responsePayload, out var error))
        {
            throw new ProtocolException($"Unable to decode transport frame: {error}");
        }

        var responsePacket = Bao1702PacketSerializer.Deserialize(responsePayload);
        _connection.TraceSink?.TraceMessage(
            Bao1702.Transport.Abstractions.TransportTraceLevel.Information,
            Bao1702.Transport.Abstractions.TransportTraceDirection.Internal,
            $"Protocol response\n{Bao1702PacketTraceFormatter.Format(responsePacket)}",
            responsePayload);
        return responsePacket;
    }

    private static void ValidateAddressAndLength(int address, int length)
    {
        if (address < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(address));
        }

        if (length <= 0 || length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }
}
