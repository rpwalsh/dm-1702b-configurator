using Bao1702.Protocol.Model;

namespace Bao1702.Protocol.Safety;

/// <summary>
/// Request to validate whether a write operation should be permitted.
/// </summary>
public sealed record WriteIntentValidationRequest(
    RadioIdentity Identity,
    RadioOperation Operation,
    string BackupRootDirectory,
    SafetyPolicyOptions SafetyPolicy,
    bool RequireCompatibleTarget = true);

public sealed record WriteIntentValidationResult(
    bool IsAllowed,
    string Summary,
    IReadOnlyList<string> Reasons,
    BackupRecord? LatestBackup,
    CompatibilityResult Compatibility,
    SafetyDecision SafetyDecision);

public sealed record WritePreflightReport(
    RadioIdentity Identity,
    RadioOperation Operation,
    WriteIntentValidationResult IntentValidation,
    bool IsImageValidationAvailable,
    bool IsImageValid,
    int? ExpectedImageSize,
    int? ActualImageSize,
    IReadOnlyList<string> ImageValidationMessages)
{
    public bool IsAllowed => IntentValidation.IsAllowed && (!IsImageValidationAvailable || IsImageValid);

    public string Summary => IsAllowed
        ? "Write preflight passed."
        : "Write preflight failed.";

    public IReadOnlyList<string> Reasons { get; } = BuildReasons(IntentValidation, IsImageValidationAvailable, ImageValidationMessages);

    private static IReadOnlyList<string> BuildReasons(
        WriteIntentValidationResult intentValidation,
        bool isImageValidationAvailable,
        IReadOnlyList<string> imageValidationMessages)
    {
        var reasons = new List<string>(intentValidation.Reasons);
        if (isImageValidationAvailable)
        {
            reasons.AddRange(imageValidationMessages);
        }

        return reasons;
    }
}

public static class WritePreflightFormatter
{
    public static string FormatText(WritePreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var lines = new List<string>
        {
            report.Summary,
            $"Target: {report.Identity.ModelName} / {report.Identity.Variant}",
            $"Operation: {report.Operation}",
            $"Compatibility: {report.IntentValidation.Compatibility.Summary}",
            $"WriteSafeByDefault: {report.IntentValidation.Compatibility.IsWriteSafeByDefault}",
            $"Safety decision: {report.IntentValidation.SafetyDecision.Summary}",
            $"Latest backup: {report.IntentValidation.LatestBackup?.BackupId ?? "none"}",
        };

        if (report.IntentValidation.LatestBackup is not null)
        {
            lines.Add($"Latest backup path: {report.IntentValidation.LatestBackup.ImagePath}");
        }

        if (report.IsImageValidationAvailable)
        {
            lines.Add($"Image validation: {(report.IsImageValid ? "passed" : "failed")}");
            if (report.ExpectedImageSize.HasValue || report.ActualImageSize.HasValue)
            {
                lines.Add($"Image size: actual={report.ActualImageSize?.ToString() ?? "n/a"}, expected={report.ExpectedImageSize?.ToString() ?? "n/a"}");
            }
        }

        if (report.Reasons.Count > 0)
        {
            lines.Add("Reasons:");
            lines.AddRange(report.Reasons.Select(static reason => $"- {reason}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}

public sealed class WriteIntentValidator
{
    private readonly SafetyPolicyEngine _safetyPolicyEngine;

    public WriteIntentValidator(SafetyPolicyEngine? safetyPolicyEngine = null)
    {
        _safetyPolicyEngine = safetyPolicyEngine ?? new SafetyPolicyEngine();
    }

    public WriteIntentValidationResult Validate(WriteIntentValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ledger = new BackupLedger(request.BackupRootDirectory);
        var latestBackup = request.Operation == RadioOperation.WriteCodeplug
            ? ledger.FindLatestCodeplugBackup(request.Identity)
            : null;

        var effectiveOptions = request.SafetyPolicy with
        {
            BackupCompleted = request.SafetyPolicy.BackupCompleted || latestBackup is not null,
        };

        var compatibility = _safetyPolicyEngine.EvaluateCompatibility(request.Identity);
        var safetyDecision = _safetyPolicyEngine.Evaluate(request.Identity, request.Operation, effectiveOptions);
        var reasons = new List<string>(safetyDecision.Reasons);

        if (request.RequireCompatibleTarget && !compatibility.IsKnownFamily)
        {
            reasons.Add("Target is not a known Bao1702-family device in the compatibility matrix.");
        }

        if (request.Operation == RadioOperation.WriteCodeplug && latestBackup is null)
        {
            reasons.Add("No prior codeplug backup exists in the backup ledger for this target.");
        }

        var isAllowed = safetyDecision.IsAllowed
            && (!request.RequireCompatibleTarget || compatibility.IsKnownFamily)
            && (request.Operation != RadioOperation.WriteCodeplug || latestBackup is not null);

        return new WriteIntentValidationResult(
            isAllowed,
            isAllowed ? "Write intent validated." : "Write intent rejected.",
            reasons,
            latestBackup,
            compatibility,
            safetyDecision);
    }

    public WritePreflightReport BuildPreflight(
        WriteIntentValidationRequest request,
        bool isImageValidationAvailable = false,
        bool isImageValid = true,
        int? expectedImageSize = null,
        int? actualImageSize = null,
        IReadOnlyList<string>? imageValidationMessages = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var intentValidation = Validate(request);
        var imageMessages = imageValidationMessages ?? [];

        return new WritePreflightReport(
            request.Identity,
            request.Operation,
            intentValidation,
            isImageValidationAvailable,
            isImageValid,
            expectedImageSize,
            actualImageSize,
            imageMessages);
    }
}
