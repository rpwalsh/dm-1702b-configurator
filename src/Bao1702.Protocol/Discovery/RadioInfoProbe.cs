using Bao1702.Protocol.Model;
using Bao1702.Protocol.Stock;
using Bao1702.Transport.Abstractions;

namespace Bao1702.Protocol.Discovery;

/// <summary>
/// Result of probing a transport endpoint for a connected DM-1702 radio.
/// </summary>
public sealed record RadioProbeResult(
    TransportEndpoint Endpoint,
    RadioInfoResult? RadioInfo,
    bool IsReachable,
    string Summary,
    IReadOnlyList<string> Notes,
    Exception? Error = null);

/// <summary>
/// Probes transport endpoints to discover and identify connected DM-1702 radios.
/// </summary>
public static class RadioInfoProbe
{
    public static async Task<RadioProbeResult> ProbeAsync(
        ITransportFactory transportFactory,
        TransportEndpoint endpoint,
        ITransportTraceSink? traceSink = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(endpoint);

        try
        {
            await using var connection = await transportFactory.OpenAsync(endpoint, TransportTimeouts.Default, cancellationToken).ConfigureAwait(false);
            connection.TraceSink = traceSink;
            await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await connection.ResetAsync(cancellationToken).ConfigureAwait(false);

            RadioInfoResult info;
            if (endpoint.TransportType == TransportType.UsbPrinter)
            {
                await using var stockSession = new StockCpsSession(connection);
                info = await stockSession.ReadRadioInfoAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await using var session = new Bao1702ProtocolSession(connection);
                info = await session.ReadRadioInfoAsync(cancellationToken).ConfigureAwait(false);
            }

            var notes = new List<string>
            {
                $"Compatibility: {info.Compatibility.Summary}",
                $"Identity confidence: {info.Identity.Confidence}",
                $"Endpoint transport: {endpoint.TransportType}",
            };
            notes.AddRange(BuildTransportEvidenceNotes(endpoint));
            notes.AddRange(info.Compatibility.Reasons);

            return new RadioProbeResult(
                endpoint,
                info,
                IsReachable: true,
                Summary: $"Detected {info.Identity.ModelName} via {endpoint.DisplayName}.",
                Notes: notes);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or ProtocolException)
        {
            return new RadioProbeResult(
                endpoint,
                RadioInfo: null,
                IsReachable: false,
                Summary: $"Probe failed on {endpoint.DisplayName}: {ex.Message}",
                Notes: ["No write-capable decision should be made from a failed probe."],
                Error: ex);
        }
    }

    private static IReadOnlyList<string> BuildTransportEvidenceNotes(TransportEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var notes = new List<string>();
        switch (endpoint.TransportType)
        {
            case TransportType.UsbPrinter:
                notes.Add("USB printer-class transport matches the observed stock CPS connectivity path for DM-1702-family radios.");
                if (endpoint.Properties.TryGetValue("HardwareId", out var hardwareId) && !string.IsNullOrWhiteSpace(hardwareId))
                {
                    notes.Add($"USB hardware ID: {hardwareId}");
                }
                break;

            case TransportType.Serial:
                notes.Add("Serial transport remains a provisional access path and does not by itself prove stock CPS compatibility for this radio family.");
                break;

            case TransportType.Mock:
                notes.Add("Mock transport is synthetic and not evidence of on-device compatibility.");
                break;
        }

        return notes;
    }
}
