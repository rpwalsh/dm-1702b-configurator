using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class WriteBlockMapperTests
{
    [TestMethod]
    public void MapToSavedCodeplug_FindsUniqueOffsetsForWriteBlocks()
    {
        var codeplug = new byte[256];
        var blockData = System.Text.Encoding.ASCII.GetBytes("WEATHER BAND").Concat(new byte[64 - 12]).ToArray();
        Array.Copy(blockData, 0, codeplug, 0x60, blockData.Length);

        var mappings = WriteBlockMapper.MapToSavedCodeplug(
        [
            new WriteBlock(33723, 0x000100, 0x00, blockData, "WEATHER BAND")
        ], codeplug);

        Assert.AreEqual(1, mappings.Count);
        Assert.AreEqual(0x60, mappings[0].FileOffset);
        Assert.AreEqual(CodeplugConfidence.Inferred, mappings[0].Confidence);
    }
}
