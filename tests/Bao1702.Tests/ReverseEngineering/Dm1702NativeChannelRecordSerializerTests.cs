using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class Dm1702NativeChannelRecordSerializerTests
{
    [TestMethod]
    public void Write_EncodesAnalogToneAndPowerFields()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var channel = new AnalogChannel(
            1,
            "GB3CG",
            145_125_000,
            145_725_000,
            PowerLevel.High,
            ChannelBandwidth.Narrow,
            AdmitCriteria.Always,
            ["Repeaters"],
            ToneValue.Parse("118.8"),
            ToneValue.Parse("118.8"),
            CodeplugConfidence.Inferred);

        Dm1702NativeChannelRecordSerializer.Write(image, channel, 0, [], [], [], null);

        Assert.AreEqual(0x02, image[Dm1702NativeImageAssumptions.ChannelRecordStart + 0x08]);
        Assert.AreEqual(0x88, image[Dm1702NativeImageAssumptions.ChannelRecordStart + 0x13]);
        Assert.AreEqual(0x11, image[Dm1702NativeImageAssumptions.ChannelRecordStart + 0x14]);
    }

    [TestMethod]
    public void Write_EncodesDigitalSlotColorCodeAndRxGroupReference()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var contacts = new[] { new Contact("907 [TGRP001]", 907, ContactType.Group, CodeplugConfidence.Inferred) };
        var rxGroups = new[] { new RxGroup("[TGRP001]", ["907 [TGRP001]"], CodeplugConfidence.Inferred) };
        var scanLists = new[] { new ScanList("TGRP001", ["TEST-CH-073"], CodeplugConfidence.Inferred) };
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
            CodeplugConfidence.Inferred);

        Dm1702NativeChannelRecordSerializer.Write(image, channel, 72, contacts, rxGroups, scanLists, "APRS-GPS");
        Dm1702NativeChannelRecordSerializer.WriteChannelContactMap(image, 72, contacts, channel.ContactName);

        Assert.AreEqual(0x42, image[0x3D90 + 0x08]);
        Assert.AreEqual(0x10, image[0x3D90 + 0x0D]);
        Assert.AreEqual(0x15, image[0x3D90 + 0x0E]);
        Assert.AreEqual(0x01, image[0x3D90 + 0x11]);
        Assert.AreEqual(0x00, image[Dm1702NativeImageAssumptions.ChannelContactMapStart + (72 * 2) + 1]);
    }

    [TestMethod]
    public void Write_EncodesDcsTones_OemValidated()
    {
        // OEM capture: baseline+ctcss_cdcss_decode_encode.data
        // Ch1: Analog, 400MHz, RX Decode=D023N, TX Encode=D023I
        // Diff vs baseline: +0x13=0x23, +0x14=0x80, +0x15=0x23, +0x16=0xC7
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var channel = new AnalogChannel(
            1,
            "Channel 1",
            400_000_000,
            400_000_000,
            PowerLevel.Low,
            ChannelBandwidth.Wide,
            AdmitCriteria.Always,
            [],
            ToneValue.Parse("D023N"),
            ToneValue.Parse("D023I"),
            CodeplugConfidence.Inferred);

        Dm1702NativeChannelRecordSerializer.Write(image, channel, 0, [], [], [], null);

        var rec = Dm1702NativeImageAssumptions.ChannelRecordStart;
        Assert.AreEqual(0x23, image[rec + 0x13]);
        Assert.AreEqual(0x80, image[rec + 0x14]);
        Assert.AreEqual(0x23, image[rec + 0x15]);
        Assert.AreEqual(0xC7, image[rec + 0x16]);
    }

    [TestMethod]
    public void Write_EncodesDisplayPttId_OemValidated()
    {
        // OEM evidence: baseline+ch2_digital+system+privacy+pttid_changes.data
        // Ch1 byte +0x18: 0x00?0x80 when Display PTT-ID enabled.
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
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
            DisplayPttId: true,
            GpsSystemIndex: null,
            ScanListIndex: null,
            EmergencySystemIndex: null,
            PttKeyupMode: null,
            PttKeyupEncodeType: null,
            Confidence: CodeplugConfidence.Inferred);

        var channel = new AnalogChannel(
            1,
            "Channel 1",
            400_000_000,
            400_000_000,
            PowerLevel.Low,
            ChannelBandwidth.Wide,
            AdmitCriteria.Always,
            [],
            ToneValue.None,
            ToneValue.None,
            CodeplugConfidence.Inferred,
            semantics);

        Dm1702NativeChannelRecordSerializer.Write(image, channel, 0, [], [], [], null);

        var rec = Dm1702NativeImageAssumptions.ChannelRecordStart;
        Assert.AreEqual(0x80, image[rec + 0x18]);
    }

    [TestMethod]
    public void Write_DisplayPttIdDisabled_ByteIsZero()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var channel = new AnalogChannel(
            1,
            "Channel 1",
            400_000_000,
            400_000_000,
            PowerLevel.Low,
            ChannelBandwidth.Wide,
            AdmitCriteria.Always,
            [],
            ToneValue.None,
            ToneValue.None,
            CodeplugConfidence.Inferred);

        Dm1702NativeChannelRecordSerializer.Write(image, channel, 0, [], [], [], null);

        var rec = Dm1702NativeImageAssumptions.ChannelRecordStart;
        // No NativeSemantics ? Display PTT-ID defaults off ? byte +0x18 = 0x00
        Assert.AreEqual(0x00, image[rec + 0x18]);
    }
}
