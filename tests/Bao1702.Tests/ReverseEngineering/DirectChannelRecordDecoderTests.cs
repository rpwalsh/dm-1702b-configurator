using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class DirectChannelRecordDecoderTests
{
    [TestMethod]
    public void Decode_FindsPackedFrequenciesInThirtyByteRecords()
    {
        var image = new byte[0x3012 + (2 * 0x30) + 16];
        WriteUInt32(image, 0x3012, 16240000);
        WriteUInt32(image, 0x3016, 16210000);
        image[0x301A] = 1;
        WriteUInt32(image, 0x3042, 16340000);
        WriteUInt32(image, 0x3046, 16310000);
        image[0x304A] = 2;

        var records = DirectChannelRecordDecoder.Decode(image, 0x3012, 4, 0x30);

        Assert.AreEqual(2, records.Count);
        Assert.AreEqual(162.4000, records[0].RxFrequencyMHz, 0.0001);
        Assert.AreEqual(162.1000, records[0].TxFrequencyMHz, 0.0001);
        Assert.AreEqual(163.4000, records[1].RxFrequencyMHz, 0.0001);
        Assert.AreEqual(2, records[1].ModeOrFlags);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), value);
    }
}
