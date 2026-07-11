using Bao1702.Protocol.Model;
using Bao1702.Protocol.Packets;
using Bao1702.Protocol.Safety;
using Bao1702.Transport.Abstractions;

namespace Bao1702.Protocol.Stock;

/// <summary>
/// Tracks the lifecycle state of a stock CPS session with the radio.
/// </summary>
public enum StockCpsSessionState
{
    /// <summary>Session created but handshake not yet performed.</summary>
    Created,

    /// <summary>Transport is connected and handshake has completed successfully.</summary>
    Ready,

    /// <summary>Session has been disposed and can no longer be used.</summary>
    Disposed,
}

/// <summary>
/// Manages a live CPS session with a connected DM-1702 radio, providing
/// codeplug read, write, and radio information operations over the transport layer.
/// </summary>
public sealed class StockCpsSession : IAsyncDisposable
{
    private readonly ITransportConnection _connection;
    private readonly SafetyPolicyEngine _safetyPolicyEngine;
    private StockCpsSessionState _state;
    private string? _bootstrapIdentity;
    private byte _upperSectorByte;

    public StockCpsSession(ITransportConnection connection, SafetyPolicyEngine? safetyPolicyEngine = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _safetyPolicyEngine = safetyPolicyEngine ?? new SafetyPolicyEngine();
        _state = StockCpsSessionState.Created;
    }

    /// <summary>Gets the current session lifecycle state.</summary>
    public StockCpsSessionState State => _state;

    public async Task<RadioInfoResult> ReadRadioInfoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var responsesByStep = new Dictionary<string, List<byte[]>>(StringComparer.Ordinal);
        foreach (var step in StockCpsSessionBootstrap.ReadRadioInfoSteps)
        {
            var responses = await ExecuteStepAsync(step, cancellationToken).ConfigureAwait(false);
            responsesByStep[step.Name] = responses;
        }

        var payload = StockCpsRadioInfoParser.Parse(
            responsesByStep["PSEARCH"][0],
            [responsesByStep["G0000"][0], responsesByStep["G0040"][0], responsesByStep["G0080"][0], responsesByStep["G00C0"][0]],
            [responsesByStep["V0010"][1], responsesByStep["V0020"][1], responsesByStep["V0000"][0]]);

        // Cache values needed for the transfer-mode transition after the handshake.
        // PSEARCH response is ACK (0x06) + ASCII identity (e.g. "DMR1702").
        var psearchResp = responsesByStep["PSEARCH"][0];
        _bootstrapIdentity = System.Text.Encoding.ASCII.GetString(psearchResp.AsSpan(1));

        // V0000 continuation (response index [1]) is 8 bytes; byte at offset 6 is the
        // upper-sector limit used by the OEM enter-transfer-mode and probe-read sequences.
        var v0000Continuation = responsesByStep["V0000"][1];
        _upperSectorByte = v0000Continuation.Length > 6 ? v0000Continuation[6] : (byte)0x0C;

        var identity = StockCpsRadioInfoParser.ToRadioIdentity(payload);
        var compatibility = _safetyPolicyEngine.EvaluateCompatibility(identity);

        _state = StockCpsSessionState.Ready;
        return new RadioInfoResult(identity, compatibility);
    }

    /// <summary>
    /// Reads the full codeplug from the radio and returns it as a 245,760-byte native
    /// .data image compatible with the OEM CPS software.
    /// </summary>
    public async Task<byte[]> ReadCodeplugAsync(IProgress<ProtocolProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        // ── OEM transition sequence (handshake → transfer mode) ──────────────
        await EnterTransferModeAsync(cancellationToken).ConfigureAwait(false);

        // ── OEM probe reads (1-byte reads across 4 KiB page boundaries) ─────
        // The OEM CPS reads a single byte from the last address of each 4 KiB
        // page before beginning the 64-byte block reads.
        foreach (var probeAddr in StockCpsSessionBootstrap.EnumerateProbeAddresses(_upperSectorByte))
        {
            var (pLo, pMid, pHi, _) = StockCpsSessionBootstrap.EncodeAddress(probeAddr);
            byte[] probeCmd = [0x52, pLo, pMid, pHi, 0x01];

            await _connection.WriteAsync(probeCmd, cancellationToken).ConfigureAwait(false);

            // Response: W-header (0x57) + 3 addr bytes + length (0x01) + 1 data byte = 6 bytes
            var probeResp = await ReadExpectedPayloadAsync(6, $"Probe 0x{probeAddr:X6}", cancellationToken).ConfigureAwait(false);
            if (probeResp.Length < 6 || probeResp[0] != 0x57)
            {
                throw new ProtocolException($"Expected 6-byte W-response for probe read at 0x{probeAddr:X6}, but received {probeResp.Length} byte(s).");
            }

            await _connection.WriteAsync(StockCpsPackets.Ack, cancellationToken).ConfigureAwait(false);
            var probeAck = await ReadExpectedPayloadAsync(1, $"Probe 0x{probeAddr:X6}:ACK", cancellationToken).ConfigureAwait(false);
            if (probeAck.Length != 1 || probeAck[0] != 0x06)
            {
                throw new ProtocolException($"Expected ACK after probe read at 0x{probeAddr:X6}, but received {StockCpsPackets.Format(probeAck)}.");
            }
        }

        // ── 64-byte block reads (codeplug data) ─────────────────────────────
        var pageSize = StockCpsSessionBootstrap.ObservedCodeplugPageSize;
        var totalBlocks = StockCpsSessionBootstrap.ObservedReadTotalBlocks;
        var packedSize = StockCpsSessionBootstrap.ObservedReadImageSize;
        var packed = new byte[packedSize];
        var blockNumber = 0;

        foreach (var (address, imageOffset) in StockCpsSessionBootstrap.EnumerateBlocks(StockCpsSessionBootstrap.ObservedReadSegments))
        {
            var (selectorLow, selectorMid, selectorHigh, length) = StockCpsSessionBootstrap.EncodeAddress(address);
            var rCommand = StockCpsSessionBootstrap.BuildRReadCommand(selectorLow, selectorMid, selectorHigh, length);

            _connection.TraceSink?.TraceMessage(
                TransportTraceLevel.Debug,
                TransportTraceDirection.ToDevice,
                $"Stock CPS R-read block {blockNumber}/{totalBlocks} addr=0x{address:X6} -> {StockCpsPackets.Format(rCommand)}",
                rCommand);

            await _connection.WriteAsync(rCommand, cancellationToken).ConfigureAwait(false);

            var wResponse = await ReadExpectedPayloadAsync(pageSize + 5, $"R-read block {blockNumber}", cancellationToken).ConfigureAwait(false);
            if (wResponse.Length < pageSize + 5 || wResponse[0] != 0x57)
            {
                throw new ProtocolException($"Expected W-response with {pageSize + 5} bytes for codeplug block {blockNumber}, but received {wResponse.Length} byte(s) starting with 0x{(wResponse.Length > 0 ? wResponse[0] : 0):X2}.");
            }

            wResponse.AsSpan(5, pageSize).CopyTo(packed.AsSpan(imageOffset, pageSize));

            await _connection.WriteAsync(StockCpsPackets.Ack, cancellationToken).ConfigureAwait(false);
            var ackResponse = await ReadExpectedPayloadAsync(1, $"R-read block {blockNumber}:ACK", cancellationToken).ConfigureAwait(false);
            if (ackResponse.Length != 1 || ackResponse[0] != 0x06)
            {
                throw new ProtocolException($"Expected ACK after codeplug block {blockNumber} read, but received {StockCpsPackets.Format(ackResponse)}.");
            }

            blockNumber++;
            progress?.Report(new ProtocolProgress("ReadCodeplug", blockNumber, totalBlocks, blockNumber * pageSize, packedSize));
        }

        return StockCpsSessionBootstrap.BuildNativeImage(packed);
    }

    /// <summary>
    /// Writes a full codeplug image to the radio using the stock CPS write protocol.
    /// Accepts a 245,760-byte native .data image; extracts the writable segments automatically.
    /// OEM captured write protocol: for each block the host sends a 69-byte W-command
    /// (0x57 + 3 address bytes + 0x40 length + 64 data bytes), and the radio responds
    /// with a single 0x06 ACK. No host ACK is sent between writes.
    /// Write addresses use the same segment map as reads, minus the read-only segment at 0x055000.
    /// </summary>
    public async Task WriteCodeplugAsync(byte[] nativeImage, IProgress<ProtocolProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nativeImage);
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        if (nativeImage.Length != StockCpsSessionBootstrap.NativeImageLength)
        {
            throw new ProtocolException(
                $"Write image must be exactly {StockCpsSessionBootstrap.NativeImageLength:N0} bytes " +
                $"(native .data format), but received {nativeImage.Length:N0} bytes.");
        }

        // ── Re-establish PC mode before writing ─────────────────────────────
        // When the user reads a codeplug, edits it on the PC, and then writes,
        // the radio will have timed out of PC mode. The OEM CPS performs a full
        // handshake + transition for every session (read or write).  Re-run the
        // handshake to bring the radio back into transfer mode.  The cached
        // _bootstrapIdentity and _upperSectorByte are refreshed by this call.
        //
        // When the session is already Ready (ReadRadioInfoAsync was called in the
        // same session), the radio is still in PC mode and the bootstrap data is
        // already cached — skip the re-handshake entirely.  Sending a second
        // PSEARCH while the radio is in PC mode causes a USB I/O error.
        if (_state != StockCpsSessionState.Ready)
        {
            await ReHandshakeForWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        // ── OEM transition sequence (identical to ReadCodeplugAsync) ─────────
        await EnterTransferModeAsync(cancellationToken).ConfigureAwait(false);

        // ── OEM pre-write probe reads ───────────────────────────────────────
        // The OEM CPS sends another round of 200 single-byte probe reads at
        // 4 KiB page boundaries after the block reads and before writing.
        foreach (var probeAddr in StockCpsSessionBootstrap.EnumerateProbeAddresses(_upperSectorByte))
        {
            var (pLo, pMid, pHi, _) = StockCpsSessionBootstrap.EncodeAddress(probeAddr);
            byte[] probeCmd = [0x52, pLo, pMid, pHi, 0x01];

            await _connection.WriteAsync(probeCmd, cancellationToken).ConfigureAwait(false);

            var probeResp = await ReadExpectedPayloadAsync(6, $"WriteProbe 0x{probeAddr:X6}", cancellationToken).ConfigureAwait(false);
            if (probeResp.Length < 6 || probeResp[0] != 0x57)
            {
                throw new ProtocolException($"Expected 6-byte W-response for write-probe at 0x{probeAddr:X6}, but received {probeResp.Length} byte(s).");
            }

            await _connection.WriteAsync(StockCpsPackets.Ack, cancellationToken).ConfigureAwait(false);
            var probeAck = await ReadExpectedPayloadAsync(1, $"WriteProbe 0x{probeAddr:X6}:ACK", cancellationToken).ConfigureAwait(false);
            if (probeAck.Length != 1 || probeAck[0] != 0x06)
            {
                throw new ProtocolException($"Expected ACK after write-probe at 0x{probeAddr:X6}, but received {StockCpsPackets.Format(probeAck)}.");
            }
        }

        // ── 64-byte block writes (codeplug data) ────────────────────────────
        var packed = StockCpsSessionBootstrap.ExtractPackedWriteImage(nativeImage);

        var pageSize = StockCpsSessionBootstrap.ObservedCodeplugPageSize;
        var totalBlocks = StockCpsSessionBootstrap.ObservedWriteTotalBlocks;
        var blockNumber = 0;

        foreach (var (address, imageOffset) in StockCpsSessionBootstrap.EnumerateBlocks(StockCpsSessionBootstrap.ObservedWriteSegments))
        {
            var (selectorLow, selectorMid, selectorHigh, length) = StockCpsSessionBootstrap.EncodeAddress(address);
            var pageData = packed.AsSpan(imageOffset, pageSize);
            var wCommand = StockCpsPackets.BuildWWriteCommand(selectorLow, selectorMid, selectorHigh, length, pageData);

            _connection.TraceSink?.TraceMessage(
                TransportTraceLevel.Debug,
                TransportTraceDirection.ToDevice,
                $"Stock CPS W-write block {blockNumber}/{totalBlocks} addr=0x{address:X6}",
                wCommand);

            await _connection.WriteAsync(wCommand, cancellationToken).ConfigureAwait(false);

            var ack = await ReadExpectedPayloadAsync(1, $"W-write block {blockNumber}:ACK", cancellationToken).ConfigureAwait(false);
            if (ack.Length != 1 || ack[0] != 0x06)
            {
                throw new ProtocolException(
                    $"Expected ACK (0x06) after write block {blockNumber}, " +
                    $"but received {StockCpsPackets.Format(ack)}.");
            }

            blockNumber++;
            progress?.Report(new ProtocolProgress("WriteCodeplug", blockNumber, totalBlocks, blockNumber * pageSize, packed.Length));
        }
    }

    /// <summary>
    /// Re-runs the full PSEARCH/PASSSTA/SYSINFO/G/V handshake so the radio re-enters
    /// PC mode after a session timeout.  Refreshes <c>_bootstrapIdentity</c> and
    /// <c>_upperSectorByte</c> from the new handshake responses.
    /// The transport is reset (close + reopen) first because the radio drops the
    /// USB endpoint when it times out of PC mode — the old handle becomes stale.
    /// Only called when the session is NOT in Ready state (i.e. the radio has timed
    /// out of PC mode since the last handshake).
    /// </summary>
    private async Task ReHandshakeForWriteAsync(CancellationToken cancellationToken)
    {
        // The radio dropped out of PC mode while the user edited on the PC.
        // The Win32 USB handle is still technically "open" but writes will
        // fail with ERROR_ACCESS_DENIED.  Reset the transport to get a fresh handle.
        await _connection.ResetAsync(cancellationToken).ConfigureAwait(false);

        var responsesByStep = new Dictionary<string, List<byte[]>>(StringComparer.Ordinal);
        foreach (var step in StockCpsSessionBootstrap.ReadRadioInfoSteps)
        {
            var responses = await ExecuteStepAsync(step, cancellationToken).ConfigureAwait(false);
            responsesByStep[step.Name] = responses;
        }

        var psearchResp = responsesByStep["PSEARCH"][0];
        _bootstrapIdentity = System.Text.Encoding.ASCII.GetString(psearchResp.AsSpan(1));

        var v0000Continuation = responsesByStep["V0000"][1];
        _upperSectorByte = v0000Continuation.Length > 6 ? v0000Continuation[6] : (byte)0x0C;
    }

    /// <summary>
    /// Sends the OEM transition sequence that moves the radio from handshake
    /// mode into block-transfer mode:
    ///   1. FF FF FF FF {upperSector} — enter transfer mode (no response)
    ///   2. {bootstrapIdentity} echo  — radio responds with ACK (0x06)
    ///   3. 02 start-read             — radio responds with 8 bytes (all 0xFF)
    ///   4. ACK 06                    — radio responds with ACK (0x06)
    /// </summary>
    private async Task EnterTransferModeAsync(CancellationToken cancellationToken)
    {
        var enterTransfer = StockCpsPackets.BuildEnterTransferMode(_upperSectorByte);
        _connection.TraceSink?.TraceMessage(
            TransportTraceLevel.Information,
            TransportTraceDirection.ToDevice,
            $"Stock CPS enter-transfer-mode -> {StockCpsPackets.Format(enterTransfer)}",
            enterTransfer);
        await _connection.WriteAsync(enterTransfer, cancellationToken).ConfigureAwait(false);

        var identityEcho = StockCpsPackets.BuildIdentityEcho(_bootstrapIdentity ?? "DMR1702");
        _connection.TraceSink?.TraceMessage(
            TransportTraceLevel.Information,
            TransportTraceDirection.ToDevice,
            $"Stock CPS identity-echo -> {StockCpsPackets.Format(identityEcho)}",
            identityEcho);
        await _connection.WriteAsync(identityEcho, cancellationToken).ConfigureAwait(false);
        var identityAck = await ReadExpectedPayloadAsync(1, "IdentityEcho:ACK", cancellationToken).ConfigureAwait(false);
        if (identityAck.Length != 1 || identityAck[0] != 0x06)
        {
            throw new ProtocolException($"Expected ACK after identity echo, but received {StockCpsPackets.Format(identityAck)}.");
        }

        _connection.TraceSink?.TraceMessage(
            TransportTraceLevel.Information,
            TransportTraceDirection.ToDevice,
            "Stock CPS start-read -> 02",
            StockCpsPackets.StartRead.ToArray());
        await _connection.WriteAsync(StockCpsPackets.StartRead, cancellationToken).ConfigureAwait(false);
        var startReadResp = await ReadExpectedPayloadAsync(8, "StartRead", cancellationToken).ConfigureAwait(false);

        await _connection.WriteAsync(StockCpsPackets.Ack, cancellationToken).ConfigureAwait(false);
        var startReadAck = await ReadExpectedPayloadAsync(1, "StartRead:ACK", cancellationToken).ConfigureAwait(false);
        if (startReadAck.Length != 1 || startReadAck[0] != 0x06)
        {
            throw new ProtocolException($"Expected ACK after start-read, but received {StockCpsPackets.Format(startReadAck)}.");
        }
    }

    private async Task<List<byte[]>> ExecuteStepAsync(StockCpsStartupStep step, CancellationToken cancellationToken)
    {
        _connection.TraceSink?.TraceMessage(
            TransportTraceLevel.Information,
            TransportTraceDirection.Internal,
            $"Stock CPS step {step.Name} -> {StockCpsPackets.Format(step.Request.Span)}",
            step.Request.ToArray());

        await _connection.WriteAsync(step.Request, cancellationToken).ConfigureAwait(false);
        var responses = new List<byte[]>(step.ExpectedResponseLengths.Length);
        foreach (var expectedLength in step.ExpectedResponseLengths)
        {
            var response = await ReadExpectedPayloadAsync(expectedLength, step.Name, cancellationToken).ConfigureAwait(false);
            responses.Add(response);
        }

        if (step.RequiresAckRoundTripAfterResponses)
        {
            await _connection.WriteAsync(StockCpsPackets.Ack, cancellationToken).ConfigureAwait(false);
            _connection.TraceSink?.TraceMessage(
                TransportTraceLevel.Information,
                TransportTraceDirection.ToDevice,
                $"Stock CPS step {step.Name} ACK -> 06",
                StockCpsPackets.Ack.ToArray());

            var ackResponse = await ReadExpectedPayloadAsync(1, step.Name + ":ACK", cancellationToken).ConfigureAwait(false);
            if (ackResponse.Length != 1 || ackResponse[0] != 0x06)
            {
                throw new ProtocolException($"Expected single-byte ACK after step {step.Name}, but received {StockCpsPackets.Format(ackResponse)}.");
            }
        }

        return responses;
    }

    private async Task<byte[]> ReadExpectedPayloadAsync(int expectedLength, string stepName, CancellationToken cancellationToken)
    {
        var buffer = new byte[expectedLength];
        var total = 0;

        while (total < expectedLength)
        {
            var read = await _connection.ReadAsync(buffer.AsMemory(total, expectedLength - total), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                if (total == 0)
                {
                    throw new TimeoutException($"Timed out waiting for stock CPS response payload for step {stepName}.");
                }

                break;
            }

            total += read;
        }

        var payload = buffer.AsSpan(0, total).ToArray();
        _connection.TraceSink?.TraceMessage(
            TransportTraceLevel.Information,
            TransportTraceDirection.FromDevice,
            $"Stock CPS step {stepName} response <- {StockCpsPackets.Format(payload)}",
            payload);

        if (expectedLength > 0 && payload.Length != expectedLength)
        {
            throw new ProtocolException($"Expected {expectedLength} byte(s) for stock CPS step {stepName}, but received {payload.Length}.");
        }

        return payload;
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (!_connection.IsOpen)
        {
            await _connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_state == StockCpsSessionState.Disposed, this);
    }

    public ValueTask DisposeAsync()
    {
        _state = StockCpsSessionState.Disposed;
        return _connection.DisposeAsync();
    }
}
