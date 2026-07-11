using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class ChannelRecordTableDetectorTests
{
    [TestMethod]
    public void Detect_FindsRepeatingBinaryTableAfterChannelNames()
    {
        var image = new byte[512];
        var names = new[] { "CHAN 1", "CHAN 2", "CHAN 3", "CHAN 4" };
        for (var index = 0; index < names.Length; index++)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(names[index]);
            Array.Copy(bytes, 0, image, index * 11, bytes.Length);
        }

        image[44] = 0;
        image[45] = 0;
        image[46] = 0;
        image[47] = 0;
        image[48] = 0;
        image[49] = 0;
        image[50] = 0;
        image[51] = 0;
        image[52] = 0;
        image[53] = 0;
        image[54] = 0;
        image[55] = 0;
        image[56] = 0;
        image[57] = 0;
        image[58] = 0;
        image[59] = 0;
        image[60] = 0;
        image[61] = 0;
        image[62] = 0;
        image[63] = 0;

        var pattern = new byte[] { 0xD0, 0xC5, 0xB5, 0xC0, 0x20, 0x32, 0x00, 0x00, 0x00, 0x00, 0x00 };
        for (var index = 0; index < 8; index++)
        {
            Array.Copy(pattern, 0, image, 64 + (index * pattern.Length), pattern.Length);
        }

        var tables = new[]
        {
            new InferredStringTable(InferredStringTableKind.ChannelName, 0, 11, 4, names, CodeplugConfidence.Inferred),
        };

        var detected = ChannelRecordTableDetector.Detect(image, tables);

        Assert.AreEqual(1, detected.Count);
        Assert.AreEqual(64, detected[0].StartOffset);
        Assert.AreEqual(11, detected[0].Stride);
        Assert.AreEqual(8, detected[0].EntryCount);
    }
}
