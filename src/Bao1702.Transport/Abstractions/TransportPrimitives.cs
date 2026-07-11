using System.Buffers;

namespace Bao1702.Transport.Abstractions;

/// <summary>Identifies the physical transport layer used for radio communication.</summary>
public enum TransportType
{
    Serial,
    UsbPrinter,
    Hid,
    Dfu,
    Mock,
    Unknown,
}

[Flags]
public enum TransportCapabilities
{
    None = 0,
    Enumerate = 1 << 0,
    Connect = 1 << 1,
    Read = 1 << 2,
    Write = 1 << 3,
    RequestResponse = 1 << 4,
    PacketCapture = 1 << 5,
}

public sealed record TransportEndpoint(
    string Id,
    string DisplayName,
    TransportType TransportType,
    IReadOnlyDictionary<string, string> Properties);

public sealed record TransportTimeouts(
    TimeSpan ConnectTimeout,
    TimeSpan ReadTimeout,
    TimeSpan WriteTimeout,
    TimeSpan ExchangeTimeout)
{
    public static TransportTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5));
}

public sealed record TransportRetryPolicy(int RetryCount, TimeSpan RetryDelay)
{
    public static TransportRetryPolicy None { get; } = new(0, TimeSpan.Zero);
    public static TransportRetryPolicy Conservative { get; } = new(2, TimeSpan.FromMilliseconds(100));
}

/// <summary>Factory that enumerates available radio endpoints and creates transport connections.</summary>
public interface ITransportFactory
{
    string Name { get; }

    TransportType TransportType { get; }

    TransportCapabilities Capabilities { get; }

    Task<IReadOnlyList<TransportEndpoint>> EnumerateAsync(CancellationToken cancellationToken = default);

    Task<ITransportConnection> OpenAsync(
        TransportEndpoint endpoint,
        TransportTimeouts? timeouts = null,
        CancellationToken cancellationToken = default);
}

public interface IRadioTransport
{
    string Name { get; }

    TransportType TransportType { get; }

    ITransportFactory Factory { get; }
}

public interface ITransportConnection : IAsyncDisposable
{
    TransportEndpoint Endpoint { get; }

    bool IsOpen { get; }

    ITransportTraceSink? TraceSink { get; set; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    Task<byte[]> ExchangeAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default);
}
