using System.Text;
using Bao1702.Protocol.Model;

namespace Bao1702.Protocol.Stock;

/// <summary>Parsed radio identity payload from the stock CPS bootstrap handshake.</summary>
public sealed record StockCpsRadioInfoPayload(
    string BootstrapIdentity,
    string? ModelDisplayName,
    string? HardwareCode,
    string? FirmwareDisplay,
    IReadOnlyList<string> ExtractedStrings,
    IReadOnlyList<byte[]> GResponses,
    IReadOnlyList<byte[]> VResponses);

public static class StockCpsRadioInfoParser
{
    public static StockCpsRadioInfoPayload Parse(
        ReadOnlySpan<byte> psearchResponse,
        IReadOnlyList<byte[]> gResponses,
        IReadOnlyList<byte[]> vResponses)
    {
        if (psearchResponse.Length < 2 || psearchResponse[0] != 0x06)
        {
            throw new ProtocolException("PSEARCH response did not begin with ACK-prefixed ASCII identity data.");
        }

        var bootstrapIdentity = Encoding.ASCII.GetString(psearchResponse[1..]);
        var extractedStrings = new List<string> { bootstrapIdentity };
        var assembledInfoRegion = new byte[gResponses.Count * 64];

        for (var index = 0; index < gResponses.Count; index++)
        {
            var response = gResponses[index];
            if (response.Length != 69 || response[0] != 0x53)
            {
                throw new ProtocolException($"Expected a 69-byte S-response for info window {index}, but received {response.Length} byte(s).");
            }

            response.AsSpan(5, 64).CopyTo(assembledInfoRegion.AsSpan(index * 64, 64));
        }

        extractedStrings.AddRange(ExtractAsciiStrings(assembledInfoRegion));
        var distinctStrings = extractedStrings
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var modelDisplayName = distinctStrings.FirstOrDefault(static value => value.Contains("DM-1702", StringComparison.OrdinalIgnoreCase));
        var hardwareCode = distinctStrings.FirstOrDefault(static value => value.Contains("DM1702-", StringComparison.OrdinalIgnoreCase));
        var firmwareDisplay = distinctStrings.FirstOrDefault(static value => value.Contains("V1.", StringComparison.OrdinalIgnoreCase) || value.Contains("V2.", StringComparison.OrdinalIgnoreCase));

        return new StockCpsRadioInfoPayload(
            bootstrapIdentity,
            modelDisplayName,
            hardwareCode,
            firmwareDisplay,
            distinctStrings,
            gResponses.ToArray(),
            vResponses.ToArray());
    }

    public static RadioIdentity ToRadioIdentity(StockCpsRadioInfoPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var modelName = payload.ModelDisplayName ?? payload.BootstrapIdentity;
        var variant = InferVariant(payload, modelName);
        var confidence = InferConfidence(payload, variant);

        var firmwareVersion = new FirmwareVersion(payload.FirmwareDisplay ?? payload.HardwareCode ?? "Unknown");
        var bootloaderVersion = new BootloaderVersion("Unknown (stock CPS read path not yet mapped)");
        return new RadioIdentity(
            RadioFamily.Bao1702,
            variant,
            modelName,
            firmwareVersion,
            bootloaderVersion,
            payload.HardwareCode,
            new RadioCapabilities(
                SupportsCodeplugRead: true,
                SupportsCodeplugWrite: true,
                SupportsFirmwareRead: false,
                SupportsFirmwareWrite: false,
                SupportsRtcReadWrite: false,
                StockCpsSessionBootstrap.NativeImageLength,
                ProtocolAssumptions.AssumedDefaultFirmwareSize,
                confidence),
            confidence);
    }

    private static RadioVariant InferVariant(StockCpsRadioInfoPayload payload, string modelName)
    {
        if (payload.ExtractedStrings.Any(static value => value.Contains("1702B", StringComparison.OrdinalIgnoreCase))
            || modelName.Contains("1702B", StringComparison.OrdinalIgnoreCase))
        {
            return RadioVariant.Bao1702B;
        }

        if (modelName.Contains("DM-1702", StringComparison.OrdinalIgnoreCase)
            || payload.BootstrapIdentity.Contains("DMR1702", StringComparison.OrdinalIgnoreCase))
        {
            return RadioVariant.Dm1702;
        }

        return RadioVariant.Unknown;
    }

    private static ConfidenceLevel InferConfidence(StockCpsRadioInfoPayload payload, RadioVariant variant)
    {
        if (variant == RadioVariant.Bao1702B)
        {
            return ConfidenceLevel.Inferred;
        }

        if (variant == RadioVariant.Dm1702)
        {
            return ConfidenceLevel.Inferred;
        }

        return ConfidenceLevel.Unknown;
    }

    private static IReadOnlyList<string> ExtractAsciiStrings(ReadOnlySpan<byte> bytes)
    {
        var results = new List<string>();
        var builder = new StringBuilder();
        foreach (var value in bytes)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                builder.Append((char)value);
                continue;
            }

            Flush(builder, results);
        }

        Flush(builder, results);
        return results;
    }

    private static void Flush(StringBuilder builder, List<string> results)
    {
        if (builder.Length >= 4)
        {
            results.Add(builder.ToString());
        }

        builder.Clear();
    }
}
