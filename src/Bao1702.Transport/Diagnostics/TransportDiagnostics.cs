namespace Bao1702.Transport.Abstractions;

/// <summary>Direction of a transport-level trace event relative to the host.</summary>
public enum TransportTraceDirection
{
    Internal,
    ToDevice,
    FromDevice,
}

public enum TransportTraceLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

public sealed record TransportTraceEvent(
    DateTimeOffset Timestamp,
    TransportTraceLevel Level,
    TransportTraceDirection Direction,
    string Message,
    byte[]? Payload = null);

public interface ITransportTraceSink
{
    void Trace(TransportTraceEvent traceEvent);
}

/// <summary>Collects transport trace events into an in-memory list for diagnostic inspection.</summary>
public sealed class TransportTraceCollector : ITransportTraceSink
{
    private readonly List<TransportTraceEvent> _events = [];

    public IReadOnlyList<TransportTraceEvent> Events => _events;

    public void Trace(TransportTraceEvent traceEvent)
    {
        ArgumentNullException.ThrowIfNull(traceEvent);
        _events.Add(traceEvent);
    }
}

public static class TransportTraceExtensions
{
    public static void TraceMessage(
        this ITransportTraceSink? sink,
        TransportTraceLevel level,
        TransportTraceDirection direction,
        string message,
        byte[]? payload = null)
    {
        sink?.Trace(new TransportTraceEvent(DateTimeOffset.UtcNow, level, direction, message, payload));
    }
}
