using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class WriteSessionAnalyzerTests
{
    [TestMethod]
    public void AnalyzeTsharkFieldLines_FindsHostWriteBlocks()
    {
        var lines = new[]
        {
            "33707\t0x01\t0x03\t69\t570080054015010001000000000000000000000000536b797761726e202f20415245530000090e002a002b002c002d002e0059005a005b000a000b000c000d000e000f0010",
            "33711\t0x01\t0x03\t69\t574080054000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
        };

        var analysis = WriteSessionAnalyzer.AnalyzeTsharkFieldLines(lines);

        Assert.AreEqual(2, analysis.Blocks.Count);
        Assert.AreEqual(0x058000, analysis.Blocks[0].Address);
        Assert.AreEqual(0x00, analysis.Blocks[0].WindowOffset);
        StringAssert.Contains(analysis.Blocks[0].AsciiPreview, "Skywarn / ARES");
        Assert.AreEqual(1, analysis.WindowWriteCounts[0x00]);
        Assert.AreEqual(1, analysis.WindowWriteCounts[0x40]);
    }
}
