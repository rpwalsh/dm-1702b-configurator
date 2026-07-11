using System.Collections.Concurrent;
using Bao1702.Protocol;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Stock;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.UsbPrinter;

namespace Bao1702.Tests.Protocol;

[TestClass]
public sealed class StockCpsSessionTests
{
    [TestMethod]
    public async Task ReadRadioInfoAsync_ReplaysObservedBootstrapAndParsesIdentity()
    {
        var responses = new[]
        {
            new byte[] { 0x06, (byte)'D', (byte)'M', (byte)'R', (byte)'1', (byte)'7', (byte)'0', (byte)'2' },
            new byte[] { 0x50, 0x00, 0x00 },
            new byte[] { 0x06 },
            new byte[] { 0x56, 0x0D, 0x0A },
            new byte[10],
            new byte[] { 0x06 },
            new byte[] { 0x56, 0x0D, 0x0A },
            new byte[] { 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            new byte[] { 0x06 },
            CreateSResponse(0x00, "DM-1702"),
            new byte[] { 0x06 },
            CreateSResponse(0x40, "DM1702-0103\0\0\0DM-1702-V1.4"),
            new byte[] { 0x06 },
            CreateSResponse(0x80, "\0\0\0\0"),
            new byte[] { 0x06 },
            CreateSResponse(0xC0, "\0\0\0\0"),
            new byte[] { 0x06 },
            new byte[] { 0x56, 0x0A, 0x08 },
            new byte[] { 0x00, 0x10, 0x00, 0x00, 0xFF, 0x8F, 0x0C, 0x00 },
            new byte[] { 0x06 },
        };

        var stream = new ScriptedDuplexStream(responses);
        var endpoint = new TransportEndpoint(
            "usbprint://test-radio",
            "USB Printer Test Radio",
            TransportType.UsbPrinter,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProbeOpenPath"] = "\\\\?\\usb#vid_0483&pid_5780#synthetic#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}",
            });

        await using var connection = new UsbPrinterTransportConnection(endpoint, TransportTimeouts.Default, () => stream);
        await connection.ConnectAsync().ConfigureAwait(false);
        await using var session = new StockCpsSession(connection);

        var result = await session.ReadRadioInfoAsync().ConfigureAwait(false);

        Assert.AreEqual(RadioFamily.Bao1702, result.Identity.Family);
        Assert.AreEqual(RadioVariant.Dm1702, result.Identity.Variant);
        Assert.AreEqual(ConfidenceLevel.Inferred, result.Identity.Confidence);
        Assert.AreEqual("DM-1702", result.Identity.ModelName);
        Assert.AreEqual("DM-1702-V1.4", result.Identity.FirmwareVersion.RawValue);
        Assert.AreEqual("DM1702-0103", result.Identity.SerialNumber);

        var writes = stream.Writes.ToArray();
        CollectionAssert.AreEqual("PSEARCH"u8.ToArray(), writes[0]);
        CollectionAssert.AreEqual("PASSSTA"u8.ToArray(), writes[1]);
        CollectionAssert.AreEqual("SYSINFO"u8.ToArray(), writes[2]);
        Assert.IsTrue(writes.Any(static write => write.SequenceEqual(new byte[] { 0x47, 0x00, 0x00, 0x00, 0x40 })), "Expected the stock session to emit the first observed G command.");
        Assert.IsTrue(writes.Any(static write => write.SequenceEqual(new byte[] { 0x56, 0x00, 0x00, 0x00, 0x0A })), "Expected the stock session to emit the V0000 PC-mode transition packet.");
    }

    [TestMethod]
    public async Task ReadRadioInfoAsync_AllowsNullTraceSink()
    {
        var responses = new[]
        {
            new byte[] { 0x06, (byte)'D', (byte)'M', (byte)'R', (byte)'1', (byte)'7', (byte)'0', (byte)'2' },
            new byte[] { 0x50, 0x00, 0x00 },
            new byte[] { 0x06 },
            new byte[] { 0x56, 0x0D, 0x0A },
            new byte[10],
            new byte[] { 0x06 },
            new byte[] { 0x56, 0x0D, 0x0A },
            new byte[] { 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            new byte[] { 0x06 },
            CreateSResponse(0x00, "DM-1702"),
            new byte[] { 0x06 },
            CreateSResponse(0x40, "DM1702-0103\0\0\0DM-1702-V1.4"),
            new byte[] { 0x06 },
            CreateSResponse(0x80, "\0\0\0\0"),
            new byte[] { 0x06 },
            CreateSResponse(0xC0, "\0\0\0\0"),
            new byte[] { 0x06 },
            new byte[] { 0x56, 0x0A, 0x08 },
            new byte[] { 0x00, 0x10, 0x00, 0x00, 0xFF, 0x8F, 0x0C, 0x00 },
            new byte[] { 0x06 },
        };

        var stream = new ScriptedDuplexStream(responses);
        var endpoint = new TransportEndpoint(
            "usbprint://test-radio-null-trace",
            "USB Printer Test Radio (Null Trace)",
            TransportType.UsbPrinter,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProbeOpenPath"] = "\\\\?\\usb#vid_0483&pid_5780#synthetic#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}",
            });

        await using var connection = new UsbPrinterTransportConnection(endpoint, TransportTimeouts.Default, () => stream);
        connection.TraceSink = null;
        await connection.ConnectAsync().ConfigureAwait(false);
        await using var session = new StockCpsSession(connection);

        var result = await session.ReadRadioInfoAsync().ConfigureAwait(false);

        Assert.AreEqual("DM-1702", result.Identity.ModelName);
    }

    [TestMethod]
    public async Task ReadRadioInfoAsync_InfersBao1702BWhenSpecificMarkerIsPresent()
    {
        var responses = new[]
        {
            new byte[] { 0x06, (byte)'D', (byte)'M', (byte)'R', (byte)'1', (byte)'7', (byte)'0', (byte)'2' },
            new byte[] { 0x50, 0x00, 0x00 },
            new byte[] { 0x06 },
            new byte[] { 0x56, 0x0D, 0x0A },
            new byte[10],
            new byte[] { 0x06 },
            new byte[] { 0x56, 0x0D, 0x0A },
            new byte[] { 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            new byte[] { 0x06 },
            CreateSResponse(0x00, "1702B"),
            new byte[] { 0x06 },
            CreateSResponse(0x40, "BF-1702B\0\0\0BF-1702B-V2.0"),
            new byte[] { 0x06 },
            CreateSResponse(0x80, "\0\0\0\0"),
            new byte[] { 0x06 },
            CreateSResponse(0xC0, "\0\0\0\0"),
            new byte[] { 0x06 },
            new byte[] { 0x56, 0x0A, 0x08 },
            new byte[] { 0x00, 0x10, 0x00, 0x00, 0xFF, 0x8F, 0x0C, 0x00 },
            new byte[] { 0x06 },
        };

        var stream = new ScriptedDuplexStream(responses);
        var endpoint = new TransportEndpoint(
            "usbprint://test-radio-1702b",
            "USB Printer Test Radio 1702B",
            TransportType.UsbPrinter,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProbeOpenPath"] = "\\\\?\\usb#vid_0483&pid_5780#synthetic#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}",
            });

        await using var connection = new UsbPrinterTransportConnection(endpoint, TransportTimeouts.Default, () => stream);
        await connection.ConnectAsync().ConfigureAwait(false);
        await using var session = new StockCpsSession(connection);

        var result = await session.ReadRadioInfoAsync().ConfigureAwait(false);

        Assert.AreEqual(RadioVariant.Bao1702B, result.Identity.Variant);
        Assert.AreEqual(ConfidenceLevel.Inferred, result.Identity.Confidence);
    }

    [TestMethod]
    public async Task ReadCodeplugAsync_UsesSegmentBasedAddressingAndReportsProgress()
    {
        var pageSize = StockCpsSessionBootstrap.ObservedCodeplugPageSize;
        var totalBlocks = StockCpsSessionBootstrap.ObservedReadTotalBlocks;
        byte upperSector = 0x0C;

        var allResponses = new List<byte[]>();

        // ── Transition command responses ──
        // 1. FF FF FF FF 0C — no response expected (enter-transfer-mode is fire-and-forget)
        //    but the identity echo that follows expects an ACK:
        allResponses.Add([0x06]);          // identity echo ACK
        allResponses.Add(new byte[8]);     // start-read 8-byte response (all 0x00 for test)
        allResponses.Add([0x06]);          // start-read ACK

        // ── Probe reads (1-byte each) ──
        foreach (var probeAddr in StockCpsSessionBootstrap.EnumerateProbeAddresses(upperSector))
        {
            var (pLo, pMid, pHi, _) = StockCpsSessionBootstrap.EncodeAddress(probeAddr);
            byte[] probeResp = [0x57, pLo, pMid, pHi, 0x01, 0x00];
            allResponses.Add(probeResp);
            allResponses.Add([0x06]);      // probe ACK
        }

        // ── 64-byte block reads ──
        var blockIndex = 0;
        foreach (var (address, _) in StockCpsSessionBootstrap.EnumerateBlocks(StockCpsSessionBootstrap.ObservedReadSegments))
        {
            // W-response: 0x57 + 3 address bytes + 0x40 length + 64 data bytes
            var wResponse = new byte[5 + pageSize];
            wResponse[0] = 0x57;
            wResponse[1] = (byte)(address & 0xFF);
            wResponse[2] = (byte)((address >> 8) & 0xFF);
            wResponse[3] = (byte)((address >> 16) & 0xFF);
            wResponse[4] = 0x40;
            Array.Fill(wResponse, (byte)(blockIndex % 251), 5, pageSize);
            allResponses.Add(wResponse);

            // ACK response after host ACK
            allResponses.Add([0x06]);
            blockIndex++;
        }

        var stream = new ScriptedDuplexStream(allResponses);
        var endpoint = new TransportEndpoint(
            "usbprint://read-test",
            "USB Printer Read Test",
            TransportType.UsbPrinter,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProbeOpenPath"] = "\\\\?\\usb#vid_0483&pid_5780#read-test#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}",
            });

        await using var connection = new UsbPrinterTransportConnection(endpoint, TransportTimeouts.Default, () => stream);
        await connection.ConnectAsync().ConfigureAwait(false);
        await using var session = new StockCpsSession(connection);
        // Prime the session with cached handshake values (normally set by ReadRadioInfoAsync)
        typeof(StockCpsSession).GetField("_bootstrapIdentity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(session, "DMR1702");
        typeof(StockCpsSession).GetField("_upperSectorByte", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(session, upperSector);

        var progressReports = new List<ProtocolProgress>();
        var progress = new Progress<ProtocolProgress>(p => progressReports.Add(p));

        var image = await session.ReadCodeplugAsync(progress).ConfigureAwait(false);

        Assert.AreEqual(StockCpsSessionBootstrap.NativeImageLength, image.Length);

        var writes = stream.Writes.ToArray();
        var probeCount = StockCpsSessionBootstrap.EnumerateProbeAddresses(upperSector).Count();
        // Transition: 3 writes (enter-transfer, identity-echo, start-read) + 2 ACKs (start-read ACK, post-start ACK)
        // = enter-transfer(1) + identity-echo(1) + start-read(1) + startRead-hostACK(1) = 4 transition writes
        // Probes: probeCount * 2 (probe-cmd + host-ACK)
        // Blocks: totalBlocks * 2 (R-cmd + host-ACK)
        var transitionWrites = 4; // enter-transfer + identity-echo + start-read + start-read-ACK
        var expectedWrites = transitionWrites + probeCount * 2 + totalBlocks * 2;
        Assert.AreEqual(expectedWrites, writes.Length, $"Expected {transitionWrites} transition + {probeCount * 2} probe + {totalBlocks * 2} block writes.");

        // Find the first 64-byte R-command (after transition + probes)
        var blockWriteStart = transitionWrites + probeCount * 2;
        var firstR = writes[blockWriteStart];
        Assert.AreEqual(5, firstR.Length, "R-command must be exactly 5 bytes.");
        Assert.AreEqual(0x52, firstR[0], "R-command must start with 0x52.");
        Assert.AreEqual(0x00, firstR[1], "First read block low selector byte = 0x00 (addr 0x055000).");
        Assert.AreEqual(0x50, firstR[2], "First read block mid selector byte = 0x50 (addr 0x055000).");
        Assert.AreEqual(0x05, firstR[3], "First read block high selector byte = 0x05 (addr 0x055000).");
        Assert.AreEqual(0x40, firstR[4], "Length byte must be 0x40.");

        // Host ACK after first block
        CollectionAssert.AreEqual(new byte[] { 0x06 }, writes[blockWriteStart + 1], "Host must send ACK after receiving W-response.");

        // Verify first segment (block 0) data is placed at its native file offset (0x2000)
        var firstSegmentFileOffset = StockCpsSessionBootstrap.ReadSegmentFileMapping[0].FileOffset;
        Assert.AreEqual(0x2000, firstSegmentFileOffset);
        Assert.IsTrue(image.AsSpan(firstSegmentFileOffset, pageSize).ToArray().All(b => b == 0),
            "Block 0 data pattern mismatch at native file offset 0x2000.");

        // Unread region at offset 0 should be 0xFF fill
        Assert.IsTrue(image.AsSpan(0, pageSize).ToArray().All(b => b == 0xFF),
            "Unread region at start of native image must be 0xFF.");
    }

    [TestMethod]
    public void SegmentMaps_MatchCapturedOemTotals()
    {
        // OEM capture: 3648 R-commands in read phase, 3584 W-commands in write phase
        Assert.AreEqual(3648, StockCpsSessionBootstrap.ObservedReadTotalBlocks, "Read segments must total 3648 blocks.");
        Assert.AreEqual(3584, StockCpsSessionBootstrap.ObservedWriteTotalBlocks, "Write segments must total 3584 blocks.");
        Assert.AreEqual(3648 * 64, StockCpsSessionBootstrap.ObservedReadImageSize, "Read image = 233,472 bytes.");
        Assert.AreEqual(3584 * 64, StockCpsSessionBootstrap.ObservedWriteImageSize, "Write image = 229,376 bytes.");

        // Write segments must be read segments minus the read-only 0x055000 segment (64 blocks)
        Assert.AreEqual(
            StockCpsSessionBootstrap.ObservedReadTotalBlocks - 64,
            StockCpsSessionBootstrap.ObservedWriteTotalBlocks,
            "Write total must be read total minus the 64-block read-only segment at 0x055000.");
    }

    [TestMethod]
    public void EncodeAddress_MatchesCapturedStockPattern()
    {
        // First read segment starts at 0x055000
        Assert.AreEqual(((byte)0x00, (byte)0x50, (byte)0x05, (byte)0x40), StockCpsSessionBootstrap.EncodeAddress(0x055000));
        Assert.AreEqual(((byte)0x40, (byte)0x50, (byte)0x05, (byte)0x40), StockCpsSessionBootstrap.EncodeAddress(0x055040));
        // First write segment at 0x046000
        Assert.AreEqual(((byte)0x00, (byte)0x60, (byte)0x04, (byte)0x40), StockCpsSessionBootstrap.EncodeAddress(0x046000));
        // Common segment at 0x007000
        Assert.AreEqual(((byte)0x00, (byte)0x70, (byte)0x00, (byte)0x40), StockCpsSessionBootstrap.EncodeAddress(0x007000));
        Assert.AreEqual(((byte)0x40, (byte)0x70, (byte)0x00, (byte)0x40), StockCpsSessionBootstrap.EncodeAddress(0x007040));
    }

    [TestMethod]
    public async Task WriteCodeplugAsync_SendsWCommandAndReceivesSingleAck()
    {
        var pageSize = StockCpsSessionBootstrap.ObservedCodeplugPageSize;
        var totalBlocks = StockCpsSessionBootstrap.ObservedWriteTotalBlocks;
        byte upperSector = 0x0C;

        var allResponses = new List<byte[]>();

        // ── Re-handshake responses (WriteCodeplugAsync re-runs the full handshake) ──
        allResponses.Add(new byte[] { 0x06, (byte)'D', (byte)'M', (byte)'R', (byte)'1', (byte)'7', (byte)'0', (byte)'2' }); // PSEARCH
        allResponses.Add(new byte[] { 0x50, 0x00, 0x00 }); // PASSSTA
        allResponses.Add([0x06]); // PASSSTA ACK
        allResponses.Add(new byte[] { 0x56, 0x0D, 0x0A }); // SYSINFO
        allResponses.Add(new byte[10]); // V0010 continuation
        allResponses.Add([0x06]); // V0010 ACK
        allResponses.Add(new byte[] { 0x56, 0x0D, 0x0A }); // V0020
        allResponses.Add(new byte[] { 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }); // V0020 continuation
        allResponses.Add([0x06]); // V0020 ACK
        allResponses.Add(CreateSResponse(0x00, "DM-1702")); // G0000
        allResponses.Add([0x06]); // G0000 ACK
        allResponses.Add(CreateSResponse(0x40, "DM1702-0103\0\0\0DM-1702-V1.4")); // G0040
        allResponses.Add([0x06]); // G0040 ACK
        allResponses.Add(CreateSResponse(0x80, "\0\0\0\0")); // G0080
        allResponses.Add([0x06]); // G0080 ACK
        allResponses.Add(CreateSResponse(0xC0, "\0\0\0\0")); // G00C0
        allResponses.Add([0x06]); // G00C0 ACK
        allResponses.Add(new byte[] { 0x56, 0x0A, 0x08 }); // V0000
        allResponses.Add(new byte[] { 0x00, 0x10, 0x00, 0x00, 0xFF, 0x8F, upperSector, 0x00 }); // V0000 continuation (byte[6] = upperSector)
        allResponses.Add([0x06]); // V0000 ACK

        // ── Transition command responses ──
        allResponses.Add([0x06]);          // identity echo ACK
        allResponses.Add(new byte[8]);     // start-read 8-byte response
        allResponses.Add([0x06]);          // start-read ACK

        // ── Pre-write probe reads (1-byte each, same as read phase) ──
        foreach (var probeAddr in StockCpsSessionBootstrap.EnumerateProbeAddresses(upperSector))
        {
            var (pLo, pMid, pHi, _) = StockCpsSessionBootstrap.EncodeAddress(probeAddr);
            byte[] probeResp = [0x57, pLo, pMid, pHi, 0x01, 0x00];
            allResponses.Add(probeResp);
            allResponses.Add([0x06]);      // probe ACK
        }

        // ── Write block ACKs ──
        for (var i = 0; i < totalBlocks; i++)
        {
            allResponses.Add([0x06]);
        }

        var fullStream = new ScriptedDuplexStream(allResponses);
        var endpoint = new TransportEndpoint(
            "usbprint://write-test",
            "USB Printer Write Test",
            TransportType.UsbPrinter,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProbeOpenPath"] = "\\\\?\\usb#vid_0483&pid_5780#write-test#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}",
            });

        // Build a native-format image (245,760 bytes) with test patterns at the
        // file offsets that correspond to the first write segment (wire 0x046000 → file 0x3000).
        var fullImage = new byte[StockCpsSessionBootstrap.NativeImageLength];
        Array.Fill(fullImage, (byte)0xFF);
        var firstWriteFileOffset = StockCpsSessionBootstrap.WriteSegmentFileMapping[0].FileOffset;
        Array.Fill(fullImage, (byte)0xAA, firstWriteFileOffset, pageSize);
        Array.Fill(fullImage, (byte)0xBB, firstWriteFileOffset + pageSize, pageSize);

        await using var connection = new UsbPrinterTransportConnection(endpoint, TransportTimeouts.Default, () => fullStream);
        await connection.ConnectAsync().ConfigureAwait(false);
        await using var session = new StockCpsSession(connection);

        await session.WriteCodeplugAsync(fullImage).ConfigureAwait(false);

        var writes = fullStream.Writes.ToArray();
        var probeCount = StockCpsSessionBootstrap.EnumerateProbeAddresses(upperSector).Count();
        // Handshake: PSEARCH + PASSSTA + SYSINFO + V0010 + V0020 + G0000-G00C0 + V0000 = variable
        // Transition: enter-transfer(1) + identity-echo(1) + start-read(1) + start-read-ACK(1) = 4
        // Probes: probeCount * 2 (probe-cmd + host-ACK)
        // Blocks: totalBlocks W-commands (no host ACK between writes)
        // Find the first W-command (0x57) write block by scanning from end
        var blockWrites = writes.Where(w => w.Length == 5 + pageSize && w[0] == 0x57).ToArray();
        Assert.AreEqual(totalBlocks, blockWrites.Length, $"Expected {totalBlocks} W-command block writes.");

        var firstWrite = blockWrites[0];
        Assert.AreEqual(5 + pageSize, firstWrite.Length, "W-command must be 5 header bytes plus 64 data bytes.");
        Assert.AreEqual(0x57, firstWrite[0], "W-command must start with 0x57.");
        Assert.AreEqual(0x00, firstWrite[1], "First write block low selector byte = 0x00 (addr 0x046000).");
        Assert.AreEqual(0x60, firstWrite[2], "First write block mid selector byte = 0x60 (addr 0x046000).");
        Assert.AreEqual(0x04, firstWrite[3], "First write block high selector byte = 0x04 (addr 0x046000).");
        Assert.AreEqual(0x40, firstWrite[4], "Length byte must be 0x40.");
        Assert.IsTrue(firstWrite.Skip(5).All(b => b == 0xAA), "Block 0 data must be all 0xAA.");

        var secondWrite = blockWrites[1];
        Assert.AreEqual(0x40, secondWrite[1], "Second write block low selector byte = 0x40 (addr 0x046040).");
        Assert.AreEqual(0x60, secondWrite[2], "Second write block mid selector byte = 0x60.");
        Assert.AreEqual(0x04, secondWrite[3], "Second write block high selector byte = 0x04.");
        Assert.AreEqual(0x40, secondWrite[4]);
        Assert.IsTrue(secondWrite.Skip(5).All(b => b == 0xBB), "Block 1 data must be all 0xBB.");

        // Verify re-handshake was sent (PSEARCH should be the first write)
        CollectionAssert.AreEqual("PSEARCH"u8.ToArray(), writes[0], "Write must re-handshake starting with PSEARCH.");
    }

    [TestMethod]
    public async Task WriteCodeplugAsync_ThrowsForWrongImageSize()
    {
        var stream = new ScriptedDuplexStream([]);
        var endpoint = new TransportEndpoint(
            "usbprint://write-size-test",
            "USB Printer Size Test",
            TransportType.UsbPrinter,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProbeOpenPath"] = "\\\\?\\usb#vid_0483&pid_5780#size-test#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}",
            });

        await using var connection = new UsbPrinterTransportConnection(endpoint, TransportTimeouts.Default, () => stream);
        await connection.ConnectAsync().ConfigureAwait(false);
        await using var session = new StockCpsSession(connection);

        await Assert.ThrowsExactlyAsync<ProtocolException>(
            () => session.WriteCodeplugAsync(new byte[4096]));
    }

    private static byte[] CreateSResponse(byte offset, string text)
    {
        var payload = new byte[69];
        payload[0] = 0x53;
        payload[3] = offset;
        payload[4] = 0x40;
        var bytes = System.Text.Encoding.ASCII.GetBytes(text.Replace("\\0", "\0"));
        Array.Copy(bytes, 0, payload, 5, Math.Min(64, bytes.Length));
        return payload;
    }

    private sealed class ScriptedDuplexStream : Stream
    {
        private readonly ConcurrentQueue<byte[]> _responses;
        private MemoryStream? _current;

        public ScriptedDuplexStream(IEnumerable<byte[]> responses)
        {
            _responses = new ConcurrentQueue<byte[]>(responses);
        }

        public ConcurrentQueue<byte[]> Writes { get; } = new();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_current is null || _current.Position >= _current.Length)
            {
                if (!_responses.TryDequeue(out var next))
                {
                    return ValueTask.FromResult(0);
                }

                _current = new MemoryStream(next, writable: false);
            }

            var read = _current.Read(buffer.Span);
            return ValueTask.FromResult(read);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            var copy = new byte[count];
            Array.Copy(buffer, offset, copy, 0, count);
            Writes.Enqueue(copy);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes.Enqueue(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
    }
}
