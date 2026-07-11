using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;

namespace Bao1702.Tests.Protocol;

[TestClass]
public sealed class WriteIntentValidatorTests
{
    [TestMethod]
    public async Task Validate_BlocksCodeplugWriteWhenNoBackupExists()
    {
        var identity = CreateKnownIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var result = new WriteIntentValidator().Validate(new WriteIntentValidationRequest(
                identity,
                RadioOperation.WriteCodeplug,
                backupRoot,
                SafetyPolicyOptions.Default));

            Assert.IsFalse(result.IsAllowed);
            Assert.IsTrue(result.Reasons.Any(reason => reason.Contains("No prior codeplug backup exists", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildPreflight_IncludesImageValidationFailures()
    {
        var identity = CreateKnownIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var ledger = new BackupLedger(backupRoot);
            await ledger.RecordCodeplugBackupAsync(identity, new byte[identity.Capabilities.AssumedCodeplugSize], "test fixture").ConfigureAwait(false);

            var imageValidation = Bao1702.Codeplug.Validation.CodeplugWriteValidator.ValidateImage(
                new byte[identity.Capabilities.AssumedCodeplugSize - 1],
                identity.Capabilities.AssumedCodeplugSize);

            var preflight = new WriteIntentValidator().BuildPreflight(
                new WriteIntentValidationRequest(
                    identity,
                    RadioOperation.WriteCodeplug,
                    backupRoot,
                    SafetyPolicyOptions.Default),
                isImageValidationAvailable: true,
                isImageValid: imageValidation.IsValid,
                expectedImageSize: imageValidation.ExpectedImageSize,
                actualImageSize: imageValidation.ActualImageSize,
                imageValidationMessages: imageValidation.Issues.Select(static issue => issue.Message).ToArray());

            Assert.IsFalse(preflight.IsAllowed);
            Assert.IsTrue(preflight.Reasons.Any(reason => reason.Contains("does not match the expected target size", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task WritePreflightFormatter_FormatsBackupAndSafetyDetails()
    {
        var identity = CreateKnownIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var ledger = new BackupLedger(backupRoot);
            await ledger.RecordCodeplugBackupAsync(identity, new byte[identity.Capabilities.AssumedCodeplugSize], "test fixture").ConfigureAwait(false);

            var preflight = new WriteIntentValidator().BuildPreflight(
                new WriteIntentValidationRequest(
                    identity,
                    RadioOperation.WriteCodeplug,
                    backupRoot,
                    SafetyPolicyOptions.Default));

            var text = WritePreflightFormatter.FormatText(preflight);

            Assert.IsTrue(text.Contains("Target: Baofeng 1702B / Bao1702B", StringComparison.Ordinal));
            Assert.IsTrue(text.Contains("Latest backup:", StringComparison.Ordinal));
            Assert.IsTrue(text.Contains("Safety decision:", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task Validate_AllowsCodeplugWriteWhenBackupExists()
    {
        var identity = CreateKnownIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var ledger = new BackupLedger(backupRoot);
            await ledger.RecordCodeplugBackupAsync(identity, new byte[identity.Capabilities.AssumedCodeplugSize], "test fixture").ConfigureAwait(false);

            var result = new WriteIntentValidator().Validate(new WriteIntentValidationRequest(
                identity,
                RadioOperation.WriteCodeplug,
                backupRoot,
                SafetyPolicyOptions.Default));

            Assert.IsTrue(result.IsAllowed);
            Assert.IsNotNull(result.LatestBackup);
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task Validate_BlocksUnknownUsbTargetEvenWhenBackupExists()
    {
        var identity = CreateKnownIdentity() with
        {
            Family = RadioFamily.Unknown,
            Variant = RadioVariant.Unknown,
            ModelName = "TEST UNKNOWN USB DEVICE",
        };
        var backupRoot = CreateTemporaryDirectory();
        try
        {
            var ledger = new BackupLedger(backupRoot);
            await ledger.RecordCodeplugBackupAsync(identity, new byte[identity.Capabilities.AssumedCodeplugSize], "synthetic test fixture").ConfigureAwait(false);

            var result = new WriteIntentValidator().Validate(new WriteIntentValidationRequest(
                identity, RadioOperation.WriteCodeplug, backupRoot, SafetyPolicyOptions.Default));

            Assert.IsFalse(result.IsAllowed);
            Assert.IsTrue(result.Reasons.Any(reason => reason.Contains("not a known", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    private static RadioIdentity CreateKnownIdentity()
    {
        return new RadioIdentity(
            RadioFamily.Bao1702,
            RadioVariant.Bao1702B,
            "Baofeng 1702B",
            new FirmwareVersion("V02.07.001"),
            new BootloaderVersion("BL01.03"),
            "1702B-TEST-0001",
            new RadioCapabilities(true, true, true, false, true, 4096, 8192, ConfidenceLevel.Inferred),
            ConfidenceLevel.Inferred);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Bao1702Suite.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
