using Bao1702.Protocol.Packets;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>A single observed USB payload extracted from a captured CPS session.</summary>
public sealed record StockCpsObservedPayload(
    int FrameIndex,
    CaptureDirection Direction,
    byte EndpointAddress,
    byte TransferType,
    byte[] Payload,
    ObservedPayloadMatch Match);

public sealed record StockCpsExchange(
    StockCpsObservedPayload Request,
    StockCpsObservedPayload? Response,
    string Summary);

public sealed record StockCpsProtocolAnalysis(
    int TotalUsbPayloads,
    int HostPayloads,
    int DevicePayloads,
    IReadOnlyList<StockCpsObservedPayload> Payloads,
    IReadOnlyList<StockCpsExchange> Exchanges,
    IReadOnlyDictionary<string, int> PayloadNameCounts);

public static class StockCpsProtocolAnalyzer
{
    public static StockCpsProtocolAnalysis AnalyzePcap(byte[] pcapBytes)
    {
        ArgumentNullException.ThrowIfNull(pcapBytes);

        var records = PcapFileParser.ParseRecords(pcapBytes)
            .Select((record, index) => new CaptureRecord(index, CaptureDirection.Unknown, record, []))
            .ToArray();

        var captureAnalysis = CaptureSessionAnalyzer.AnalyzeRecords(records);
        return Analyze(captureAnalysis);
    }

    public static StockCpsProtocolAnalysis Analyze(CaptureSessionAnalysis captureAnalysis)
    {
        ArgumentNullException.ThrowIfNull(captureAnalysis);

        var payloads = captureAnalysis.Frames
            .Where(static frame => frame.UsbPcapRecord is not null && frame.UsbPcapRecord.HasPayload)
            .Select(frame => new StockCpsObservedPayload(
                frame.Index,
                frame.Direction,
                frame.UsbPcapRecord!.EndpointAddress,
                frame.UsbPcapRecord.TransferType,
                frame.UsbPcapRecord.Payload,
                ObservedCommandCatalog.Match(frame.UsbPcapRecord.Payload)))
            .ToArray();

        var exchanges = CorrelateExchanges(payloads);
        var nameCounts = payloads
            .GroupBy(static payload => payload.Match.Definition.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        return new StockCpsProtocolAnalysis(
            payloads.Length,
            payloads.Count(static payload => payload.Direction == CaptureDirection.HostToDevice),
            payloads.Count(static payload => payload.Direction == CaptureDirection.DeviceToHost),
            payloads,
            exchanges,
            nameCounts);
    }

    private static IReadOnlyList<StockCpsExchange> CorrelateExchanges(IReadOnlyList<StockCpsObservedPayload> payloads)
    {
        var exchanges = new List<StockCpsExchange>();
        for (var index = 0; index < payloads.Count; index++)
        {
            var request = payloads[index];
            if (request.Direction != CaptureDirection.HostToDevice)
            {
                continue;
            }

            StockCpsObservedPayload? response = null;
            for (var responseIndex = index + 1; responseIndex < payloads.Count; responseIndex++)
            {
                var candidate = payloads[responseIndex];
                if (candidate.Direction == CaptureDirection.DeviceToHost)
                {
                    response = candidate;
                    break;
                }

                if (candidate.Direction == CaptureDirection.HostToDevice)
                {
                    break;
                }
            }

            exchanges.Add(new StockCpsExchange(request, response, SummarizeExchange(request, response)));
        }

        return exchanges;
    }

    private static string SummarizeExchange(StockCpsObservedPayload request, StockCpsObservedPayload? response)
    {
        if (response is null)
        {
            return $"{request.Match.Definition.Name} -> no subsequent device payload captured before the next host payload.";
        }

        return $"{request.Match.Definition.Name} -> {response.Match.Definition.Name}";
    }
}
