namespace Bao1702.Protocol.Packets;

/// <summary>Classification of observed protocol payloads by structure and direction.</summary>
public enum ObservedPayloadKind
{
    Unknown,
    Acknowledge,
    AsciiCommand,
    AsciiResponse,
    BinaryCommand,
    BinaryResponse,
}

public sealed record ObservedPayloadDefinition(
    string Name,
    ObservedPayloadKind Kind,
    string Summary,
    ProtocolKnowledgeLevel KnowledgeLevel);

public sealed record ObservedPayloadMatch(
    ObservedPayloadDefinition Definition,
    string Detail,
    string? AsciiText = null);

/// <summary>
/// Catalog of payloads observed in live stock CPS USB captures.
/// This is separate from the earlier provisional Bao1702Packet framing model because these payloads are
/// derived from stock CPS traffic and should drive replacement of guessed protocol behavior.
/// </summary>
public static class ObservedCommandCatalog
{
    private static readonly IReadOnlyDictionary<string, ObservedPayloadDefinition> AsciiDefinitions =
        new Dictionary<string, ObservedPayloadDefinition>(StringComparer.Ordinal)
        {
            ["PSEARCH"] = new("Psearch", ObservedPayloadKind.AsciiCommand, "Stock CPS session discovery command.", ProtocolKnowledgeLevel.Confirmed),
            ["PASSSTA"] = new("PassSta", ObservedPayloadKind.AsciiCommand, "Stock CPS password or session-state negotiation command.", ProtocolKnowledgeLevel.Confirmed),
            ["SYSINFO"] = new("SysInfo", ObservedPayloadKind.AsciiCommand, "Stock CPS system information request command.", ProtocolKnowledgeLevel.Confirmed),
            ["DMR1702"] = new("RadioIdentityAscii", ObservedPayloadKind.AsciiResponse, "ASCII model identity reported by the radio.", ProtocolKnowledgeLevel.Confirmed),
        };

    private static readonly IReadOnlyDictionary<byte, ObservedPayloadDefinition> BinaryDefinitions =
        new Dictionary<byte, ObservedPayloadDefinition>
        {
            [0x06] = new("Acknowledge", ObservedPayloadKind.Acknowledge, "Single-byte ACK or ACK-prefixed payload marker.", ProtocolKnowledgeLevel.Confirmed),
            [0x47] = new("GCommand", ObservedPayloadKind.BinaryCommand, "Observed 5-byte host command used before 69-byte S-prefixed responses.", ProtocolKnowledgeLevel.Confirmed),
            [0x52] = new("RCommand", ObservedPayloadKind.BinaryCommand, "Observed 5-byte host request used with W-prefixed acknowledgements.", ProtocolKnowledgeLevel.Confirmed),
            [0x53] = new("SResponse", ObservedPayloadKind.BinaryResponse, "Observed radio response carrying structured 69-byte payload blocks.", ProtocolKnowledgeLevel.Confirmed),
            [0x56] = new("VCommandOrResponse", ObservedPayloadKind.BinaryCommand, "Observed V-prefixed query/response family used during session startup.", ProtocolKnowledgeLevel.Confirmed),
            [0x57] = new("WResponse", ObservedPayloadKind.BinaryResponse, "Observed radio acknowledgement for R-prefixed host requests.", ProtocolKnowledgeLevel.Confirmed),
        };

    public static ObservedPayloadMatch Match(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return new ObservedPayloadMatch(
                new ObservedPayloadDefinition("EmptyPayload", ObservedPayloadKind.Unknown, "USB transfer carried zero application bytes.", ProtocolKnowledgeLevel.Confirmed),
                "No payload bytes were present.");
        }

        if (TryDecodeAscii(payload, out var asciiText))
        {
            if (AsciiDefinitions.TryGetValue(asciiText, out var asciiDefinition))
            {
                return new ObservedPayloadMatch(asciiDefinition, asciiDefinition.Summary, asciiText);
            }

            return new ObservedPayloadMatch(
                new ObservedPayloadDefinition("AsciiPayload", ObservedPayloadKind.AsciiResponse, "Printable ASCII payload observed in capture.", ProtocolKnowledgeLevel.Inferred),
                $"ASCII payload '{asciiText}'.",
                asciiText);
        }

        if (payload[0] == 0x06)
        {
            if (payload.Length == 1)
            {
                var definition = BinaryDefinitions[0x06];
                return new ObservedPayloadMatch(definition, definition.Summary);
            }

            if (TryDecodeAscii(payload[1..], out var ackAsciiText))
            {
                return new ObservedPayloadMatch(
                    new ObservedPayloadDefinition("AckWithAscii", ObservedPayloadKind.AsciiResponse, "ACK-prefixed ASCII response observed in stock CPS capture.", ProtocolKnowledgeLevel.Confirmed),
                    $"ACK followed by ASCII '{ackAsciiText}'.",
                    ackAsciiText);
            }
        }

        if (BinaryDefinitions.TryGetValue(payload[0], out var binaryDefinition))
        {
            return new ObservedPayloadMatch(binaryDefinition, DescribeBinaryPayload(binaryDefinition, payload));
        }

        return new ObservedPayloadMatch(
            new ObservedPayloadDefinition($"Unknown_0x{payload[0]:X2}", ObservedPayloadKind.Unknown, "Uncataloged binary payload observed in capture.", ProtocolKnowledgeLevel.Unknown),
            $"Unknown binary payload starting with 0x{payload[0]:X2} and length {payload.Length} byte(s).");
    }

    private static bool TryDecodeAscii(ReadOnlySpan<byte> payload, out string asciiText)
    {
        asciiText = string.Empty;
        if (payload.IsEmpty)
        {
            return false;
        }

        foreach (var value in payload)
        {
            if (value is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        asciiText = System.Text.Encoding.ASCII.GetString(payload);
        return true;
    }

    private static string DescribeBinaryPayload(ObservedPayloadDefinition definition, ReadOnlySpan<byte> payload)
    {
        return payload[0] switch
        {
            0x47 when payload.Length >= 5 => $"Observed G-command arguments: {payload[1]:X2} {payload[2]:X2} {payload[3]:X2} {payload[4]:X2}.",
            0x52 when payload.Length >= 5 => $"Observed R-command window/address bytes: {payload[1]:X2} {payload[2]:X2} {payload[3]:X2} {payload[4]:X2}.",
            0x53 when payload.Length >= 5 => $"Observed S-response with selector bytes {payload[1]:X2} {payload[2]:X2} {payload[3]:X2} {payload[4]:X2} and total length {payload.Length}.",
            0x56 when payload.Length >= 3 => $"Observed V-family payload of length {payload.Length} byte(s).",
            0x57 when payload.Length >= 5 => $"Observed W-response bytes: {payload[1]:X2} {payload[2]:X2} {payload[3]:X2} {payload[4]:X2}{(payload.Length > 5 ? $" {payload[5]:X2}" : string.Empty)}.",
            _ => definition.Summary,
        };
    }
}
