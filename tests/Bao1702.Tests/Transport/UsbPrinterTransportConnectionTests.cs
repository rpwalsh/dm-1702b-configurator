using System.Collections.Concurrent;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Framing;
using Bao1702.Transport.UsbPrinter;

namespace Bao1702.Tests.Transport;

[TestClass]
public sealed class UsbPrinterTransportConnectionTests
{
    [TestMethod]
    public async Task ExchangeAsync_ReadsUntilACompleteFrameIsReceived()
    {
        var responsePayload = new byte[] { 0xAA, 0x55, 0x10, 0x20 };
        var responseFrame = TransportFrameCodec.Encode(responsePayload);
        var scriptedStream = new ScriptedDuplexStream(responseFrame, chunkSize: 3);
        var endpoint = new TransportEndpoint(
            "usbprint://test-radio",
            "USB Printer Test Radio",
            TransportType.UsbPrinter,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProbeOpenPath"] = "\\\\?\\usb#vid_0483&pid_5780#synthetic#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}",
            });

        var connection = new UsbPrinterTransportConnection(endpoint, TransportTimeouts.Default, () => scriptedStream);

        await connection.ConnectAsync().ConfigureAwait(false);
        var actualFrame = await connection.ExchangeAsync(new byte[] { 0x01, 0x02, 0x03 }, CancellationToken.None).ConfigureAwait(false);

        CollectionAssert.AreEqual(responseFrame, actualFrame);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03 }, scriptedStream.WrittenBytes.ToArray());
    }

    [TestMethod]
    public async Task ConnectAsync_RequiresProbeOpenPathProperty()
    {
        var endpoint = new TransportEndpoint("usbprint://missing", "Broken", TransportType.UsbPrinter, new Dictionary<string, string>());
        var connection = new UsbPrinterTransportConnection(endpoint, TransportTimeouts.Default, () => new MemoryStream());

        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            Assert.Fail("Expected ConnectAsync to reject endpoints that do not define ProbeOpenPath.");
        }
        catch (InvalidOperationException)
        {
        }
    }

    [TestMethod]
    public async Task ResetAsync_ReopensScriptedStream()
    {
        var scriptedStream1 = new ScriptedDuplexStream([], chunkSize: 8);
        var scriptedStream2 = new ScriptedDuplexStream([], chunkSize: 8);
        var streams = new Queue<Stream>([scriptedStream1, scriptedStream2]);
        var endpoint = new TransportEndpoint(
            "usbprint://test-reset",
            "USB Printer Reset Test",
            TransportType.UsbPrinter,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProbeOpenPath"] = "\\?\\usb#vid_0483&pid_5780#synthetic#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}",
            });

        var connection = new UsbPrinterTransportConnection(endpoint, TransportTimeouts.Default, () => streams.Dequeue());
        await connection.ConnectAsync().ConfigureAwait(false);
        await connection.ResetAsync().ConfigureAwait(false);

        Assert.IsTrue(connection.IsOpen);
        Assert.AreEqual(0, streams.Count);
    }

    private sealed class ScriptedDuplexStream : Stream
    {
        private readonly ConcurrentQueue<byte> _readBytes;
        private readonly int _chunkSize;

        public ScriptedDuplexStream(byte[] bytesToRead, int chunkSize)
        {
            _readBytes = new ConcurrentQueue<byte>(bytesToRead);
            _chunkSize = chunkSize;
        }

        public MemoryStream WrittenBytes { get; } = new();

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

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var bytesRead = 0;
            while (bytesRead < buffer.Length && bytesRead < _chunkSize && _readBytes.TryDequeue(out var nextByte))
            {
                buffer.Span[bytesRead++] = nextByte;
            }

            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WrittenBytes.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WrittenBytes.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
    }
}
