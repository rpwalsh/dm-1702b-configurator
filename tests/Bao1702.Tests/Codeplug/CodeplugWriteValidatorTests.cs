using Bao1702.Codeplug.Model;
using Bao1702.Codeplug.Validation;

namespace Bao1702.Tests.Codeplug;

[TestClass]
public sealed class CodeplugWriteValidatorTests
{
    [TestMethod]
    public void ValidateImage_BlocksLengthMismatch()
    {
        var result = CodeplugWriteValidator.ValidateImage(new byte[16], expectedImageSize: 32);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("does not match the expected target size", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ValidateModel_BlocksDuplicateChannelNames()
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, "Local", 145_500_000, 145_500_000, PowerLevel.High, ChannelBandwidth.Wide, AdmitCriteria.Always, ["Simplex"], ToneValue.Parse("67.0"), ToneValue.Parse("67.0"), CodeplugConfidence.Inferred),
                new AnalogChannel(2, "Local", 145_600_000, 145_600_000, PowerLevel.High, ChannelBandwidth.Wide, AdmitCriteria.Always, ["Simplex"], ToneValue.Parse("67.0"), ToneValue.Parse("67.0"), CodeplugConfidence.Inferred),
            ],
            Zones = [new Zone("Simplex", ["Local"], CodeplugConfidence.Inferred)],
        };

        var result = CodeplugWriteValidator.ValidateModel(image);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("duplicated", StringComparison.Ordinal)));
    }
}
