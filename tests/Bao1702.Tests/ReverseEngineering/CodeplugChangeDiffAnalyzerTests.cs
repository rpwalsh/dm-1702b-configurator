using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class CodeplugChangeDiffAnalyzerTests
{
    [TestMethod]
    public void Analyze_ClassifiesNameAndFrequencyOffsets()
    {
        var baseline = new byte[0x6000];
        var modified = (byte[])baseline.Clone();
        modified[0x4006] = (byte)'e';
        modified[0x3012] = 0x34;
        modified[0x5019] = 0x38;

        var analysis = CodeplugChangeDiffAnalyzer.Analyze("base.data", baseline, "changed.data", modified);

        Assert.AreEqual(3, analysis.ByteChanges.Count);
        Assert.IsTrue(analysis.ChangedFields.Any(static field => field.Category == "ChannelNameTable"));
        Assert.IsTrue(analysis.ChangedFields.Any(static field => field.Category == "ChannelRecordRxFrequency"));
        Assert.IsTrue(analysis.ChangedFields.Any(static field => field.Category == "MetadataOrTimestamp"));
    }
}
