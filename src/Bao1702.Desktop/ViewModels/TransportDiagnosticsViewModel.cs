using System.Collections.ObjectModel;
using Bao1702.Transport.Abstractions;

namespace Bao1702.Desktop.ViewModels;

/// <summary>
/// Displays transport-layer trace events captured during radio communication.
/// Events are collected via an <see cref="ITransportTraceSink"/> that the session wires in.
/// </summary>
public sealed class TransportDiagnosticsViewModel : ObservableObject
{
    private string _summary = "No transport trace events captured yet. Connect to a radio and perform an operation.";
    private bool _isCapturing;

    public ObservableCollection<TransportTraceEntry> Events { get; } = [];

    public string Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set => SetProperty(ref _isCapturing, value);
    }

    public int TotalEvents => Events.Count;

    public void Clear()
    {
        Events.Clear();
        OnPropertyChanged(nameof(TotalEvents));
        Summary = "Trace cleared.";
    }

    public void AppendEvent(TransportTraceEvent traceEvent)
    {
        ArgumentNullException.ThrowIfNull(traceEvent);
        var payload = traceEvent.Payload is { Length: > 0 }
            ? string.Join(' ', traceEvent.Payload.Select(static b => b.ToString("X2")))
            : null;

        Events.Add(new TransportTraceEntry(
            traceEvent.Timestamp,
            traceEvent.Level.ToString(),
            traceEvent.Direction.ToString(),
            traceEvent.Message,
            payload));

        OnPropertyChanged(nameof(TotalEvents));
    }

    public void AppendBatch(IReadOnlyList<TransportTraceEvent> events)
    {
        foreach (var evt in events)
        {
            var payload = evt.Payload is { Length: > 0 }
                ? string.Join(' ', evt.Payload.Select(static b => b.ToString("X2")))
                : null;

            Events.Add(new TransportTraceEntry(
                evt.Timestamp,
                evt.Level.ToString(),
                evt.Direction.ToString(),
                evt.Message,
                payload));
        }

        OnPropertyChanged(nameof(TotalEvents));
        Summary = $"{Events.Count} trace event(s) captured.";
    }
}

/// <summary>Flat display record for DataGrid binding.</summary>
public sealed record TransportTraceEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Direction,
    string Message,
    string? Payload);
