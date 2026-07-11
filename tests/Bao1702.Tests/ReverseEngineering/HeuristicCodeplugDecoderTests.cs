using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class HeuristicCodeplugDecoderTests
{
    [TestMethod]
    public void Decode_FindsChannelLikeStringAndNearbyFrequencyPair()
    {
        var image = new byte[64];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes("Local 1");
        Array.Copy(nameBytes, 0, image, 0, nameBytes.Length);
        image[16] = 0x44;
        image[17] = 0x62;
        image[18] = 0x50;
        image[19] = 0x00;
        image[20] = 0x44;
        image[21] = 0x62;
        image[22] = 0x50;
        image[23] = 0x00;

        var decoded = HeuristicCodeplugDecoder.Decode(image);

        Assert.IsTrue(decoded.Strings.Any(static entry => entry.Text == "Local 1"));
        Assert.IsTrue(decoded.FrequencyPairs.Any(static pair => Math.Abs(pair.RxFrequencyMHz - 446.25000) < 0.0001));
        Assert.IsTrue(decoded.ChannelCandidates.Any(static candidate => candidate.Name == "Local 1" && candidate.RxFrequencyMHz.HasValue));
    }
}
