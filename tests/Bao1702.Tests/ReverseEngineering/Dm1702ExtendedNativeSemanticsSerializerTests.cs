using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class Dm1702ExtendedNativeSemanticsSerializerTests
{
    [TestMethod]
    public void Write_EncodesExtendedDigitalNativeSemantics()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var contacts = new[] { new Contact("907 [TGRP001]", 907, ContactType.Group, CodeplugConfidence.Inferred) };
        var rxGroups = new[] { new RxGroup("[TGRP001]", ["907 [TGRP001]"], CodeplugConfidence.Inferred) };
        var scanLists = new[] { new ScanList("TGRP001", ["TEST-CH-073"], CodeplugConfidence.Inferred) };
        var semantics = new Dm1702NativeSemantics(
            RxOnly: true,
            VoxEnabled: true,
            TalkAroundEnabled: true,
            LoneWorkerEnabled: true,
            AutoScanEnabled: true,
            EmergencyAlarmIndication: true,
            EmergencyAlarmAck: true,
            EmergencyCallIndication: true,
            PrivateCallConfirmed: true,
            ShortDataMessage: false,
            EncryptionEnabled: true,
            EncryptionKeyIndex: 3,
            DoubleCapacityMode: true,
            TalkAroundStatus: true,
            DisplayPttId: false,
            GpsSystemIndex: 1,
            ScanListIndex: 1,
            EmergencySystemIndex: 1,
            PttKeyupMode: null,
            PttKeyupEncodeType: null,
            Confidence: CodeplugConfidence.Inferred);

        var channel = new DigitalChannel(
            73,
            "TEST-CH-073",
            443_400_000,
            448_400_000,
            PowerLevel.High,
            ChannelBandwidth.Narrow,
            AdmitCriteria.ColorCodeFree,
            ["Imported"],
            5,
            2,
            "907 [TGRP001]",
            "[TGRP001]",
            CodeplugConfidence.Inferred,
            semantics);

        Dm1702NativeChannelRecordSerializer.Write(image, channel, 72, contacts, rxGroups, scanLists, "APRS-GPS");

        // +0x08: mode/flags = Digital(0x40)|RxOnly(0x20)|LoneWorker(0x08)|HighPower(0x02) = 0x6A
        Assert.AreEqual(0x6A, image[0x3D90 + 0x08]);
        // +0x09: secondary flags = Encrypt(0x80)|DoubleCapacity(0x01) = 0x81
        Assert.AreEqual(0x81, image[0x3D90 + 0x09]);
        // +0x0A: always zero
        Assert.AreEqual(0x00, image[0x3D90 + 0x0A]);
        // +0x0B: AutoScan(0x80)|EmergAlarmInd(0x20)|EmergCallInd(0x01) = 0xA1
        Assert.AreEqual(0xA1, image[0x3D90 + 0x0B]);
        // +0x0C: scan list index (1-based ScanListIndex=1, stored 0-based = 0)
        Assert.AreEqual(0x00, image[0x3D90 + 0x0C]);
        // +0x0D: ColorCodeFree(0x10) + EmergAlarmAck(0x20) = 0x30
        Assert.AreEqual(0x30, image[0x3D90 + 0x0D]);
        // +0x0E: TS2(0x10)|CC5(0x05)|EmergCallInd(0x20) = 0x35
        Assert.AreEqual(0x35, image[0x3D90 + 0x0E]);
        // +0x0F: PrivCallConfirm(0x80) = 0x80
        Assert.AreEqual(0x80, image[0x3D90 + 0x0F]);
        // +0x10: encrypt key index 3 (0-based)
        Assert.AreEqual(0x03, image[0x3D90 + 0x10]);
        // +0x11: RxGroup index 1 (1-based)
        Assert.AreEqual(0x01, image[0x3D90 + 0x11]);
        // +0x17: TalkAround checkbox (0x10)
        Assert.AreEqual(0x10, image[0x3D90 + 0x17]);
        // +0x1A: TalkAroundStatus(0x40)|VOX(0x10) = 0x50
        Assert.AreEqual(0x50, image[0x3D90 + 0x1A]);
        Assert.AreEqual(0x0A, image[0x3D90 + 0x20]);
        Assert.AreEqual(0x0A, image[0x3D90 + 0x21]);
        Assert.AreEqual(0x51, image[0x3D90 + 0x22]);
    }

    [TestMethod]
    public void Write_EncodesPttKeyupProvisionally()
    {
        // No PTT key-up encoding was ever toggled in any OEM capture.
        // PttKeyupMode/PttKeyupEncodeType on the model are retained but do not affect the native image.
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var contacts = Array.Empty<Contact>();
        var rxGroups = Array.Empty<RxGroup>();
        var scanLists = Array.Empty<ScanList>();
        var semantics = new Dm1702NativeSemantics(
            RxOnly: false,
            VoxEnabled: false,
            TalkAroundEnabled: false,
            LoneWorkerEnabled: false,
            AutoScanEnabled: false,
            EmergencyAlarmIndication: false,
            EmergencyAlarmAck: false,
            EmergencyCallIndication: false,
            PrivateCallConfirmed: false,
            ShortDataMessage: false,
            EncryptionEnabled: false,
            EncryptionKeyIndex: 0,
            DoubleCapacityMode: false,
            TalkAroundStatus: false,
            DisplayPttId: false,
            GpsSystemIndex: null,
            ScanListIndex: null,
            EmergencySystemIndex: null,
            PttKeyupMode: "DTMF",
            PttKeyupEncodeType: "Pre",
            Confidence: CodeplugConfidence.Inferred);

        var channel = new DigitalChannel(
            1, "PTT Test", 443_000_000, 448_000_000, PowerLevel.High, ChannelBandwidth.Narrow,
            AdmitCriteria.Always, ["Test"], 1, 1, null, null, CodeplugConfidence.Inferred, semantics);

        Dm1702NativeChannelRecordSerializer.Write(image, channel, 0, contacts, rxGroups, scanLists, null);

        // +0x1B always 0x00 — model fields are ignored because no OEM encoding was ever observed
        Assert.AreEqual(0x00, image[Dm1702NativeImageAssumptions.ChannelRecordStart + 0x1B]);
    }

    [TestMethod]
    public void Write_EncodesDcsToneValues_OemValidated()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var channel = new AnalogChannel(
            1,
            "DCS Test",
            145_125_000,
            145_725_000,
            PowerLevel.Low,
            ChannelBandwidth.Narrow,
            AdmitCriteria.Always,
            ["Test"],
            ToneValue.Parse("D023N"),
            ToneValue.Parse("D754I"),
            CodeplugConfidence.Inferred,
            new Dm1702NativeSemantics(false, false, false, false, false, false, false, false, false, false, false, 0, false, false, false, null, null, null, null, null, CodeplugConfidence.Inferred));

        Dm1702NativeChannelRecordSerializer.Write(image, channel, 0, [], [], [], null);

        Assert.AreEqual(0x23, image[Dm1702NativeImageAssumptions.ChannelRecordStart + 0x13]);
        Assert.AreEqual(0x80, image[Dm1702NativeImageAssumptions.ChannelRecordStart + 0x14]);
        // Formula-derived: D754I ? Inverted: 0xC0 | (754 & 0x0F) = 0xC0 | 2 = 0xC2
        // Low byte: BCD(digits[1..2]) = BCD("54") = 0x54
        Assert.AreEqual(0x54, image[Dm1702NativeImageAssumptions.ChannelRecordStart + 0x15]);
        Assert.AreEqual(0xC2, image[Dm1702NativeImageAssumptions.ChannelRecordStart + 0x16]);
    }
}
