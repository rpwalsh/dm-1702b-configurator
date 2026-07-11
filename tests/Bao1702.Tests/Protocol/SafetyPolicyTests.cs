using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;

namespace Bao1702.Tests.Protocol;

[TestClass]
public sealed class SafetyPolicyTests
{
    [TestMethod]
    public void UnknownTargetWrite_IsBlockedByDefault()
    {
        var identity = new RadioIdentity(
            RadioFamily.Unknown,
            RadioVariant.Unknown,
            "Unknown",
            new FirmwareVersion("?"),
            new BootloaderVersion("?"),
            null,
            new RadioCapabilities(true, false, true, false, false, 4096, 8192, ConfidenceLevel.Unknown),
            ConfidenceLevel.Unknown);

        var decision = new SafetyPolicyEngine().Evaluate(identity, RadioOperation.WriteCodeplug, SafetyPolicyOptions.Default);

        Assert.AreEqual(SafetyDecisionKind.Block, decision.Kind);
    }

    [TestMethod]
    public void KnownTargetWriteWithBackup_IsAllowed()
    {
        var identity = new RadioIdentity(
            RadioFamily.Bao1702,
            RadioVariant.Bao1702B,
            "1702B",
            new FirmwareVersion("V1"),
            new BootloaderVersion("BL1"),
            "ABC",
            new RadioCapabilities(true, true, true, false, true, 4096, 8192, ConfidenceLevel.Inferred),
            ConfidenceLevel.Inferred);

        var options = SafetyPolicyOptions.Default with { BackupCompleted = true };
        var decision = new SafetyPolicyEngine().Evaluate(identity, RadioOperation.WriteCodeplug, options);

        Assert.AreEqual(SafetyDecisionKind.Allow, decision.Kind);
    }

    [TestMethod]
    public void KnownTargetWriteWithoutBackup_IsBlocked()
    {
        var identity = CreateKnownIdentity();
        var options = SafetyPolicyOptions.Default;

        var decision = new SafetyPolicyEngine().Evaluate(identity, RadioOperation.WriteCodeplug, options);

        Assert.AreEqual(SafetyDecisionKind.Block, decision.Kind);
        Assert.IsTrue(decision.Reasons.Any(r => r.Contains("backup", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void UnknownTargetRead_IsAllowedWhenPolicyPermits()
    {
        var identity = new RadioIdentity(
            RadioFamily.Unknown,
            RadioVariant.Unknown,
            "Unknown",
            new FirmwareVersion("?"),
            new BootloaderVersion("?"),
            null,
            new RadioCapabilities(true, false, true, false, false, 4096, 8192, ConfidenceLevel.Unknown),
            ConfidenceLevel.Unknown);

        var options = SafetyPolicyOptions.Default with { AllowUnknownReadOnly = true };
        var decision = new SafetyPolicyEngine().Evaluate(identity, RadioOperation.ReadCodeplug, options);

        Assert.IsTrue(decision.IsAllowed);
    }

    [TestMethod]
    public void UnknownTargetRead_IsBlockedWhenPolicyDenies()
    {
        var identity = new RadioIdentity(
            RadioFamily.Unknown,
            RadioVariant.Unknown,
            "Unknown",
            new FirmwareVersion("?"),
            new BootloaderVersion("?"),
            null,
            new RadioCapabilities(true, false, true, false, false, 4096, 8192, ConfidenceLevel.Unknown),
            ConfidenceLevel.Unknown);

        var options = SafetyPolicyOptions.Default with { AllowUnknownReadOnly = false };
        var decision = new SafetyPolicyEngine().Evaluate(identity, RadioOperation.ReadCodeplug, options);

        Assert.AreEqual(SafetyDecisionKind.Block, decision.Kind);
    }

    [TestMethod]
    public void ForceUnsafe_OverridesWriteBlock()
    {
        var identity = new RadioIdentity(
            RadioFamily.Bao1702,
            RadioVariant.Dm1702,
            "DM-1702",
            new FirmwareVersion("V1"),
            new BootloaderVersion("BL1"),
            "SERIAL",
            new RadioCapabilities(true, false, true, false, false, 4096, 8192, ConfidenceLevel.Inferred),
            ConfidenceLevel.Inferred);

        var options = SafetyPolicyOptions.Default with
        {
            ForceUnsafe = true,
            BackupCompleted = true,
        };

        var decision = new SafetyPolicyEngine().Evaluate(identity, RadioOperation.WriteCodeplug, options);

        Assert.IsTrue(decision.IsAllowed);
        Assert.AreEqual(SafetyDecisionKind.AllowWithWarnings, decision.Kind);
        Assert.IsTrue(decision.Reasons.Any(r => r.Contains("Unsafe override", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CompatibilityMatrix_RecognizesBao1702B()
    {
        var identity = CreateKnownIdentity();
        var result = CompatibilityMatrix.Evaluate(identity);

        Assert.IsTrue(result.IsKnownFamily);
        Assert.IsTrue(result.IsWriteSafeByDefault);
    }

    [TestMethod]
    public void CompatibilityMatrix_BlocksDm1702WriteWhenCodeplugWriteCapabilityMissing()
    {
        var identity = new RadioIdentity(
            RadioFamily.Bao1702,
            RadioVariant.Dm1702,
            "DM-1702",
            new FirmwareVersion("V1"),
            new BootloaderVersion("BL1"),
            null,
            new RadioCapabilities(true, false, true, false, false, 4096, 8192, ConfidenceLevel.Inferred),
            ConfidenceLevel.Inferred);

        var result = CompatibilityMatrix.Evaluate(identity);

        Assert.IsTrue(result.IsKnownFamily);
        Assert.IsFalse(result.IsWriteSafeByDefault);
    }

    private static RadioIdentity CreateKnownIdentity()
    {
        return new RadioIdentity(
            RadioFamily.Bao1702,
            RadioVariant.Bao1702B,
            "1702B",
            new FirmwareVersion("V1"),
            new BootloaderVersion("BL1"),
            "ABC",
            new RadioCapabilities(true, true, true, false, true, 4096, 8192, ConfidenceLevel.Inferred),
            ConfidenceLevel.Inferred);
    }
}
