using Bao1702.Transport.Abstractions;

namespace Bao1702.Transport.Serial;

/// <summary>
/// Transport factory that enumerates and connects to radios over COM-port serial interfaces.
/// </summary>
public sealed class SerialTransportFactory : ITransportFactory
{
    public string Name => "Serial transport";

    public TransportType TransportType => TransportType.Serial;

    public TransportCapabilities Capabilities =>
        TransportCapabilities.Enumerate |
        TransportCapabilities.Connect |
        TransportCapabilities.Read |
        TransportCapabilities.Write |
        TransportCapabilities.RequestResponse |
        TransportCapabilities.PacketCapture;

    public Task<IReadOnlyList<TransportEndpoint>> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SerialPortEnumerator.EnumeratePorts());
    }

    public Task<ITransportConnection> OpenAsync(
        TransportEndpoint endpoint,
        TransportTimeouts? timeouts = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(endpoint);
        ITransportConnection connection = new SerialTransportConnection(endpoint, timeouts ?? TransportTimeouts.Default);
        return Task.FromResult(connection);
    }
}
