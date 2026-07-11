using Bao1702.Protocol;
using Bao1702.Protocol.Discovery;
using Bao1702.Protocol.Safety;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Mock;
using Bao1702.Transport.Serial;
using Bao1702.Transport.UsbPrinter;

namespace Bao1702.Cli.Infrastructure;

/// <summary>
/// Creates transport connections and protocol sessions for CLI operations,
/// probing serial and USB printer interfaces.
/// </summary>
internal sealed class CliSessionFactory
{
    private readonly MockRadioDevice _mockDevice;
    private readonly SerialTransportFactory _serialTransportFactory = new();
    private readonly UsbPrinterTransportFactory _usbPrinterTransportFactory = new();

    public CliSessionFactory(MockRadioDevice mockDevice)
    {
        _mockDevice = mockDevice ?? throw new ArgumentNullException(nameof(mockDevice));
    }

    public async Task<IReadOnlyList<(ITransportFactory Factory, TransportEndpoint Endpoint)>> EnumerateAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<(ITransportFactory Factory, TransportEndpoint Endpoint)>();
        results.AddRange((await _serialTransportFactory.EnumerateAsync(cancellationToken).ConfigureAwait(false)).Select(endpoint => ((ITransportFactory)_serialTransportFactory, endpoint)));
        results.AddRange((await _usbPrinterTransportFactory.EnumerateAsync(cancellationToken).ConfigureAwait(false)).Select(endpoint => ((ITransportFactory)_usbPrinterTransportFactory, endpoint)));

        var mockFactory = CreateMockFactory();
        results.AddRange((await mockFactory.EnumerateAsync(cancellationToken).ConfigureAwait(false)).Select(endpoint => ((ITransportFactory)mockFactory, endpoint)));
        return results;
    }

    public async Task<CliSessionContext> OpenPreferredAsync(string? endpointId = null, bool backupCompleted = true, CancellationToken cancellationToken = default)
    {
        var candidates = await EnumerateAllAsync(cancellationToken).ConfigureAwait(false);
        var selected = SelectEndpoint(candidates, endpointId);
        var connection = await selected.Factory.OpenAsync(selected.Endpoint, TransportTimeouts.Default, cancellationToken).ConfigureAwait(false);
        var traceCollector = new TransportTraceCollector();
        connection.TraceSink = traceCollector;
        await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);

        var session = new Bao1702ProtocolSession(
            connection,
            new SafetyPolicyEngine(),
            new ProtocolSessionOptions(SafetyPolicyOptions.Default with { BackupCompleted = backupCompleted }, ProtocolAssumptions.AssumedDefaultBlockSize));

        return new CliSessionContext(selected.Factory, selected.Endpoint, session, traceCollector);
    }

    public async Task<RadioProbeResult> ProbePreferredAsync(string? endpointId = null, CancellationToken cancellationToken = default)
    {
        var probeContext = await ProbePreferredWithTraceAsync(endpointId, cancellationToken).ConfigureAwait(false);
        return probeContext.Probe;
    }

    public async Task<CliProbeContext> ProbePreferredWithTraceAsync(string? endpointId = null, CancellationToken cancellationToken = default)
    {
        var candidates = await EnumerateAllAsync(cancellationToken).ConfigureAwait(false);
        var selected = SelectEndpoint(candidates, endpointId);
        var traceCollector = new TransportTraceCollector();
        var probe = await RadioInfoProbe.ProbeAsync(selected.Factory, selected.Endpoint, traceCollector, cancellationToken).ConfigureAwait(false);
        return new CliProbeContext(probe, traceCollector);
    }

    public async Task<CliTransportContext> OpenTransportAsync(string? endpointId = null, CancellationToken cancellationToken = default)
    {
        var candidates = await EnumerateAllAsync(cancellationToken).ConfigureAwait(false);
        var selected = SelectEndpoint(candidates, endpointId);
        var connection = await selected.Factory.OpenAsync(selected.Endpoint, TransportTimeouts.Default, cancellationToken).ConfigureAwait(false);
        var traceCollector = new TransportTraceCollector();
        connection.TraceSink = traceCollector;
        await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return new CliTransportContext(selected.Factory, selected.Endpoint, connection, traceCollector);
    }

    private MockTransportFactory CreateMockFactory() => new(_mockDevice.Handle);

    private static (ITransportFactory Factory, TransportEndpoint Endpoint) SelectEndpoint(
        IReadOnlyList<(ITransportFactory Factory, TransportEndpoint Endpoint)> candidates,
        string? endpointId)
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No transport endpoints were found.");
        }

        if (!string.IsNullOrWhiteSpace(endpointId))
        {
            var explicitMatch = candidates.FirstOrDefault(candidate => string.Equals(candidate.Endpoint.Id, endpointId, StringComparison.OrdinalIgnoreCase));
            if (explicitMatch.Endpoint is not null)
            {
                return explicitMatch;
            }

            throw new InvalidOperationException($"Transport endpoint '{endpointId}' was not found.");
        }

        return candidates
            .OrderBy(static candidate => candidate.Endpoint.TransportType switch
            {
                TransportType.UsbPrinter => 0,
                TransportType.Serial => 1,
                TransportType.Mock => 2,
                _ => 3,
            })
            .ThenBy(static candidate => candidate.Endpoint.DisplayName, StringComparer.OrdinalIgnoreCase)
            .First();
    }
}

internal sealed class CliSessionContext : IAsyncDisposable
{
    public CliSessionContext(
        ITransportFactory transportFactory,
        TransportEndpoint endpoint,
        Bao1702ProtocolSession session,
        TransportTraceCollector traceCollector)
    {
        TransportFactory = transportFactory;
        Endpoint = endpoint;
        Session = session;
        TraceCollector = traceCollector;
    }

    public ITransportFactory TransportFactory { get; }

    public TransportEndpoint Endpoint { get; }

    public Bao1702ProtocolSession Session { get; }

    public TransportTraceCollector TraceCollector { get; }

    public ValueTask DisposeAsync() => Session.DisposeAsync();
}

internal sealed record CliProbeContext(
    RadioProbeResult Probe,
    TransportTraceCollector TraceCollector);

internal sealed class CliTransportContext : IAsyncDisposable
{
    public CliTransportContext(
        ITransportFactory transportFactory,
        TransportEndpoint endpoint,
        ITransportConnection connection,
        TransportTraceCollector traceCollector)
    {
        TransportFactory = transportFactory;
        Endpoint = endpoint;
        Connection = connection;
        TraceCollector = traceCollector;
    }

    public ITransportFactory TransportFactory { get; }

    public TransportEndpoint Endpoint { get; }

    public ITransportConnection Connection { get; }

    public TransportTraceCollector TraceCollector { get; }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync().ConfigureAwait(false);
    }
}
