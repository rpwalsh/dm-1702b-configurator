using System.Buffers.Binary;
using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class Dm1702NativeContactSerializerTests
{
    [TestMethod]
    public void Write_SerializesContactMetaAndContactDataFromScratch()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var contacts = new[]
        {
            new Contact("907 [TGRP001]", 907, ContactType.Group, CodeplugConfidence.Inferred),
            new Contact("9990 [TCT001]", 9990, ContactType.Group, CodeplugConfidence.Inferred),
        };

        Dm1702NativeContactSerializer.Write(image, contacts);

        Assert.AreEqual(2, BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(Dm1702NativeImageAssumptions.ContactMetaStart, 2)));
        Assert.AreEqual((byte)'9', image[Dm1702NativeImageAssumptions.ContactDataStart + 2]);
        Assert.AreEqual((byte)'9', image[Dm1702NativeImageAssumptions.ContactDataStart + Dm1702NativeImageAssumptions.ContactRecordLength + 2]);
        Assert.AreEqual(1, Dm1702NativeContactSerializer.FindContactIndex(contacts, "907 [TGRP001]"));
    }

    [TestMethod]
    public void Write_ContactBitmap_IsZeroIndexed_OemValidated()
    {
        // 7th capture 4 contacts ? bits 0-3 cleared ? 0xF0.
        var meta = Dm1702NativeImageAssumptions.ContactMetaStart;
        var bitmapStart = meta + 0x10;

        // 2 contacts: bits 0-1 cleared ? byte[0] = 0xFC, rest = 0xFF
        var image2 = new byte[Dm1702NativeImageAssumptions.ImageLength];
        Dm1702NativeContactSerializer.Write(image2,
        [
            new Contact("A", 1, ContactType.Group, CodeplugConfidence.Inferred),
            new Contact("B", 2, ContactType.Group, CodeplugConfidence.Inferred),
        ]);
        Assert.AreEqual(0xFC, image2[bitmapStart], "2 contacts: byte[0] should be 0xFC (bits 0-1 cleared)");
        Assert.AreEqual(0xFF, image2[bitmapStart + 1], "2 contacts: byte[1] should be 0xFF");

        var image18 = new byte[Dm1702NativeImageAssumptions.ImageLength];
        Dm1702NativeSectorSerializer.Initialize(image18);
        var contacts18 = new Contact[18];
        for (var i = 0; i < 18; i++)
            contacts18[i] = new Contact($"C{i + 1}", i + 1, ContactType.Group, CodeplugConfidence.Inferred);
        Dm1702NativeContactSerializer.Write(image18, contacts18);
        Assert.AreEqual(0x00, image18[bitmapStart], "18 contacts: byte[0] should be 0x00");
        Assert.AreEqual(0x00, image18[bitmapStart + 1], "18 contacts: byte[1] should be 0x00");
        Assert.AreEqual(0xFC, image18[bitmapStart + 2], "18 contacts: byte[2] should be 0xFC (bits 16-17 cleared)");
        Assert.AreEqual(0xFF, image18[bitmapStart + 3], "18 contacts: byte[3] should be 0xFF");
    }

    [TestMethod]
    public void Write_ContactRecordPaddingBytes_MatchOemLayout()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        Dm1702NativeSectorSerializer.Initialize(image);
        var contacts = new[]
        {
            new Contact("3129 [TGRP004]", 3129, ContactType.Group, CodeplugConfidence.Inferred),
        };

        Dm1702NativeContactSerializer.Write(image, contacts);

        var off = Dm1702NativeImageAssumptions.ContactDataStart;
        // Sentinel/padding bytes [0..1] = FF FF
        Assert.AreEqual(0xFF, image[off + 0], "byte[0] should be 0xFF");
        Assert.AreEqual(0xFF, image[off + 1], "byte[1] should be 0xFF");
        // Name at [2..17]
        Assert.AreEqual((byte)'3', image[off + 2], "name starts at byte[2]");
        // Separator [18] = FF
        Assert.AreEqual(0xFF, image[off + 18], "byte[18] should be 0xFF");
        // CallId LE24 at [19..21]: 3129 = 0x0C39
        Assert.AreEqual(0x39, image[off + 19], "callId low byte");
        Assert.AreEqual(0x0C, image[off + 20], "callId mid byte");
        Assert.AreEqual(0x00, image[off + 21], "callId high byte");
        // Type [22] = 4 (Group)
        Assert.AreEqual(0x04, image[off + 22], "type byte should be 4 for Group");
        // Trailer [23] = FF
        Assert.AreEqual(0xFF, image[off + 23], "byte[23] should be 0xFF");
    }

    [TestMethod]
    public void GetContactRecordImageOffset_RespectsPageBoundaries()
    {
        // 170 records per 4K page, page gap of 16 bytes at each boundary
        Assert.AreEqual(0x1F000, Dm1702NativeContactSerializer.GetContactRecordImageOffset(0));
        Assert.AreEqual(0x1F000 + 169 * 0x18, Dm1702NativeContactSerializer.GetContactRecordImageOffset(169));
        // Record 170 starts at page 1 (0x20000), not linearly at 0x1FFF0
        Assert.AreEqual(0x20000, Dm1702NativeContactSerializer.GetContactRecordImageOffset(170));
        Assert.AreEqual(0x20000 + 0x18, Dm1702NativeContactSerializer.GetContactRecordImageOffset(171));
        // Record 340 starts at page 2 (0x21000)
        Assert.AreEqual(0x21000, Dm1702NativeContactSerializer.GetContactRecordImageOffset(340));
        // Last record (799) on page 4
        Assert.AreEqual(0x23000 + 119 * 0x18, Dm1702NativeContactSerializer.GetContactRecordImageOffset(799));
    }

    [TestMethod]
    public void Write_PreservesSectorMarkersAtPageBoundaries()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        Dm1702NativeSectorSerializer.Initialize(image);

        // Capture sector markers before contact write
        var marker0 = image[0x1FFFF]; // Should be 0x28
        var marker1 = image[0x20FFF]; // Should be 0x29

        Dm1702NativeContactSerializer.Write(image, [
            new Contact("Test", 1, ContactType.Private, CodeplugConfidence.Inferred),
        ]);

        // Sector markers must survive the contact fill
        Assert.AreEqual(marker0, image[0x1FFFF], "Sector marker at 0x1FFFF should be preserved");
        Assert.AreEqual(marker1, image[0x20FFF], "Sector marker at 0x20FFF should be preserved");
    }

    [TestMethod]
    public void Write_PostBitmapRegion_MatchesCpsNewFilePattern()
    {
        //   - Scalar search: ZERO references to image 0x7074-0x70FF
        //   - All 6 computed-offset bitmap functions: READ-ONLY bit-tests
        //   - All 15 rebuild sub-functions: NONE write to 0x7074+
        // OEM evidence: CPS-created files (baseline.data etc.) = all-zeros at 0x7074+.
        // Our serializer zero-fills to match CPS "new file" behavior.
        var meta = Dm1702NativeImageAssumptions.ContactMetaStart;
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        Dm1702NativeSectorSerializer.Initialize(image);
        var contacts = new Contact[18];
        for (var i = 0; i < 16; i++)
            contacts[i] = new Contact($"G{i + 1}", i + 1, ContactType.Group, CodeplugConfidence.Inferred);
        contacts[16] = new Contact("P1", 9990, ContactType.Private, CodeplugConfidence.Inferred);
        contacts[17] = new Contact("P2", 9991, ContactType.Private, CodeplugConfidence.Inferred);

        Dm1702NativeContactSerializer.Write(image, contacts);

        // Entire +0x74..+0xFF region must be 0x00 (CPS new-file zero-fill behavior)
        for (var i = 0x74; i <= 0xFF; i++)
            Assert.AreEqual(0x00, image[meta + i], $"+0x{i:X2} should be 0x00 (opaque region, CPS never touches)");
    }

    [TestMethod]
    public void Write_RoundTrips_ThroughReader()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        Dm1702NativeSectorSerializer.Initialize(image);
        var contacts = new Contact[]
        {
            new("3129 [TGRP004]", 3129, ContactType.Group, CodeplugConfidence.Inferred),
            new("9990 [TCT001]", 9990, ContactType.Private, CodeplugConfidence.Inferred),
        };

        Dm1702NativeContactSerializer.Write(image, contacts);
        var result = Dm1702NativeImageReader.ReadFromNative(image);

        Assert.AreEqual(2, result.Contacts.Count);
        Assert.AreEqual("3129 [TGRP004]", result.Contacts[0].Name);
        Assert.AreEqual(3129, result.Contacts[0].CallId);
        Assert.AreEqual(ContactType.Group, result.Contacts[0].ContactType);
        Assert.AreEqual("9990 [TCT001]", result.Contacts[1].Name);
        Assert.AreEqual(9990, result.Contacts[1].CallId);
        Assert.AreEqual(ContactType.Private, result.Contacts[1].ContactType);
    }
}
