using System.Text;
using System.Text.Json;
using Bao1702.Protocol;
using Bao1702.Protocol.Stock;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>A decoded payload segment from a stock CPS read/write session.</summary>
public sealed record StockCpsPayloadSegmentDump(
    int FrameIndex,
    CaptureDirection Direction,
    byte EndpointAddress,
    byte TransferType,
    int Length,
    string Hex,
    string? Ascii,
    string Kind);

public sealed record StockCpsTransactionDump(
    int RequestFrameIndex,
    string RequestKind,
    string RequestHex,
    string? RequestAscii,
    IReadOnlyList<StockCpsPayloadSegmentDump> ResponseSegments,
    string? CombinedResponseHex,
    string? CombinedResponseAscii,
    string Summary);

public sealed record StockCpsDataBlockDump(
    string Family,
    int RequestFrameIndex,
    IReadOnlyList<int> ResponseFrameIndices,
    string RequestHex,
    string ResponseHex,
    string SelectorHex,
    int DataLength,
    string DataHex,
    string? AsciiPreview,
    bool ReassembledFromMultipleSegments);

public sealed record StockCpsRadioDataDump(
    string CapturePath,
    string? BootstrapIdentity,
    string? ModelName,
    string? HardwareCode,
    string? FirmwareDisplay,
    IReadOnlyList<string> ExtractedStrings,
    ReassembledImageDump? ReassembledImage,
    HeuristicDecodedCodeplugDump? HeuristicDecodedCodeplug,
    IReadOnlyDictionary<string, int> PayloadCounts,
    IReadOnlyList<StockCpsTransactionDump> Transactions,
    IReadOnlyList<StockCpsDataBlockDump> InfoBlocks,
    IReadOnlyList<StockCpsDataBlockDump> DataBlocks);

public sealed record ReassembledImageDump(
    int BaseAddress,
    int Length,
    int BlockCount,
    string Sha256,
    IReadOnlyList<ReassembledImageGap> Gaps,
    IReadOnlyList<string> ExtractedStrings,
    string ImageHex);

public sealed record HeuristicDecodedCodeplugDump(
    IReadOnlyList<DecodedStringEntry> Strings,
    IReadOnlyList<FrequencyPairCandidate> FrequencyPairs,
    IReadOnlyList<ChannelCandidate> ChannelCandidates);

public static class StockCpsRadioDataExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static StockCpsRadioDataDump BuildDump(string capturePath, CaptureSessionAnalysis captureAnalysis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capturePath);
        ArgumentNullException.ThrowIfNull(captureAnalysis);

        var protocolAnalysis = StockCpsProtocolAnalyzer.Analyze(captureAnalysis);
        var transactions = BuildTransactions(protocolAnalysis.Payloads);
        var infoBlocks = ExtractBlocks(transactions, requestOpcode: 0x47, responseOpcode: 0x53, family: "G/S");
        var dataBlocks = ExtractBlocks(transactions, requestOpcode: 0x52, responseOpcode: 0x57, family: "R/W");
        var reassembledImage = dataBlocks.Count == 0 ? null : StockCpsCodeplugReassembler.Reassemble(dataBlocks);

        StockCpsRadioInfoPayload? radioInfo = null;
        try
        {
            var psearchResponse = transactions
                .FirstOrDefault(static transaction => string.Equals(transaction.Request.Match.Definition.Name, "Psearch", StringComparison.Ordinal))
                ?.ResponseBytes.FirstOrDefault();
            var gResponses = transactions
                .Where(static transaction => transaction.RequestPayload.Length > 0 && transaction.RequestPayload[0] == 0x47)
                .Select(static transaction => transaction.CombineResponsePayload())
                .Where(static payload => payload.Length == 69 && payload[0] == 0x53)
                .Take(4)
                .ToArray();
            var vResponses = transactions
                .Where(static transaction => transaction.RequestPayload.Length > 0 && transaction.RequestPayload[0] == 0x56)
                .SelectMany(static transaction => transaction.ResponseBytes)
                .ToArray();

            if (psearchResponse is not null && gResponses.Length == 4)
            {
                radioInfo = StockCpsRadioInfoParser.Parse(psearchResponse, gResponses, vResponses);
            }
        }
        catch (ProtocolException)
        {
        }

        return new StockCpsRadioDataDump(
            capturePath,
            radioInfo?.BootstrapIdentity,
            radioInfo?.ModelDisplayName,
            radioInfo?.HardwareCode,
            radioInfo?.FirmwareDisplay,
            radioInfo?.ExtractedStrings ?? [],
            reassembledImage is null
                ? null
                : new ReassembledImageDump(
                    reassembledImage.BaseAddress,
                    reassembledImage.Image.Length,
                    reassembledImage.BlockCount,
                    reassembledImage.Sha256,
                    reassembledImage.Gaps,
                    reassembledImage.ExtractedStrings.Take(256).ToArray(),
                    Convert.ToHexString(reassembledImage.Image)),
            reassembledImage is null
                ? null
                : BuildHeuristicDump(reassembledImage.Image),
            protocolAnalysis.PayloadNameCounts,
            transactions.Select(ToTransactionDump).ToArray(),
            infoBlocks,
            dataBlocks);
    }

    public static string SerializeToJson(StockCpsRadioDataDump dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        return JsonSerializer.Serialize(dump, JsonOptions);
    }

    private static IReadOnlyList<StockCpsTransaction> BuildTransactions(IReadOnlyList<StockCpsObservedPayload> payloads)
    {
        var transactions = new List<StockCpsTransaction>();
        for (var index = 0; index < payloads.Count; index++)
        {
            var request = payloads[index];
            if (request.Direction != CaptureDirection.HostToDevice)
            {
                continue;
            }

            var responses = new List<StockCpsObservedPayload>();
            for (var responseIndex = index + 1; responseIndex < payloads.Count; responseIndex++)
            {
                var candidate = payloads[responseIndex];
                if (candidate.Direction == CaptureDirection.HostToDevice)
                {
                    break;
                }

                responses.Add(candidate);
            }

            transactions.Add(new StockCpsTransaction(request, responses));
        }

        return transactions;
    }

    private static IReadOnlyList<StockCpsDataBlockDump> ExtractBlocks(
        IReadOnlyList<StockCpsTransaction> transactions,
        byte requestOpcode,
        byte responseOpcode,
        string family)
    {
        var blocks = new List<StockCpsDataBlockDump>();
        foreach (var transaction in transactions)
        {
            if (transaction.RequestPayload.Length == 0 || transaction.RequestPayload[0] != requestOpcode)
            {
                continue;
            }

            var combined = transaction.CombineResponsePayload();
            if (combined.Length < 5 || combined[0] != responseOpcode)
            {
                continue;
            }

            var selector = combined.AsSpan(1, 4).ToArray();
            var data = combined.Length > 5 ? combined.AsSpan(5).ToArray() : [];
            blocks.Add(new StockCpsDataBlockDump(
                family,
                transaction.Request.FrameIndex,
                transaction.Responses.Select(static response => response.FrameIndex).ToArray(),
                Convert.ToHexString(transaction.RequestPayload),
                Convert.ToHexString(combined),
                Convert.ToHexString(selector),
                data.Length,
                Convert.ToHexString(data),
                GetAsciiPreview(data),
                transaction.Responses.Count > 1));
        }

        return blocks;
    }

    private static StockCpsTransactionDump ToTransactionDump(StockCpsTransaction transaction)
    {
        var combined = transaction.CombineResponsePayload();
        return new StockCpsTransactionDump(
            transaction.Request.FrameIndex,
            transaction.Request.Match.Definition.Name,
            Convert.ToHexString(transaction.RequestPayload),
            TryAscii(transaction.RequestPayload),
            transaction.Responses.Select(static response => new StockCpsPayloadSegmentDump(
                response.FrameIndex,
                response.Direction,
                response.EndpointAddress,
                response.TransferType,
                response.Payload.Length,
                Convert.ToHexString(response.Payload),
                TryAscii(response.Payload),
                response.Match.Definition.Name)).ToArray(),
            combined.Length == 0 ? null : Convert.ToHexString(combined),
            TryAscii(combined),
            transaction.Summary);
    }

    private static string? TryAscii(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return null;
        }

        foreach (var value in bytes)
        {
            if (value is < 0x20 or > 0x7E)
            {
                return null;
            }
        }

        return Encoding.ASCII.GetString(bytes);
    }

    private static string? GetAsciiPreview(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder();
        foreach (var value in bytes)
        {
            builder.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
        }

        var preview = builder.ToString().Trim('.');
        return string.IsNullOrWhiteSpace(preview) ? null : preview;
    }

    private static HeuristicDecodedCodeplugDump BuildHeuristicDump(byte[] image)
    {
        var decoded = HeuristicCodeplugDecoder.Decode(image);
        return new HeuristicDecodedCodeplugDump(
            decoded.Strings.Take(1024).ToArray(),
            decoded.FrequencyPairs.Take(2048).ToArray(),
            decoded.ChannelCandidates.Take(1024).ToArray());
    }

    private sealed record StockCpsTransaction(
        StockCpsObservedPayload Request,
        IReadOnlyList<StockCpsObservedPayload> Responses)
    {
        public byte[] RequestPayload => Request.Payload;

        public IReadOnlyList<byte[]> ResponseBytes => Responses.Select(static response => response.Payload).ToArray();

        public string Summary => Responses.Count == 0
            ? $"{Request.Match.Definition.Name} -> no device payload"
            : $"{Request.Match.Definition.Name} -> {string.Join(" + ", Responses.Select(static response => response.Match.Definition.Name))}";

        public byte[] CombineResponsePayload()
        {
            if (Responses.Count == 0)
            {
                return [];
            }

            if (Responses.Count == 1)
            {
                return Responses[0].Payload;
            }

            var totalLength = Responses.Sum(static response => response.Payload.Length);
            var combined = new byte[totalLength];
            var offset = 0;
            foreach (var response in Responses)
            {
                response.Payload.CopyTo(combined.AsSpan(offset));
                offset += response.Payload.Length;
            }

            return combined;
        }
    }
}
