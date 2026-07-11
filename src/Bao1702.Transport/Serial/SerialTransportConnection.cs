using System.IO.Ports;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Diagnostics;
using Bao1702.Transport.Framing;

namespace Bao1702.Transport.Serial;

/// <summary>
/// Serial transport connection for early read-only radio protocol work.
/// The transport is generic and intentionally does not assert that the far endpoint is a Bao1702-family radio.
/// </summary>
public sealed class SerialTransportConnection : ITransportConnection
{
    private readonly TransportTimeouts _timeouts;
    private readonly Func<Stream>? _streamFactory;
    private readonly int _baudRate;
    private Stream? _stream;
    private SerialPort? _serialPort;

    public SerialTransportConnection(TransportEndpoint endpoint, TransportTimeouts timeouts)
        : this(endpoint, timeouts, streamFactory: null, baudRate: ResolveBaudRate(endpoint))
    {
    }

    public SerialTransportConnection(TransportEndpoint endpoint, TransportTimeouts timeouts, Func<Stream> streamFactory)
        : this(endpoint, timeouts, streamFactory, baudRate: ResolveBaudRate(endpoint))
    {
    }

    private SerialTransportConnection(TransportEndpoint endpoint, TransportTimeouts timeouts, Func<Stream>? streamFactory, int baudRate)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _timeouts = timeouts ?? throw new ArgumentNullException(nameof(timeouts));
        _streamFactory = streamFactory;
        _baudRate = baudRate;
    }

    public TransportEndpoint Endpoint { get; }

    public bool IsOpen { get; private set; }

    public ITransportTraceSink? TraceSink { get; set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsOpen)
        {
            return Task.CompletedTask;
        }

        var portName = ResolvePortName(Endpoint);

        if (_streamFactory is not null)
        {
            _stream = _streamFactory();
        }
        else
        {
            _serialPort = new SerialPort(portName, _baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = ToMilliseconds(_timeouts.ReadTimeout),
                WriteTimeout = ToMilliseconds(_timeouts.WriteTimeout),
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false,
            };
            _serialPort.Open();
            _stream = _serialPort.BaseStream;
        }

        IsOpen = true;
        TraceSink.TraceMessage(
            TransportTraceLevel.Information,
            TransportTraceDirection.Internal,
            $"Opened serial transport on {portName} at {_baudRate} baud.");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOpen)
        {
            return Task.CompletedTask;
        }

        TraceSink.TraceMessage(
            TransportTraceLevel.Information,
            TransportTraceDirection.Internal,
            $"Closing serial transport on {ResolvePortName(Endpoint)}.");

        _stream?.Dispose();
        _stream = null;

        _serialPort?.Dispose();
        _serialPort = null;
        IsOpen = false;
        return Task.CompletedTask;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TraceSink?.TraceMessage(
            TransportTraceLevel.Information,
            TransportTraceDirection.Internal,
            $"Resetting serial transport on {ResolvePortName(Endpoint)}.");

        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeouts.ReadTimeout);
        var read = await _stream!.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
        if (read > 0)
        {
            TraceSink?.TraceMessage(
                TransportTraceLevel.Debug,
                TransportTraceDirection.FromDevice,
                $"Serial read {read} byte(s).\n{HexDump.Format(buffer.Span[..read])}",
                buffer[..read].ToArray());
        }

        return read;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeouts.WriteTimeout);
        await _stream!.WriteAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
        await _stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
        TraceSink?.TraceMessage(
            TransportTraceLevel.Debug,
            TransportTraceDirection.ToDevice,
            $"Serial write {buffer.Length} byte(s).\n{HexDump.Format(buffer.Span)}",
            buffer.ToArray());
    }

    public async Task<byte[]> ExchangeAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeouts.ExchangeTimeout);

        await WriteAsync(request, timeoutCts.Token).ConfigureAwait(false);

        using var responseBuffer = new MemoryStream();
        var chunk = new byte[256];
        while (true)
        {
            var bytesRead = await ReadAsync(chunk, timeoutCts.Token).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new TimeoutException($"Timed out waiting for a response frame from {ResolvePortName(Endpoint)}.");
            }

            responseBuffer.Write(chunk, 0, bytesRead);
            var candidate = responseBuffer.ToArray();
            if (TransportFrameCodec.TryDecode(candidate, out _, out _))
            {
                return candidate;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private static int ResolveBaudRate(TransportEndpoint endpoint)
    {
        if (endpoint.Properties.TryGetValue("BaudRate", out var configured)
            && int.TryParse(configured, out var baudRate)
            && baudRate > 0)
        {
            return baudRate;
        }

        return SerialPortEnumerator.DefaultBaudRate;
    }

    private static string ResolvePortName(TransportEndpoint endpoint)
    {
        if (endpoint.Properties.TryGetValue("PortName", out var portName) && !string.IsNullOrWhiteSpace(portName))
        {
            return portName;
        }

        throw new InvalidOperationException("Serial transport endpoint does not define a PortName property.");
    }

    private static int ToMilliseconds(TimeSpan timeout)
    {
        var value = (int)Math.Ceiling(timeout.TotalMilliseconds);
        return value <= 0 ? 1 : value;
    }

    private void EnsureOpen()
    {
        if (!IsOpen || _stream is null)
        {
            throw new InvalidOperationException("Serial transport connection is not open.");
        }
    }
}
