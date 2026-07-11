using Bao1702.Protocol.Model;

namespace Bao1702.Firmware.Analysis;

/// <summary>
/// Validates whether a firmware image is compatible with a specific radio identity,
/// checking family, capability flags, and image structure.
/// </summary>
public static class FirmwareCompatibilityValidator
{
    public static FirmwareCompatibilityResult Validate(RadioIdentity identity, ReadOnlySpan<byte> imageBytes)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var reasons = new List<string>();
        var analysis = FirmwareImageParser.Analyze(imageBytes);

        if (identity.Family != RadioFamily.Bao1702)
        {
            reasons.Add("Target family is not Bao1702-family.");
        }

        if (!identity.Capabilities.SupportsFirmwareRead)
        {
            reasons.Add("Target capabilities do not advertise firmware read support.");
        }

        if (identity.Capabilities.SupportsFirmwareWrite)
        {
            reasons.Add("Target advertises firmware write capability, but managed firmware write support is intentionally disabled by policy.");
        }
        else
        {
            reasons.Add("Managed firmware writes remain disabled even for known targets.");
        }

        if (analysis.Image.Header.DeclaredLength != 0 && analysis.Image.Header.DeclaredLength != imageBytes.Length)
        {
            reasons.Add($"Firmware declared length {analysis.Image.Header.DeclaredLength} does not match actual image length {imageBytes.Length}.");
        }

        if (analysis.Image.Header.Signature.Any(ch => char.IsControl(ch)))
        {
            reasons.Add("Firmware signature contains non-printable characters.");
        }

        reasons.AddRange(analysis.Warnings);

        return new FirmwareCompatibilityResult(
            IsCompatible: identity.Family == RadioFamily.Bao1702 && analysis.Warnings.Count == 0,
            Summary: "Firmware images are currently supported for backup and offline analysis only; flashing remains blocked.",
            Reasons: reasons.Distinct(StringComparer.Ordinal).ToArray());
    }
}
