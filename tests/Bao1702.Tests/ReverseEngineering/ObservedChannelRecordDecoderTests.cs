using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class ObservedChannelRecordDecoderTests
{
    [TestMethod]
    public void DecodeAllChannels_ParsesSyntheticFrequencyRecords()
    {
        // Image must be large enough for records (0x3010+84*0x30) and names (Table1: 0x4000+50*11)
        var image = new byte[Bao1702.Codeplug.Model.Dm1702NativeImageAssumptions.ImageLength];
        WriteName(image, 0, "TEST-CH-001");
        WriteName(image, 1, "TEST-CH-002");
        WriteObservedFrequency(image, 0x3010, 162.4000, 162.4000);
        WriteObservedFrequency(image, 0x3040, 162.4250, 162.4250);

        var records = ObservedChannelRecordDecoder.DecodeAllChannels(image);

        var named = records.Where(static record => !string.IsNullOrWhiteSpace(record.Name)).ToArray();
        Assert.AreEqual(2, named.Length);
        Assert.AreEqual(162.4, records[0].RxFrequencyMHz, 0.0001);
        Assert.AreEqual(162.425, records[1].RxFrequencyMHz, 0.0001);
        Assert.AreEqual("TEST-CH-001", records[0].Name);
        Assert.AreEqual("TEST-CH-002", records[1].Name);
    }

    [TestMethod]
    public void DecodeAllChannels_ParsesTable2ChannelNamesCorrectly()
    {
        // Channel index 50 uses Table2 starting at 0x4226
        var image = new byte[Bao1702.Codeplug.Model.Dm1702NativeImageAssumptions.ImageLength];
        var nameOffset = 0x4226; // Table2 slot 0 = channel index 50
        var recordOffset = 0x3010 + (50 * 0x30); // record for channel 50
        System.Text.Encoding.ASCII.GetBytes("TEST-CH-050").CopyTo(image.AsSpan(nameOffset));
        WriteObservedFrequency(image, recordOffset, 151.8200, 151.8200);

        var records = ObservedChannelRecordDecoder.DecodeAllChannels(image);

        var ch50 = records.FirstOrDefault(static r => r.Index == 50);
        Assert.IsNotNull(ch50);
        Assert.AreEqual("TEST-CH-050", ch50.Name);
        Assert.AreEqual(151.82, ch50.RxFrequencyMHz, 0.0001);
    }

    private static void WriteName(byte[] image, int index, string name)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Copy(bytes, 0, image, 0x4000 + (index * 11), bytes.Length);
    }

    private static void WriteObservedFrequency(byte[] image, int offset, double rx, double tx)
    {
        WriteObservedFrequencyBytes(image, offset, rx);
        WriteObservedFrequencyBytes(image, offset + 4, tx);
    }

    private static void WriteObservedFrequencyBytes(byte[] image, int offset, double frequency)
    {
        // OEM: little-endian packed BCD. digits[6..7] in byte[0] (LSB), digits[0..1] in byte[3] (MSB).
        var digits = ((int)Math.Round(frequency * 100000d)).ToString("D8");
        image[offset]     = byte.Parse(digits.Substring(6, 2), System.Globalization.NumberStyles.HexNumber); // LSB
        image[offset + 1] = byte.Parse(digits.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        image[offset + 2] = byte.Parse(digits.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        image[offset + 3] = byte.Parse(digits.Substring(0, 2), System.Globalization.NumberStyles.HexNumber); // MSB
    }
}
