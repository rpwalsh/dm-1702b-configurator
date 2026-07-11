using Bao1702.Transport.Framing;

namespace Bao1702.Tests.Transport;

[TestClass]
public sealed class FrameCodecTests
{
    [TestMethod]
    public void EncodeThenDecode_RoundTripsPayload()
    {
        var payload = new byte[] { 0x01, 0x02, 0xA5, 0xFF };

        var encoded = TransportFrameCodec.Encode(payload);
        var decoded = TransportFrameCodec.TryDecode(encoded, out var actualPayload, out var error);

        Assert.IsTrue(decoded, error);
        CollectionAssert.AreEqual(payload, actualPayload);
    }

    [TestMethod]
    public void ComputeCrc16Ccitt_ReturnsExpectedValue()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("123456789");

        var crc = Checksums.ComputeCrc16Ccitt(data);

        Assert.AreEqual(0x29B1, crc);
    }
}
