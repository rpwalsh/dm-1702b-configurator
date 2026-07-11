using Bao1702.Protocol.Model;

namespace Bao1702.Protocol.Safety;

/// <summary>
/// Configurable safety policy options that control write permissions and backup requirements.
/// </summary>
public sealed record SafetyPolicyOptions(
    bool ForceUnsafe,
    bool RequireBackupBeforeWrite,
    bool BackupCompleted,
    bool AllowUnknownReadOnly)
{
    public static SafetyPolicyOptions Default { get; } = new(
        ForceUnsafe: false,
        RequireBackupBeforeWrite: true,
        BackupCompleted: false,
        AllowUnknownReadOnly: true);
}

public sealed class SafetyPolicyEngine
{
    public CompatibilityResult EvaluateCompatibility(RadioIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return CompatibilityMatrix.Evaluate(identity);
    }

    public SafetyDecision Evaluate(
        RadioIdentity identity,
        RadioOperation operation,
        SafetyPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);

        var compatibility = EvaluateCompatibility(identity);
        var reasons = new List<string>(compatibility.Reasons);

        if (operation is RadioOperation.ReadRadioInfo or RadioOperation.ReadCodeplug or RadioOperation.ReadFirmware or RadioOperation.BackupFirmware or RadioOperation.ReadRtc)
        {
            if (compatibility.IsKnownFamily || options.AllowUnknownReadOnly)
            {
                return new SafetyDecision(
                    reasons.Count == 0 ? SafetyDecisionKind.Allow : SafetyDecisionKind.AllowWithWarnings,
                    "Read-only operation permitted.",
                    reasons);
            }

            reasons.Add("Unknown target reads are disabled by policy.");
            return new SafetyDecision(SafetyDecisionKind.Block, "Read-only operation blocked by policy.", reasons);
        }

        if (!compatibility.IsWriteSafeByDefault)
        {
            reasons.Add("Write operation is blocked because the target is not write-safe by default.");
            if (!options.ForceUnsafe)
            {
                return new SafetyDecision(SafetyDecisionKind.Block, "Write operation blocked.", reasons);
            }

            reasons.Add("Unsafe override is enabled.");
        }

        if (options.RequireBackupBeforeWrite && !options.BackupCompleted)
        {
            reasons.Add("Backup-before-write requirement has not been satisfied.");
            return new SafetyDecision(SafetyDecisionKind.Block, "Write operation blocked until a backup exists.", reasons);
        }

        return new SafetyDecision(
            options.ForceUnsafe ? SafetyDecisionKind.AllowWithWarnings : SafetyDecisionKind.Allow,
            options.ForceUnsafe ? "Write permitted with unsafe override." : "Write permitted.",
            reasons);
    }
}
