using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class PcapFileParserTests
{
    [TestMethod]
    public void ParseRecords_ReadsClassicLittleEndianPcapPayloads()
    {
        var packet1 = new byte[] { 0x1B, 0x00, 0x00, 0x00 };
        var packet2 = new byte[] { 0x52, 0xFF, 0x1F, 0x00, 0x01 };
        var pcap = BuildPcap(packet1, packet2);

        var records = PcapFileParser.ParseRecords(pcap);

        Assert.AreEqual(2, records.Count);
        CollectionAssert.AreEqual(packet1, records[0]);
        CollectionAssert.AreEqual(packet2, records[1]);
    }

    private static byte[] BuildPcap(params byte[][] packets)
    {
        using var stream = new MemoryStream();

        stream.Write(new byte[]
        {
            0xD4, 0xC3, 0xB2, 0xA1,
            0x02, 0x00,
            0x04, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF, 0x00, 0x00,
            0xF9, 0x00, 0x00, 0x00,
        });

        foreach (var packet in packets)
        {
            var length = BitConverter.GetBytes((uint)packet.Length);
            stream.Write(new byte[8]);
            stream.Write(length);
            stream.Write(length);
            stream.Write(packet);
        }

        return stream.ToArray();
    }
}
