namespace Bao1702.Protocol.Model;

/// <summary>
/// Indicates the verification confidence level of a protocol inference or data field.
/// </summary>
public enum ConfidenceLevel
{
    Confirmed,
    Inferred,
    Unknown,
    Preserved,
    RequiresHardwareVerification,
}

/// <summary>
/// Radio hardware family classification.
/// </summary>
public enum RadioFamily
{
    Unknown,
    Bao1702,
}

public enum RadioVariant
{
    Unknown,
    Bao1702B,
    Dm1702,
}

public sealed record FirmwareVersion(string RawValue)
{
    public override string ToString() => RawValue;
}

public sealed record BootloaderVersion(string RawValue)
{
    public override string ToString() => RawValue;
}

public sealed record RadioCapabilities(
    bool SupportsCodeplugRead,
    bool SupportsCodeplugWrite,
    bool SupportsFirmwareRead,
    bool SupportsFirmwareWrite,
    bool SupportsRtcReadWrite,
    int AssumedCodeplugSize,
    int AssumedFirmwareSize,
    ConfidenceLevel Confidence);

public sealed record RadioIdentity(
    RadioFamily Family,
    RadioVariant Variant,
    string ModelName,
    FirmwareVersion FirmwareVersion,
    BootloaderVersion BootloaderVersion,
    string? SerialNumber,
    RadioCapabilities Capabilities,
    ConfidenceLevel Confidence);

public sealed record CompatibilityResult(
    bool IsKnownFamily,
    bool IsWriteSafeByDefault,
    string Summary,
    IReadOnlyList<string> Reasons);

public enum SafetyDecisionKind
{
    Allow,
    AllowWithWarnings,
    Block,
}

public sealed record SafetyDecision(
    SafetyDecisionKind Kind,
    string Summary,
    IReadOnlyList<string> Reasons)
{
    public bool IsAllowed => Kind is SafetyDecisionKind.Allow or SafetyDecisionKind.AllowWithWarnings;
}

public enum RadioOperation
{
    ReadRadioInfo,
    ReadCodeplug,
    WriteCodeplug,
    ReadFirmware,
    WriteFirmware,
    BackupFirmware,
    ReadRtc,
    WriteRtc,
}

public sealed record RadioInfoResult(RadioIdentity Identity, CompatibilityResult Compatibility);
