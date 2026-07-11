using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

/// <summary>
/// Tests that DecodeAllChannels correctly pairs later channel records (index 80+)
/// with names from Table2 (0x4226 + (index-50)*11).
/// Previously, DecodeThirdTable used wrong name offsets (0x4231 for records starting at 0x3F10),
/// which mismatched record 80 with name 51 instead of name 80.
/// </summary>
[TestClass]
public sealed class ObservedChannelRecordDecoderThirdTableTests
{
    [TestMethod]
    public void DecodeAllChannels_ParsesLaterChannelNamesFromTable2()
    {
        var image = new byte[Bao1702.Codeplug.Model.Dm1702NativeImageAssumptions.ImageLength];

        // Channel index 80: record at 0x3010+80*0x30=0x3F10, name from Table2 at 0x4226+(80-50)*11=0x4370
        var nameOffset80 = 0x4226 + (30 * 11); // 0x4370
        System.Text.Encoding.ASCII.GetBytes("P. UHF 3").CopyTo(image.AsSpan(nameOffset80));
        WriteObservedFrequency(image, 0x3F10, 446.0300, 446.0300);

        // Channel index 81: record at 0x3F40, name at 0x4226+31*11=0x437B
        var nameOffset81 = 0x4226 + (31 * 11);
        System.Text.Encoding.ASCII.GetBytes("P. VHF 3").CopyTo(image.AsSpan(nameOffset81));
        WriteObservedFrequency(image, 0x3F40, 146.4200, 146.4200);

        var records = ObservedChannelRecordDecoder.DecodeAllChannels(image);

        var ch80 = records.FirstOrDefault(static r => r.Index == 80);
        var ch81 = records.FirstOrDefault(static r => r.Index == 81);
        Assert.IsNotNull(ch80);
        Assert.IsNotNull(ch81);
        Assert.AreEqual("P. UHF 3", ch80.Name);
        Assert.AreEqual(446.03, ch80.RxFrequencyMHz, 0.0001);
        Assert.AreEqual("P. VHF 3", ch81.Name);
        Assert.AreEqual(146.42, ch81.RxFrequencyMHz, 0.0001);
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
