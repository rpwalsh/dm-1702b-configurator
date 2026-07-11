namespace Bao1702.Protocol.Packets;

/// <summary>Confidence level for a protocol command's reverse-engineered semantics.</summary>
public enum ProtocolKnowledgeLevel
{
    Confirmed,
    Inferred,
    Unknown,
    RequiresHardwareVerification,
}

public sealed record Bao1702CommandDefinition(
    byte CommandId,
    string Name,
    string Summary,
    bool SupportsReadOnlyUse,
    bool IsPotentiallyDestructive,
    ProtocolKnowledgeLevel KnowledgeLevel);

public static class Bao1702CommandCatalog
{
    private static readonly IReadOnlyDictionary<byte, Bao1702CommandDefinition> Definitions =
        new Dictionary<byte, Bao1702CommandDefinition>
        {
            [Bao1702CommandIds.ReadRadioInfo] = new(Bao1702CommandIds.ReadRadioInfo, "ReadRadioInfo", "Probe model, firmware, bootloader, and serial information.", true, false, ProtocolKnowledgeLevel.Inferred),
            [Bao1702CommandIds.EnterProgrammingMode] = new(Bao1702CommandIds.EnterProgrammingMode, "EnterProgrammingMode", "Enter the radio programming/session state.", true, false, ProtocolKnowledgeLevel.Inferred),
            [Bao1702CommandIds.ExitProgrammingMode] = new(Bao1702CommandIds.ExitProgrammingMode, "ExitProgrammingMode", "Exit the radio programming/session state.", true, false, ProtocolKnowledgeLevel.Inferred),
            [Bao1702CommandIds.ReadCodeplugBlock] = new(Bao1702CommandIds.ReadCodeplugBlock, "ReadCodeplugBlock", "Read a block from the codeplug image.", true, false, ProtocolKnowledgeLevel.Inferred),
            [Bao1702CommandIds.WriteCodeplugBlock] = new(Bao1702CommandIds.WriteCodeplugBlock, "WriteCodeplugBlock", "Write a block to the codeplug image.", false, true, ProtocolKnowledgeLevel.RequiresHardwareVerification),
            [Bao1702CommandIds.ReadFirmwareBlock] = new(Bao1702CommandIds.ReadFirmwareBlock, "ReadFirmwareBlock", "Read a block from device firmware.", true, false, ProtocolKnowledgeLevel.RequiresHardwareVerification),
            [Bao1702CommandIds.WriteFirmwareBlock] = new(Bao1702CommandIds.WriteFirmwareBlock, "WriteFirmwareBlock", "Write a block to device firmware.", false, true, ProtocolKnowledgeLevel.RequiresHardwareVerification),
            [Bao1702CommandIds.ReadRtc] = new(Bao1702CommandIds.ReadRtc, "ReadRtc", "Read the radio RTC value.", true, false, ProtocolKnowledgeLevel.Inferred),
            [Bao1702CommandIds.WriteRtc] = new(Bao1702CommandIds.WriteRtc, "WriteRtc", "Write the radio RTC value.", false, true, ProtocolKnowledgeLevel.RequiresHardwareVerification),
        };

    public static Bao1702CommandDefinition Get(byte commandId)
    {
        return Definitions.TryGetValue(commandId, out var definition)
            ? definition
            : new Bao1702CommandDefinition(commandId, $"Unknown_0x{commandId:X2}", "Unknown protocol command.", false, true, ProtocolKnowledgeLevel.Unknown);
    }
}
