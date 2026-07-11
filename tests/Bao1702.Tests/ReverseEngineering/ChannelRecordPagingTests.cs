using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

/// <summary>
/// Tests for channel record paging: linear region (0-84) and overflow pages (85+).
/// Validates <see cref="Dm1702NativeImageAssumptions.GetChannelRecordOffset"/> and
/// <see cref="Dm1702NativeImageAssumptions.GetChannelNameOffset"/> against known binary evidence.
/// </summary>
[TestClass]
public sealed class ChannelRecordPagingTests
{
    [TestMethod]
    public void GetChannelRecordOffset_LinearRegion_ReturnsCorrectOffsets()
    {
        Assert.AreEqual(0x3010, Dm1702NativeImageAssumptions.GetChannelRecordOffset(0));
        Assert.AreEqual(0x3040, Dm1702NativeImageAssumptions.GetChannelRecordOffset(1));
        Assert.AreEqual(0x3FD0, Dm1702NativeImageAssumptions.GetChannelRecordOffset(84));
    }

    [TestMethod]
    public void GetChannelRecordOffset_PagedRegion_ReturnsCorrectOffsets()
    {
        Assert.AreEqual(0xF030, Dm1702NativeImageAssumptions.GetChannelRecordOffset(85));

        // i=86: j=2, page=0, slot=2 ? 0xF000+2*0x30=0xF060
        Assert.AreEqual(0xF060, Dm1702NativeImageAssumptions.GetChannelRecordOffset(86));

        // i=168: j=84, page=0, slot=84 ? 0xF000+84*0x30=0xFFC0 (last slot on page 0)
        Assert.AreEqual(0xFFC0, Dm1702NativeImageAssumptions.GetChannelRecordOffset(168));

        // i=169: j=85, page=1, slot=0 ? 0x10000+0*0x30=0x10000 (phone-list slot of page 1)
        Assert.AreEqual(0x10000, Dm1702NativeImageAssumptions.GetChannelRecordOffset(169));

        // i=170: j=86, page=1, slot=1 ? 0x10000+1*0x30=0x10030 (first real channel slot on page 1)
        Assert.AreEqual(0x10030, Dm1702NativeImageAssumptions.GetChannelRecordOffset(170));
    }

    [TestMethod]
    public void GetChannelNameOffset_ContiguousTable_ReturnsCorrectOffsets()
    {
        Assert.AreEqual(0x4000, Dm1702NativeImageAssumptions.GetChannelNameOffset(0));
        Assert.AreEqual(0x4000 + 49 * 11, Dm1702NativeImageAssumptions.GetChannelNameOffset(49));

        // Index 50 should match Table2Start (0x4226) — the contiguous formula must agree.
        Assert.AreEqual(0x4226, Dm1702NativeImageAssumptions.GetChannelNameOffset(50));

        Assert.AreEqual(0x4000 + 371 * 11, Dm1702NativeImageAssumptions.GetChannelNameOffset(371));
    }

    [TestMethod]
    public void DecodeAllChannels_ReadsPagedChannelRecords()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];

        // Write a linear channel at index 0
        var linOffset = Dm1702NativeImageAssumptions.GetChannelRecordOffset(0);
        WriteBcdFrequency(image, linOffset, 146.5200);
        WriteBcdFrequency(image, linOffset + 4, 146.5200);
        var linNameOff = Dm1702NativeImageAssumptions.GetChannelNameOffset(0);
        System.Text.Encoding.ASCII.GetBytes("LINEAR CH").CopyTo(image.AsSpan(linNameOff));

        // Write a paged channel at index 85 (first overflow slot)
        var pgOffset = Dm1702NativeImageAssumptions.GetChannelRecordOffset(85);
        Assert.AreEqual(0xF030, pgOffset, "Index 85 should map to the independently verified first overflow record.");
        WriteBcdFrequency(image, pgOffset, 430.9000);
        WriteBcdFrequency(image, pgOffset + 4, 430.9000);
        var pgNameOff = Dm1702NativeImageAssumptions.GetChannelNameOffset(85);
        System.Text.Encoding.ASCII.GetBytes("PAGED CH").CopyTo(image.AsSpan(pgNameOff));

        var records = ObservedChannelRecordDecoder.DecodeAllChannels(image);

        var ch0 = records.FirstOrDefault(static r => r.Index == 0);
        var ch85 = records.FirstOrDefault(static r => r.Index == 85);

        Assert.IsNotNull(ch0, "Linear channel 0 should be decoded");
        Assert.AreEqual("LINEAR CH", ch0.Name);
        Assert.AreEqual(146.52, ch0.RxFrequencyMHz, 0.001);

        Assert.IsNotNull(ch85, "Paged channel 85 should be decoded");
        Assert.AreEqual("PAGED CH", ch85.Name);
        Assert.AreEqual(430.9, ch85.RxFrequencyMHz, 0.001);
    }

    [TestMethod]
    public void BuildAndReadRoundTrip_PagedChannel_PreservesData()
    {
        var channels = new List<Channel>
        {
            new AnalogChannel(1, "VHF CH1", 146_520_000, 146_520_000,
                PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.Always, [],
                ToneValue.None, ToneValue.None, CodeplugConfidence.Confirmed),
            new AnalogChannel(86, "UHF PAGE", 430_900_000, 430_900_000,
                PowerLevel.High, ChannelBandwidth.Wide, AdmitCriteria.Always, [],
                ToneValue.None, ToneValue.None, CodeplugConfidence.Confirmed),
        };

        var codeplug = CodeplugImage.CreateEmpty();
        var image = codeplug with { Channels = channels };

        var nativeBytes = Dm1702NativeImageBuilder.Build(image);

        // Verify the paged record was written at the correct offset
        var pagedOffset = Dm1702NativeImageAssumptions.GetChannelRecordOffset(85); // 0xF032
        Assert.IsTrue(ObservedChannelRecordDecoder.TryDecodeObservedFrequency(
            nativeBytes.AsSpan(pagedOffset, 4), out var rxMHz));
        Assert.AreEqual(430.9, rxMHz, 0.001, "Paged record RX frequency should be 430.9 MHz");

        // Verify name was written at contiguous offset
        var nameOffset = Dm1702NativeImageAssumptions.GetChannelNameOffset(85);
        var nameBytes = nativeBytes.AsSpan(nameOffset, 11).ToArray();
        var name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
        Assert.AreEqual("UHF PAGE", name, "Paged channel name should be at contiguous offset");

        // Read back and verify
        var readBack = Dm1702NativeImageReader.ReadFromNative(nativeBytes);
        var ch86 = readBack.Channels.FirstOrDefault(c => c.Index == 86);
        Assert.IsNotNull(ch86, "Channel 86 (1-based) should be read back from paged region");
        Assert.AreEqual("UHF PAGE", ch86.Name);
        Assert.AreEqual(430_900_000, ch86.RxFrequencyHz);
    }

    private static void WriteBcdFrequency(byte[] image, int offset, double frequencyMHz)
    {
        // OEM: little-endian packed BCD. digits[6..7] in byte[0] (LSB), digits[0..1] in byte[3] (MSB).
        var digits = ((int)Math.Round(frequencyMHz * 100000d)).ToString("D8");
        image[offset]     = byte.Parse(digits.Substring(6, 2), System.Globalization.NumberStyles.HexNumber); // LSB
        image[offset + 1] = byte.Parse(digits.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        image[offset + 2] = byte.Parse(digits.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        image[offset + 3] = byte.Parse(digits.Substring(0, 2), System.Globalization.NumberStyles.HexNumber); // MSB
    }
}
