using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class Dm1702NativeSystemSectionSerializerTests
{
    [TestMethod]
    public void Write_SerializesPrivacyEntriesAtOemValidatedOffsets()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            PrivacyEntries =
            [
                new PrivacyEntry("Privacy 1", 0x01, [0x12, 0x34, 0x56, 0x78, 0x90, 0xFF, 0xFF, 0xFF],
                    [0x00, 0x00, 0x00, 0x01], CodeplugConfidence.Confirmed),
                new PrivacyEntry("Privacy 2", 0x01, [0x87, 0x65, 0x43, 0x21, 0xFF, 0xFF, 0xFF, 0xFF],
                    [0x00, 0x00, 0x00, 0x03], CodeplugConfidence.Confirmed),
            ],
        };

        Dm1702NativeSystemSectionSerializer.Write(image, model);

        // Header byte at 0x8800 = count
        Assert.AreEqual((byte)2, image[0x8800]);

        // Entry 1 at 0x8801: name "Privacy 1"
        Assert.AreEqual((byte)'P', image[0x8801]);
        Assert.AreEqual((byte)'1', image[0x8809]);
        // Entry 1: KeyType at +0x0A
        Assert.AreEqual((byte)0x01, image[0x8801 + 0x0A]);
        // Entry 1: KeyData at +0x0B
        Assert.AreEqual((byte)0x12, image[0x8801 + 0x0B]);
        Assert.AreEqual((byte)0x34, image[0x8801 + 0x0C]);
        Assert.AreEqual((byte)0x90, image[0x8801 + 0x0F]);
        Assert.AreEqual((byte)0xFF, image[0x8801 + 0x10]);
        // Entry 1: Footer at +0x13
        Assert.AreEqual((byte)0x01, image[0x8801 + 0x16]);

        // Entry 2 at 0x8818: name "Privacy 2"
        Assert.AreEqual((byte)'P', image[0x8818]);
        Assert.AreEqual((byte)'2', image[0x8818 + 0x08]);
        // Entry 2: KeyType
        Assert.AreEqual((byte)0x01, image[0x8818 + 0x0A]);
        // Entry 2: KeyData
        Assert.AreEqual((byte)0x87, image[0x8818 + 0x0B]);
        // Entry 2: Footer last byte
        Assert.AreEqual((byte)0x03, image[0x8818 + 0x16]);
    }

    [TestMethod]
    public void Write_SerializesEmergencyEntriesAtOemValidatedOffsets()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            EmergencySystems =
            [
                new EmergencySystem("Sys 1", 0x00, 0x01, 0x00, 0x00, 0x0F, 0x05, 0x01, 0x00, 0x01, 0x01,
                    CodeplugConfidence.Confirmed),
                new EmergencySystem("Emerg A", 0x04, 0x01, 0x00, 0x00, 0x0F, 0x05, 0x01, 0x00, 0x01, 0x01,
                    CodeplugConfidence.Confirmed),
            ],
        };

        Dm1702NativeSystemSectionSerializer.Write(image, model);

        // Count at 0x86F0
        Assert.AreEqual((byte)2, image[0x86F0]);

        // Entry 1 at 0x8620: "Sys 1"
        Assert.AreEqual((byte)'S', image[0x8620]);
        Assert.AreEqual((byte)'1', image[0x8624]);
        // Entry 1: EmergencyType=0x00, Mode=0x01, ImpoliteRetries=0x0F, PoliteRetries=0x05
        Assert.AreEqual((byte)0x00, image[0x8620 + 0x0A]);
        Assert.AreEqual((byte)0x01, image[0x8620 + 0x0B]);
        Assert.AreEqual((byte)0x0F, image[0x8620 + 0x0E]);
        Assert.AreEqual((byte)0x05, image[0x8620 + 0x0F]);

        // Entry 2 at 0x8634: "Emerg A"
        Assert.AreEqual((byte)'E', image[0x8634]);
        Assert.AreEqual((byte)'A', image[0x863A]);
        // Entry 2: EmergencyType=0x04 (Silent w/ Voice)
        Assert.AreEqual((byte)0x04, image[0x8634 + 0x0A]);
    }

    [TestMethod]
    public void Write_SerializesLoneWorkerAtOemValidatedOffset()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            LoneWorkerConfig = new LoneWorkerConfig(true, 10, 10, CodeplugConfidence.Confirmed),
        };

        Dm1702NativeSystemSectionSerializer.Write(image, model);

        // 0x8B00 = enable, 0x8B01 = response minutes, 0x8B02 = reminder seconds
        Assert.AreEqual((byte)0x01, image[0x8B00]);
        Assert.AreEqual((byte)0x0A, image[0x8B01]);
        Assert.AreEqual((byte)0x0A, image[0x8B02]);
    }

    [TestMethod]
    public void Write_SerializesLoneWorkerDisabled()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            LoneWorkerConfig = new LoneWorkerConfig(false, 1, 10, CodeplugConfidence.Confirmed),
        };

        Dm1702NativeSystemSectionSerializer.Write(image, model);

        Assert.AreEqual((byte)0x00, image[0x8B00]);
        Assert.AreEqual((byte)0x01, image[0x8B01]);
        Assert.AreEqual((byte)0x0A, image[0x8B02]);
    }

    [TestMethod]
    public void Write_SerializesQuickTextMessagesAtOemValidatedOffsets()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            QuickTextMessages =
            [
                new QuickTextMessage("Message 1", CodeplugConfidence.Confirmed),
                new QuickTextMessage("Message 2", CodeplugConfidence.Confirmed),
                new QuickTextMessage("Message 3", CodeplugConfidence.Confirmed),
            ],
        };

        Dm1702NativeSystemSectionSerializer.Write(image, model);

        // Count at 0xA000
        Assert.AreEqual((byte)3, image[0xA000]);

        // Message 1 at 0xA010: length=9, text="Message 1"
        Assert.AreEqual((byte)9, image[0xA010]);
        Assert.AreEqual((byte)'M', image[0xA011]);
        Assert.AreEqual((byte)'1', image[0xA019]);

        // Message 2 at 0xA091: length=9, text="Message 2"
        Assert.AreEqual((byte)9, image[0xA091]);
        Assert.AreEqual((byte)'M', image[0xA092]);
        Assert.AreEqual((byte)'2', image[0xA09A]);

        // Message 3 at 0xA112: length=9, text="Message 3"
        Assert.AreEqual((byte)9, image[0xA112]);
        Assert.AreEqual((byte)'M', image[0xA113]);
        Assert.AreEqual((byte)'3', image[0xA11B]);
    }

    [TestMethod]
    public void Write_DoesNotWriteOneTouchCallTable()
    {
        // NOT button labels. WriteButtonSection was removed. Verify the area stays untouched.
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            ButtonConfig = new ButtonConfig("Mon", "Scan", "Pwr", "Zone", CodeplugConfidence.Inferred),
        };

        Dm1702NativeSystemSectionSerializer.Write(image, model);

        Assert.AreEqual((byte)0, image[Dm1702NativeImageAssumptions.ChannelContactMapStart + 0x1A00],
            "One-touch call table at 0x1FA00 should not be written by system section serializer");
    }
}
