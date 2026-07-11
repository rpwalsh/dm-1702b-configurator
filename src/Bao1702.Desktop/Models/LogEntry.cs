namespace Bao1702.Desktop.Models;

/// <summary>
/// Immutable entry for the application operation log.
/// </summary>
/// <param name="Timestamp">UTC time the event occurred.</param>
/// <param name="Level">Severity level (INFO, WARN, ERROR).</param>
/// <param name="Message">Human-readable event description.</param>
public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message);
