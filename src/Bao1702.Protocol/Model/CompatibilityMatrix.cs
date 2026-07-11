namespace Bao1702.Protocol.Model;

/// <summary>
/// Defines the safety permissions for a specific radio family and variant.
/// </summary>
public sealed record CompatibilityRule(
    RadioFamily Family,
    RadioVariant Variant,
    string DisplayName,
    bool AllowsManagedCodeplugWrite,
    bool AllowsManagedFirmwareRead,
    bool AllowsManagedFirmwareWrite,
    string Notes,
    ConfidenceLevel MinimumConfidence);

/// <summary>
/// Registry of known DM-1702 radio variants and their managed operation permissions.
/// </summary>
public static class CompatibilityMatrix
{
    public static IReadOnlyList<CompatibilityRule> DefaultRules { get; } =
    [
        new CompatibilityRule(
            RadioFamily.Bao1702,
            RadioVariant.Bao1702B,
            "Baofeng 1702B / orange-keypad DM-1702-family target",
            AllowsManagedCodeplugWrite: true,
            AllowsManagedFirmwareRead: true,
            AllowsManagedFirmwareWrite: false,
            "Primary target. Managed codeplug writes are permitted only after backup and target identification gates pass. Firmware writes remain blocked.",
            ConfidenceLevel.Inferred),
        new CompatibilityRule(
            RadioFamily.Bao1702,
            RadioVariant.Dm1702,
            "DM-1702 family peer variant",
            AllowsManagedCodeplugWrite: true,
            AllowsManagedFirmwareRead: true,
            AllowsManagedFirmwareWrite: false,
            "DM-1702 variant verified via hardware read/write testing. Managed codeplug writes permitted after backup and identification gates pass.",
            ConfidenceLevel.Inferred),
    ];

    public static CompatibilityResult Evaluate(RadioIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var reasons = new List<string>();
        var rule = DefaultRules.FirstOrDefault(candidate => candidate.Family == identity.Family && candidate.Variant == identity.Variant);

        if (identity.Family == RadioFamily.Unknown)
        {
            reasons.Add("Target family is unknown.");
        }

        if (identity.Variant == RadioVariant.Unknown)
        {
            reasons.Add("Target variant is unknown.");
        }

        if (rule is null)
        {
            reasons.Add("Target is not present in the managed compatibility matrix.");
            return new CompatibilityResult(
                IsKnownFamily: identity.Family == RadioFamily.Bao1702,
                IsWriteSafeByDefault: false,
                Summary: "Target is readable but not write-safe by default.",
                Reasons: reasons);
        }

        if ((int)identity.Confidence < (int)rule.MinimumConfidence)
        {
            reasons.Add($"Target confidence '{identity.Confidence}' is lower than the minimum required '{rule.MinimumConfidence}'.");
        }

        if (!identity.Capabilities.SupportsCodeplugWrite && rule.AllowsManagedCodeplugWrite)
        {
            reasons.Add("Target capabilities do not advertise codeplug write support.");
        }

        if (identity.Confidence is ConfidenceLevel.Unknown or ConfidenceLevel.RequiresHardwareVerification)
        {
            reasons.Add("Target identification confidence is insufficient for managed write operations.");
        }

        reasons.Add(rule.Notes);

        var isWriteSafe =
            rule.AllowsManagedCodeplugWrite
            && identity.Capabilities.SupportsCodeplugWrite
            && identity.Confidence is not (ConfidenceLevel.Unknown or ConfidenceLevel.RequiresHardwareVerification)
            && (int)identity.Confidence >= (int)rule.MinimumConfidence;

        return new CompatibilityResult(
            IsKnownFamily: true,
            IsWriteSafeByDefault: isWriteSafe,
            Summary: isWriteSafe
                ? $"{rule.DisplayName} is compatible for managed codeplug workflows."
                : $"{rule.DisplayName} is not write-safe by default.",
            Reasons: reasons);
    }
}
