using Bao1702.Codeplug.Model;
using Bao1702.Protocol.Stock;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class Dm1702NativeImageBuilderTests
{
    [TestMethod]
    public void Build_EmptyCodeplug_ProducesExactSizeImage()
    {
        var bytes = Dm1702NativeImageBuilder.Build(CodeplugImage.CreateEmpty());
        Assert.AreEqual(Dm1702NativeImageAssumptions.ImageLength, bytes.Length);
        Assert.AreEqual(0, bytes[Dm1702NativeImageAssumptions.ChannelCountOffset]);
    }

    [TestMethod]
    public void Write_ReceiveOnlyChannel_EmitsTransmitInhibitBit()
    {
        var channel = new AnalogChannel(1, "TEST-CH-001", 145_000_000, 145_000_000, PowerLevel.Low,
            ChannelBandwidth.Narrow, AdmitCriteria.Always, [], ToneValue.None, ToneValue.None,
            CodeplugConfidence.Inferred) { ReceiveOnly = true };
        var bytes = Dm1702NativeImageBuilder.Build(CodeplugImage.CreateEmpty() with { Channels = [channel] });
        Assert.AreEqual(0x20, bytes[Dm1702NativeImageAssumptions.GetChannelRecordOffset(0) + 0x08] & 0x20);
    }


    [TestMethod]
    public void Build_WritesChannelNamesFrequenciesAndDigitalContactMap()
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, "TEST-CH-001", 162_400_000, 162_400_000, PowerLevel.Low, ChannelBandwidth.Wide, AdmitCriteria.Always, ["WX"], ToneValue.Parse(string.Empty), ToneValue.Parse(string.Empty), CodeplugConfidence.Inferred),
                new DigitalChannel(73, "TEST-CH-073", 443_400_000, 448_400_000, PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.Always, ["Imported"], 5, 1, "907 [TGRP001]", null, CodeplugConfidence.Inferred),
            ],
            Contacts =
            [
                new Contact("907 [TGRP001]", 907, ContactType.Group, CodeplugConfidence.Inferred)
            ]
        };

        var bytes = Dm1702NativeImageBuilder.Build(image);

        Assert.AreEqual(Dm1702NativeImageAssumptions.ImageLength, bytes.Length);
        Assert.AreEqual((byte)'T', bytes[Dm1702NativeImageAssumptions.ChannelNameTable1Start]);
        Assert.AreEqual((byte)'T', bytes[0x4318]);
        // OEM BCD LE: 162.4 MHz = 16240000 ? [0x00, 0x00, 0x24, 0x16]. First byte is LSB pair = 0x00.
        Assert.AreEqual(0x00, bytes[Dm1702NativeImageAssumptions.ChannelRecordStart]);
        Assert.AreEqual(0x24, bytes[Dm1702NativeImageAssumptions.ChannelRecordStart + 2]);
        Assert.AreEqual(0x16, bytes[Dm1702NativeImageAssumptions.ChannelRecordStart + 3]);
        // OEM BCD LE: 443.4 MHz = 44340000 ? [0x00, 0x00, 0x34, 0x44]. First byte = 0x00.
        Assert.AreEqual(0x00, bytes[0x3D90]);
        Assert.AreEqual(0x34, bytes[0x3D90 + 2]);
        Assert.AreEqual(0x44, bytes[0x3D90 + 3]);
        Assert.AreEqual(0x00, bytes[Dm1702NativeImageAssumptions.ChannelContactMapStart + (72 * 2)]);
        Assert.AreEqual(0x00, bytes[Dm1702NativeImageAssumptions.ChannelContactMapStart + (72 * 2) + 1]);
    }

    [TestMethod]
    [DataRow(162_400_000L, "TEST-CH-001 (VHF)")]
    [DataRow(443_400_000L, "TEST-CH-073 (UHF)")]
    [DataRow(448_400_000L, "TEST-CH-073 TX offset")]
    [DataRow(146_520_000L, "2m Simplex")]
    [DataRow(462_562_500L, "TEST-SERVICE 1")]
    [DataRow(400_000_000L, "400 MHz boundary")]
    [DataRow(108_000_000L, "108 MHz boundary")]
    public void FrequencyRoundTrip_BuildThenRead_PreservesExactValue(long frequencyHz, string label)
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, label, frequencyHz, frequencyHz, PowerLevel.Low, ChannelBandwidth.Wide, AdmitCriteria.Always, [], ToneValue.Parse(string.Empty), ToneValue.Parse(string.Empty), CodeplugConfidence.Inferred),
            ],
        };

        var bytes = Dm1702NativeImageBuilder.Build(image);
        var readBack = Dm1702NativeImageReader.ReadFromNative(bytes);

        Assert.AreEqual(1, readBack.Channels.Count, $"Expected exactly 1 channel for {label}");
        var channel = readBack.Channels[0];
        Assert.AreEqual(frequencyHz, channel.RxFrequencyHz, $"RX frequency mismatch for {label}");
        Assert.AreEqual(frequencyHz, channel.TxFrequencyHz, $"TX frequency mismatch for {label}");
    }

    [TestMethod]
    public void FrequencyRoundTrip_SplitRxTx_PreservesBothFrequencies()
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new DigitalChannel(73, "TEST-CH-073", 443_400_000, 448_400_000, PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.Always, [], 5, 1, null, null, CodeplugConfidence.Inferred),
            ],
        };

        var bytes = Dm1702NativeImageBuilder.Build(image);
        var readBack = Dm1702NativeImageReader.ReadFromNative(bytes);

        Assert.AreEqual(1, readBack.Channels.Count);
        var channel = readBack.Channels[0];
        Assert.AreEqual(443_400_000L, channel.RxFrequencyHz, "RX frequency mismatch");
        Assert.AreEqual(448_400_000L, channel.TxFrequencyHz, "TX frequency mismatch");
    }

    [TestMethod]
    public void ReadFromNative_RoundTrips_ContactsZonesRxGroupsAndScanLists()
    {
        var source = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, "TEST-CH-001", 162_400_000, 162_400_000, PowerLevel.Low, ChannelBandwidth.Wide, AdmitCriteria.Always, ["WX"], ToneValue.Parse(string.Empty), ToneValue.Parse(string.Empty), CodeplugConfidence.Inferred),
                new DigitalChannel(73, "TEST-CH-073", 443_400_000, 448_400_000, PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.ColorCodeFree, ["TGRP001 Zone"], 5, 1, "907 [TGRP001]", "[TGRP001]", CodeplugConfidence.Inferred),
            ],
            Contacts =
            [
                new Contact("907 [TGRP001]", 907, ContactType.Group, CodeplugConfidence.Inferred),
                new Contact("9990 [TCT001]", 9990, ContactType.Group, CodeplugConfidence.Inferred),
            ],
            Zones =
            [
                new Zone("WX", ["TEST-CH-001"], CodeplugConfidence.Inferred),
                new Zone("TGRP001 Zone", ["TEST-CH-073"], CodeplugConfidence.Inferred),
            ],
            RxGroups =
            [
                new RxGroup("[TGRP001]", ["907 [TGRP001]", "9990 [TCT001]"], CodeplugConfidence.Inferred),
            ],
            ScanLists =
            [
                new ScanList("TGRP1 Scan", ["TEST-CH-001", "TEST-CH-073"], CodeplugConfidence.Inferred),
            ],
        };

        var bytes = Dm1702NativeImageBuilder.Build(source);
        var result = Dm1702NativeImageReader.ReadFromNative(bytes);

        // Contacts
        Assert.AreEqual(2, result.Contacts.Count, "Contact count");
        // NormalizeContacts sorts alphabetically: "907 [TGRP001]" < "9990 [TCT001]"
        Assert.AreEqual("907 [TGRP001]", result.Contacts[0].Name, "Contact[0] name");
        Assert.AreEqual(907, result.Contacts[0].CallId, "Contact[0] callId");
        Assert.AreEqual(ContactType.Group, result.Contacts[0].ContactType, "Contact[0] type");
        Assert.AreEqual("9990 [TCT001]", result.Contacts[1].Name, "Contact[1] name");
        Assert.AreEqual(9990, result.Contacts[1].CallId, "Contact[1] callId");

        // Channels
        Assert.AreEqual(2, result.Channels.Count, "Channel count");
        var analog = result.Channels.OfType<AnalogChannel>().Single();
        Assert.AreEqual("TEST-CH-001", analog.Name, "Analog channel name");
        Assert.AreEqual(162_400_000L, analog.RxFrequencyHz, "Analog RX freq");
        var digital = result.Channels.OfType<DigitalChannel>().Single();
        Assert.AreEqual("TEST-CH-073", digital.Name, "Digital channel name");
        Assert.AreEqual(5, digital.ColorCode, "Digital color code");
        Assert.AreEqual(1, digital.TimeSlot, "Digital time slot");
        Assert.AreEqual("907 [TGRP001]", digital.ContactName, "Digital contact name (via contact map)");
        Assert.AreEqual(AdmitCriteria.ColorCodeFree, digital.AdmitCriteria, "Digital admit criteria");

        // Zones
        Assert.AreEqual(2, result.Zones.Count, "Zone count");
        var wxZone = result.Zones.Single(z => z.Name == "WX");
        CollectionAssert.AreEqual(new[] { "TEST-CH-001" }, wxZone.ChannelNames.ToArray(), "WX zone members");
        var jotaZone = result.Zones.Single(z => z.Name == "TGRP001 Zone");
        CollectionAssert.AreEqual(new[] { "TEST-CH-073" }, jotaZone.ChannelNames.ToArray(), "TGRP001 Zone members");

        // RxGroups
        Assert.AreEqual(1, result.RxGroups.Count, "RxGroup count");
        Assert.AreEqual("[TGRP001]", result.RxGroups[0].Name, "RxGroup name");
        CollectionAssert.AreEqual(new[] { "907 [TGRP001]", "9990 [TCT001]" }, result.RxGroups[0].ContactNames.ToArray(), "RxGroup members");

        // ScanLists
        Assert.AreEqual(1, result.ScanLists.Count, "ScanList count");
        Assert.AreEqual("TGRP1 Scan", result.ScanLists[0].Name, "ScanList name");
        Assert.AreEqual(2, result.ScanLists[0].ChannelNames.Count, "ScanList member count");
    }

    [TestMethod]
    public void ReadFromNative_RoundTrips_AnalogCtcssAndDcsTones()
    {
        var source = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, "CTCSS Ch", 146_520_000, 146_520_000, PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.ChannelFree, [], ToneValue.Parse("100.0"), ToneValue.Parse("67.0"), CodeplugConfidence.Inferred),
                new AnalogChannel(2, "DCS Ch", 462_562_500, 462_562_500, PowerLevel.Low, ChannelBandwidth.Wide, AdmitCriteria.Always, [], ToneValue.Parse("D023N"), ToneValue.Parse("D023N"), CodeplugConfidence.Inferred),
            ],
        };

        var bytes = Dm1702NativeImageBuilder.Build(source);
        var result = Dm1702NativeImageReader.ReadFromNative(bytes);

        Assert.AreEqual(2, result.Channels.Count, "Channel count");
        var ctcss = result.Channels.OfType<AnalogChannel>().Single(c => c.Name == "CTCSS Ch");
        Assert.AreEqual("100.0", ctcss.RxTone.ToString(), "CTCSS RX tone");
        Assert.AreEqual("67.0", ctcss.TxTone.ToString(), "CTCSS TX tone");
        Assert.AreEqual(AdmitCriteria.ChannelFree, ctcss.AdmitCriteria, "CTCSS admit criteria");

        var dcs = result.Channels.OfType<AnalogChannel>().Single(c => c.Name == "DCS Ch");
        Assert.AreEqual("D023N", dcs.RxTone.ToString(), "DCS RX tone");
        Assert.AreEqual("D023N", dcs.TxTone.ToString(), "DCS TX tone");
    }

    [TestMethod]
    public void ReadFromNative_RoundTrips_GeneralAndDisplaySettings()
    {
        var source = CodeplugImage.CreateEmpty() with
        {
            GeneralSettings = new GeneralSettings("W7TEST", "Hello", "World", CodeplugConfidence.Inferred),
            DisplaySettings = new DisplaySettings(BacklightDuration.TenSeconds, true, false, CodeplugConfidence.Inferred),
            StartupScreen = new StartupScreen("Hello", "World", CodeplugConfidence.Inferred),
            Channels =
            [
                new AnalogChannel(1, "Simplex", 146_520_000, 146_520_000, PowerLevel.Low, ChannelBandwidth.Wide, AdmitCriteria.Always, [], ToneValue.Parse(string.Empty), ToneValue.Parse(string.Empty), CodeplugConfidence.Inferred),
            ],
        };

        var bytes = Dm1702NativeImageBuilder.Build(source);
        var result = Dm1702NativeImageReader.ReadFromNative(bytes);

        Assert.AreEqual("W7TEST", result.GeneralSettings.RadioName, "Radio name");
        Assert.AreEqual("Hello", result.StartupScreen.Line1, "Startup line 1");
        Assert.AreEqual("World", result.StartupScreen.Line2, "Startup line 2");
        Assert.AreEqual(BacklightDuration.TenSeconds, result.DisplaySettings.BacklightDuration, "Backlight duration");
        Assert.IsTrue(result.DisplaySettings.ShowChannelNumber, "Show channel number");
    }

    [TestMethod]
    public void ReadFromNative_RoundTrips_RadioIdentitySquelchPowerAndLoneWorker()
    {
        // batterySaver at +0x0D b2-b3, loneWorker at 0x8B00 (enable), 0x8B01 (minutes), 0x8B02 (seconds).
        var source = CodeplugImage.CreateEmpty() with
        {
            RadioIdentity = new RadioIdentitySettings("1000001", "N0CALL", CodeplugConfidence.Inferred),
            SquelchSettings = new SquelchSettings(12, 3, CodeplugConfidence.Inferred),
            PowerSettings = new PowerSettings(PowerLevel.Medium, true, CodeplugConfidence.Inferred),
            LoneWorkerConfig = new LoneWorkerConfig(true, 5, 30, CodeplugConfidence.Inferred),
            Channels =
            [
                new AnalogChannel(1, "Simplex", 146_520_000, 146_520_000, PowerLevel.Low, ChannelBandwidth.Wide, AdmitCriteria.Always, [], ToneValue.Parse(string.Empty), ToneValue.Parse(string.Empty), CodeplugConfidence.Inferred),
            ],
        };

        var bytes = Dm1702NativeImageBuilder.Build(source);
        var result = Dm1702NativeImageReader.ReadFromNative(bytes);

        Assert.AreEqual("1000001", result.RadioIdentity.DmrId, "DMR ID");
        Assert.AreEqual(12, result.SquelchSettings.AnalogLevel, "Analog squelch level");
        Assert.AreEqual(3, result.SquelchSettings.DigitalLevel, "Digital squelch level");
        Assert.AreEqual(PowerLevel.Medium, result.PowerSettings.DefaultPower, "Default power level");
        Assert.IsTrue(result.PowerSettings.BatterySaverEnabled, "Battery saver");
        Assert.IsTrue(result.LoneWorkerConfig.Enabled, "Lone worker enabled");
        Assert.AreEqual(5, result.LoneWorkerConfig.ResponseTimeMinutes, "Lone worker response minutes");
        Assert.AreEqual(30, result.LoneWorkerConfig.ReminderTimeSeconds, "Lone worker reminder seconds");
    }

    [TestMethod]
    public void ReadFromNative_RoundTrips_DtmfConfigAndKeyAssignments()
    {
        // ReviveCode at +0x1A4 (8B). Key assignment table at config+0x150 (14 bytes).
        byte[] customKeys = [0x05, 0x1E, 0x07, 0x0A, 0x05, 0x00, 0x04, 0x13, 0x0F, 0x23, 0x14, 0x28, 0x15, 0x07];
        var source = CodeplugImage.CreateEmpty() with
        {
            DtmfConfig = new DtmfConfig("1234", "KILL01", "REVV01", CodeplugConfidence.Inferred),
            KeyAssignments = new KeyAssignmentTable(customKeys, CodeplugConfidence.Inferred),
            Channels =
            [
                new AnalogChannel(1, "Simplex", 146_520_000, 146_520_000, PowerLevel.Low, ChannelBandwidth.Wide, AdmitCriteria.Always, [], ToneValue.Parse(string.Empty), ToneValue.Parse(string.Empty), CodeplugConfidence.Inferred),
            ],
        };

        var bytes = Dm1702NativeImageBuilder.Build(source);
        var result = Dm1702NativeImageReader.ReadFromNative(bytes);

        Assert.AreEqual("1234", result.DtmfConfig.PttId, "DTMF PttId");
        Assert.AreEqual("KILL01", result.DtmfConfig.KillCode, "DTMF KillCode");
        Assert.AreEqual("REVV01", result.DtmfConfig.ReviveCode, "DTMF ReviveCode");
        CollectionAssert.AreEqual(customKeys, result.KeyAssignments.Assignments, "Key assignment table");
    }

    [TestMethod]
    public void ReadFromNative_RoundTrips_PrivacyEntriesEmergencySystemsAndQuickText()
    {
        // quick text at 0xA010 stride 0x81. All confirmed against 7th capture (has all three populated).
        byte[] keyData = [0x12, 0x34, 0x56, 0x78, 0xFF, 0xFF, 0xFF, 0xFF];
        byte[] footer = [0x00, 0x00, 0x00, 0x00];
        var source = CodeplugImage.CreateEmpty() with
        {
            PrivacyEntries =
            [
                new PrivacyEntry("Privacy 1", 0x01, keyData, footer, CodeplugConfidence.Inferred),
                new PrivacyEntry("Privacy 2", 0x02, keyData, footer, CodeplugConfidence.Inferred),
            ],
            EmergencySystems =
            [
                new EmergencySystem("Sys 1", 0x01, 0x00, 0x00, 0x00, 0x03, 0x03, 0x01, 0x00, 0x0A, 0x06, CodeplugConfidence.Inferred),
            ],
            QuickTextMessages =
            [
                new QuickTextMessage("Call QRZ", CodeplugConfidence.Inferred),
                new QuickTextMessage("On my way", CodeplugConfidence.Inferred),
            ],
            Channels =
            [
                new AnalogChannel(1, "Simplex", 146_520_000, 146_520_000, PowerLevel.Low, ChannelBandwidth.Wide, AdmitCriteria.Always, [], ToneValue.Parse(string.Empty), ToneValue.Parse(string.Empty), CodeplugConfidence.Inferred),
            ],
        };

        var bytes = Dm1702NativeImageBuilder.Build(source);
        var result = Dm1702NativeImageReader.ReadFromNative(bytes);

        // Privacy entries
        Assert.AreEqual(2, result.PrivacyEntries.Count, "Privacy entry count");
        Assert.AreEqual("Privacy 1", result.PrivacyEntries[0].Name, "Privacy[0] name");
        Assert.AreEqual(0x01, result.PrivacyEntries[0].KeyType, "Privacy[0] key type");
        CollectionAssert.AreEqual(keyData, result.PrivacyEntries[0].KeyData, "Privacy[0] key data");
        Assert.AreEqual("Privacy 2", result.PrivacyEntries[1].Name, "Privacy[1] name");
        Assert.AreEqual(0x02, result.PrivacyEntries[1].KeyType, "Privacy[1] key type");

        // Emergency systems
        Assert.AreEqual(1, result.EmergencySystems.Count, "Emergency system count");
        Assert.AreEqual("Sys 1", result.EmergencySystems[0].Name, "Emergency[0] name");
        Assert.AreEqual(0x01, result.EmergencySystems[0].EmergencyType, "Emergency[0] type");
        Assert.AreEqual(0x03, result.EmergencySystems[0].ImpoliteRetries, "Emergency[0] impolite retries");
        Assert.AreEqual(0x0A, result.EmergencySystems[0].HotMicDuration, "Emergency[0] hot mic duration");

        // Quick text messages
        Assert.AreEqual(2, result.QuickTextMessages.Count, "Quick text count");
        Assert.AreEqual("Call QRZ", result.QuickTextMessages[0].Text, "QuickText[0]");
        Assert.AreEqual("On my way", result.QuickTextMessages[1].Text, "QuickText[1]");
    }

    [TestMethod]
    public void ReadFromNative_RoundTrips_ExtendedChannelSemantics()
    {
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
            DisplayPttId: true,
            GpsSystemIndex: null,
            ScanListIndex: 1,
            EmergencySystemIndex: null,
            PttKeyupMode: null,
            PttKeyupEncodeType: null,
            Confidence: CodeplugConfidence.Inferred);

        var rxGroup = new RxGroup("[TGRP001]", ["907 [TGRP001]"], CodeplugConfidence.Inferred);
        var scanList = new ScanList("TGRP001", ["TEST-CH-073"], CodeplugConfidence.Inferred);
        var contact = new Contact("907 [TGRP001]", 907, ContactType.Group, CodeplugConfidence.Inferred);

        var source = CodeplugImage.CreateEmpty() with
        {
            Contacts = [contact],
            RxGroups = [rxGroup],
            ScanLists = [scanList],
            Channels =
            [
                new DigitalChannel(73, "TEST-CH-073", 443_400_000, 448_400_000,
                    PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.ColorCodeFree,
                    ["Imported"], 5, 2, "907 [TGRP001]", "[TGRP001]",
                    CodeplugConfidence.Inferred, semantics),
            ],
        };

        var bytes = Dm1702NativeImageBuilder.Build(source);
        var result = Dm1702NativeImageReader.ReadFromNative(bytes);

        Assert.AreEqual(1, result.Channels.Count, "Channel count");
        var ch = result.Channels[0] as DigitalChannel;
        Assert.IsNotNull(ch, "Channel is DigitalChannel");
        Assert.IsNotNull(ch.NativeSemantics, "NativeSemantics present");

        var s = ch.NativeSemantics!;
        Assert.IsTrue(s.RxOnly, "RxOnly");
        Assert.IsTrue(s.VoxEnabled, "VoxEnabled");
        Assert.IsTrue(s.TalkAroundEnabled, "TalkAroundEnabled");
        Assert.IsTrue(s.LoneWorkerEnabled, "LoneWorkerEnabled");
        Assert.IsTrue(s.AutoScanEnabled, "AutoScanEnabled");
        Assert.IsTrue(s.EmergencyAlarmIndication, "EmergencyAlarmIndication");
        Assert.IsTrue(s.EmergencyAlarmAck, "EmergencyAlarmAck");
        Assert.IsTrue(s.EmergencyCallIndication, "EmergencyCallIndication");
        Assert.IsTrue(s.PrivateCallConfirmed, "PrivateCallConfirmed");
        Assert.IsFalse(s.ShortDataMessage, "ShortDataMessage");
        Assert.IsTrue(s.EncryptionEnabled, "EncryptionEnabled");
        Assert.AreEqual(3, s.EncryptionKeyIndex, "EncryptionKeyIndex");
        Assert.IsTrue(s.DoubleCapacityMode, "DoubleCapacityMode");
        Assert.IsTrue(s.TalkAroundStatus, "TalkAroundStatus");
        Assert.IsTrue(s.DisplayPttId, "DisplayPttId");
        Assert.AreEqual(1, s.ScanListIndex, "ScanListIndex");
        Assert.AreEqual("[TGRP001]", ch.RxGroupName, "RxGroupName");

        // Analog channel: semantics also reconstructed
        var analogSemantics = new Dm1702NativeSemantics(
            RxOnly: true, VoxEnabled: false, TalkAroundEnabled: false, LoneWorkerEnabled: false,
            AutoScanEnabled: false, EmergencyAlarmIndication: false, EmergencyAlarmAck: false,
            EmergencyCallIndication: false, PrivateCallConfirmed: false, ShortDataMessage: false,
            EncryptionEnabled: false, EncryptionKeyIndex: 0, DoubleCapacityMode: false,
            TalkAroundStatus: false, DisplayPttId: false,
            GpsSystemIndex: null, ScanListIndex: null, EmergencySystemIndex: null,
            PttKeyupMode: null, PttKeyupEncodeType: null,
            Confidence: CodeplugConfidence.Inferred);

        var analogSource = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, "WX1", 162_400_000, 162_400_000,
                    PowerLevel.Low, ChannelBandwidth.Narrow, AdmitCriteria.Always, [],
                    ToneValue.Parse(string.Empty), ToneValue.Parse(string.Empty),
                    CodeplugConfidence.Inferred, analogSemantics),
            ],
        };

        var analogBytes = Dm1702NativeImageBuilder.Build(analogSource);
        var analogResult = Dm1702NativeImageReader.ReadFromNative(analogBytes);
        var ach = analogResult.Channels[0] as AnalogChannel;
        Assert.IsNotNull(ach, "AnalogChannel present");
        Assert.IsNotNull(ach.NativeSemantics, "Analog NativeSemantics present");
        Assert.IsTrue(ach.NativeSemantics!.RxOnly, "Analog RxOnly");
    }

    [TestMethod]
    public void ChannelNames_SurvivePackedWriteReadRoundTrip()
    {
        // Simulate the full wire write ? read pipeline:
        // Build ? ExtractPackedWriteImage ? (simulated radio) ? BuildNativeImage ? ReadFromNative
        var source = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, "WX 1", 162_400_000, 162_400_000, PowerLevel.Low, ChannelBandwidth.Wide, AdmitCriteria.Always, [], ToneValue.Parse(string.Empty), ToneValue.Parse(string.Empty), CodeplugConfidence.Inferred),
                new DigitalChannel(73, "TEST-CH-073", 443_400_000, 448_400_000, PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.Always, [], 5, 1, null, null, CodeplugConfidence.Inferred),
            ],
        };

        // Step 1: Build native image (what the Desktop does before writing)
        var nativeImage = Dm1702NativeImageBuilder.Build(source);

        // Verify names are in the native image at expected offsets
        Assert.AreEqual((byte)'W', nativeImage[0x4000], "Name[0] first byte in native image");
        Assert.AreEqual((byte)'T', nativeImage[0x4318], "Name[72] first byte in native image");

        // Step 2: Extract packed write image (what WriteCodeplugAsync does)
        var packedWrite = StockCpsSessionBootstrap.ExtractPackedWriteImage(nativeImage);

        // Step 3: Simulate the radio storing and returning data.
        // The write uses ObservedWriteSegments, the read uses ObservedReadSegments.
        // We map: for each write segment, find the matching read segment by wire address,
        // and place the written data at the corresponding packed-read offset.
        var packedRead = new byte[StockCpsSessionBootstrap.ObservedReadImageSize];
        var writeSegments = StockCpsSessionBootstrap.ObservedWriteSegments;
        var readSegments = StockCpsSessionBootstrap.ObservedReadSegments;

        // Build a lookup of read segment packed offsets by wire start address
        var readOffsetByWireStart = new Dictionary<int, int>();
        var readPackedOffset = 0;
        foreach (var (startAddr, blockCount) in readSegments)
        {
            readOffsetByWireStart[startAddr] = readPackedOffset;
            readPackedOffset += blockCount * StockCpsSessionBootstrap.ObservedCodeplugPageSize;
        }

        // Copy write packed data to read packed data, matching by wire start address
        var writePackedOffset = 0;
        foreach (var (startAddr, blockCount) in writeSegments)
        {
            var segmentBytes = blockCount * StockCpsSessionBootstrap.ObservedCodeplugPageSize;
            if (readOffsetByWireStart.TryGetValue(startAddr, out var readOff))
            {
                Array.Copy(packedWrite, writePackedOffset, packedRead, readOff, segmentBytes);
            }

            writePackedOffset += segmentBytes;
        }

        // Step 4: Build native image from packed read (what ReadCodeplugAsync returns)
        var readNative = StockCpsSessionBootstrap.BuildNativeImage(packedRead);

        // Verify names survived the full pipeline
        Assert.AreEqual((byte)'W', readNative[0x4000], "Name[0] first byte after wire round-trip");
        Assert.AreEqual((byte)'T', readNative[0x4318], "Name[72] first byte after wire round-trip");

        // Step 5: Parse the native image (what the Desktop does after reading)
        var result = Dm1702NativeImageReader.ReadFromNative(readNative);

        Assert.AreEqual(2, result.Channels.Count, "Channel count after round-trip");
        Assert.AreEqual("WX 1", result.Channels[0].Name, "Channel[0] name after wire round-trip");
        Assert.AreEqual("TEST-CH-073", result.Channels[1].Name, "Channel[1] name after wire round-trip");
    }
}
