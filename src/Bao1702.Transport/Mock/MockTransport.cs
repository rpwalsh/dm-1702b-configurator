using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Diagnostics;
using Bao1702.Transport.Framing;

namespace Bao1702.Transport.Mock;

/// <summary>In-memory radio transport for test scenarios.</summary>
public sealed class MockRadioTransport(ITransportFactory factory) : IRadioTransport
{
    public string Name => "Mock radio transport";

    public TransportType TransportType => TransportType.Mock;

    public ITransportFactory Factory { get; } = factory;
}

/// <summary>Factory that creates mock transport connections with a configurable request/response handler.</summary>
public sealed class MockTransportFactory : ITransportFactory
{
    private readonly IReadOnlyList<TransportEndpoint> _endpoints;
    private readonly Func<byte[], byte[]> _handler;

    public MockTransportFactory(Func<byte[], byte[]> handler)
        : this(
            [new TransportEndpoint(
                "mock://radio/1702b-primary",
                "Mock Baofeng 1702B",
                TransportType.Mock,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Model"] = "1702B",
                    ["Mode"] = "Synthetic",
                })],
            handler)
    {
    }

    public MockTransportFactory(IReadOnlyList<TransportEndpoint> endpoints, Func<byte[], byte[]> handler)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public string Name => "Mock transport factory";

    public TransportType TransportType => TransportType.Mock;

    public TransportCapabilities Capabilities =>
        TransportCapabilities.Enumerate |
        TransportCapabilities.Connect |
        TransportCapabilities.Read |
        TransportCapabilities.Write |
        TransportCapabilities.RequestResponse |
        TransportCapabilities.PacketCapture;

    public Task<IReadOnlyList<TransportEndpoint>> EnumerateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_endpoints);

    public Task<ITransportConnection> OpenAsync(
        TransportEndpoint endpoint,
        TransportTimeouts? timeouts = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ITransportConnection connection = new MockTransportConnection(endpoint, _handler, timeouts ?? TransportTimeouts.Default);
        return Task.FromResult(connection);
    }
}

public sealed class MockTransportConnection : ITransportConnection
{
    private readonly Func<byte[], byte[]> _handler;
    private readonly TransportTimeouts _timeouts;
    private byte[] _lastResponse = [];

    public MockTransportConnection(TransportEndpoint endpoint, Func<byte[], byte[]> handler, TransportTimeouts timeouts)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _timeouts = timeouts ?? throw new ArgumentNullException(nameof(timeouts));
    }

    public TransportEndpoint Endpoint { get; }

    public bool IsOpen { get; private set; }

    public ITransportTraceSink? TraceSink { get; set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = true;
        TraceSink.TraceMessage(TransportTraceLevel.Information, TransportTraceDirection.Internal, $"Connected to {Endpoint.DisplayName}.");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = false;
        TraceSink?.TraceMessage(TransportTraceLevel.Information, TransportTraceDirection.Internal, $"Disconnected from {Endpoint.DisplayName}.");
        return Task.CompletedTask;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        _lastResponse = [];
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await Task.Delay(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        var bytesToCopy = Math.Min(buffer.Length, _lastResponse.Length);
        _lastResponse.AsMemory(0, bytesToCopy).CopyTo(buffer);
        _lastResponse = [];
        return bytesToCopy;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await Task.Delay(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        TraceSink?.TraceMessage(TransportTraceLevel.Debug, TransportTraceDirection.ToDevice, $"Write {buffer.Length} bytes.\n{HexDump.Format(buffer.Span)}", buffer.ToArray());
        _lastResponse = _handler(buffer.ToArray());
        TraceSink?.TraceMessage(TransportTraceLevel.Debug, TransportTraceDirection.FromDevice, $"Response {_lastResponse.Length} bytes.\n{HexDump.Format(_lastResponse)}", _lastResponse);
    }

    public async Task<byte[]> ExchangeAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeouts.ExchangeTimeout);
        await WriteAsync(request, timeoutCts.Token).ConfigureAwait(false);
        return _lastResponse.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsOpen)
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Transport connection is not open.");
        }
    }
}
