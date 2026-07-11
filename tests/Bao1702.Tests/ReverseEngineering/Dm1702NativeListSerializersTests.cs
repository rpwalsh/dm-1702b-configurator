using System.Buffers.Binary;
using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class Dm1702NativeListSerializersTests
{
    [TestMethod]
    public void Write_SerializesRxGroupsScanListsAndZones()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        Contact[] contacts =
        [
            new Contact("907 [TGRP001]", 907, ContactType.Group, CodeplugConfidence.Inferred),
            new Contact("9990 [TCT001]", 9990, ContactType.Group, CodeplugConfidence.Inferred),
        ];
        var channelIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["TEST-CH-073"] = 73,
            ["TCT001 u"] = 61,
        };

        Dm1702NativeRxGroupSerializer.Write(image, [new RxGroup("[TGRP001]", ["907 [TGRP001]", "9990 [TCT001]"], CodeplugConfidence.Inferred)], contacts);
        Dm1702NativeScanListSerializer.Write(image, [new ScanList("TGRP1 Scan", ["TEST-CH-073", "TCT001 u"], CodeplugConfidence.Inferred)], channelIndexByName);
        Dm1702NativeZoneSerializer.Write(image, [new Zone("TGRP001 Zone", ["TEST-CH-073", "TCT001 u"], CodeplugConfidence.Inferred)], channelIndexByName);

        //   +0x00: 0x00
        //   +0x01..+0x0B: name (11 bytes ASCII, null-padded)
        //   +0x0C..+0x0E: TalkGroup ID as LE16 + 0x00 pad
        //   +0x0F..+0x11: reserved zeros
        //   +0x12..+0x6B: member 1-based contact indices (LE16 + 0x00 pad, stride 3, max 30)
        var rxBase = Dm1702NativeImageAssumptions.RxGroupRegion1Start;
        Assert.AreEqual((byte)'[', image[rxBase + 0x01], "RxGroup name starts at +0x01");
        // TalkGroup ID at +0x0C: derived from first member "907 [TGRP001]" whose call ID = 907
        var tgId = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(rxBase + 0x0C, 2));
        Assert.AreEqual(907, tgId, "RxGroup TalkGroup ID at +0x0C = 907");
        Assert.AreEqual(0x00, image[rxBase + 0x0E], "RxGroup TalkGroup ID pad byte at +0x0E = 0x00");
        Assert.AreEqual(0x00, image[rxBase + 0x0F], "RxGroup reserved +0x0F = 0x00");
        Assert.AreEqual(0x00, image[rxBase + 0x10], "RxGroup reserved +0x10 = 0x00");
        Assert.AreEqual(0x00, image[rxBase + 0x11], "RxGroup reserved +0x11 = 0x00");
        // Member[0] at +0x12: 1-based contact index for "907 [TGRP001]" = index 1 (first contact)
        var member0Idx = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(rxBase + 0x12, 2));
        Assert.AreEqual(1, member0Idx, "RxGroup member[0] at +0x12 = 1-based contact index 1");
        Assert.AreEqual(0x00, image[rxBase + 0x14], "RxGroup member[0] pad byte at +0x14 = 0x00");
        // Member[1] at +0x15: 1-based contact index for "9990 [TCT001]" = index 2 (second contact)
        var member1Idx = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(rxBase + 0x15, 2));
        Assert.AreEqual(2, member1Idx, "RxGroup member[1] at +0x15 = 1-based contact index 2");
        Assert.AreEqual(0x00, image[rxBase + 0x17], "RxGroup member[1] pad byte at +0x17 = 0x00");

        // ScanList: OEM layout — name at +0x01, members at +0x19, sentinel at +0x17
        var slBase = Dm1702NativeImageAssumptions.ScanListsStart;
        Assert.AreEqual((byte)'T', image[slBase + 1], "ScanList name at +0x01");
        Assert.AreEqual(0x00, image[slBase + 0x17], "ScanList sentinel +0x17 = 0x00 (not 0xFE)");
        var slMember0 = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(slBase + 0x19, 2));
        Assert.IsTrue(slMember0 == 73 || slMember0 == 61, $"ScanList member at +0x19 should be a valid channel index, got {slMember0}");

        // Zone: OEM layout — zone count at +0x00 (dual-purpose with zone[0] header), member count at +0x20, name at +0x10, members at +0x21, echo at +0xA0
        var zBase = Dm1702NativeImageAssumptions.ZoneDataStart;
        Assert.AreEqual(1, image[zBase + 0x00], "Zone count at +0x00 (also zone[0] header byte)");
        Assert.AreEqual(0, image[zBase + 0x01], "Zone +0x01 should be 0x00 (not LE16 high byte)");
        Assert.AreEqual((byte)'T', image[zBase + 0x10], "Zone name at +0x10");
        Assert.AreEqual(2, image[zBase + 0x20], "Zone +0x20 independently verified member count");
        var zMember0 = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(zBase + 0x21, 2));
        Assert.IsTrue(zMember0 == 73 || zMember0 == 61, $"Zone member at +0x21 should be a valid channel index, got {zMember0}");
        Assert.AreEqual(0x00, image[zBase + 0xA0], "Zone echo +0xA0 = 0x00 fixed byte");
        Assert.AreEqual(2, image[zBase + 0xA1], "Zone echo +0xA1 = member count echo");
        var zEchoMember0 = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(zBase + 0xA2, 2));
        Assert.AreEqual(zMember0, zEchoMember0, "Zone echo member[0] matches +0x21 member[0]");
    }

    [TestMethod]
    public void Write_GpsSerializer_Writes4DefaultEntries()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var codeplug = CodeplugImage.CreateEmpty();

        Dm1702NativeGpsSerializer.Write(image, codeplug);

        // 16 default stock GPS entries
        for (var i = 0; i < 16; i++)
        {
            var offset = Dm1702NativeImageAssumptions.GpsSystemsStart + (i * 0x10);
            Assert.AreEqual((byte)'G', image[offset], $"GPS entry {i} name starts with 'G'");
            Assert.AreEqual(0x1B, image[offset + 0x09], $"GPS entry {i} stock magic [0x09]=0x1B");
            Assert.AreEqual(0x01, image[offset + 0x0C], $"GPS entry {i} stock magic [0x0C]=0x01");
            Assert.AreEqual((byte)(i + 1), image[offset + 0x0D], $"GPS entry {i} stock magic [0x0D]={i + 1}");
        }
    }

    [TestMethod]
    public void Write_GpsSerializer_OverridesEntry0WhenCallsignSet()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var codeplug = CodeplugImage.CreateEmpty() with
        {
            RadioIdentity = new RadioIdentitySettings("1000001", "N0CALL", CodeplugConfidence.Inferred),
        };

        Dm1702NativeGpsSerializer.Write(image, codeplug);

        var offset0 = Dm1702NativeImageAssumptions.GpsSystemsStart;
        Assert.AreEqual((byte)'N', image[offset0], "Entry 0 overwritten with callsign");
        Assert.AreEqual(0x06, image[offset0 + 0x09], "Entry 0 APRS magic [0x09]=0x06");

        // Entry 1 should still be stock
        var offset1 = Dm1702NativeImageAssumptions.GpsSystemsStart + 0x10;
        Assert.AreEqual((byte)'G', image[offset1], "Entry 1 remains stock 'GPS 2'");
        Assert.AreEqual(0x1B, image[offset1 + 0x09], "Entry 1 stock magic preserved");
    }
}
