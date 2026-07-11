using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class Dm1702NativeConfigAndGpsSerializerTests
{
    [TestMethod]
    public void Write_SerializesConfigAndGpsSections()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            GeneralSettings = new GeneralSettings("DM1702", "HELLO", "WORLD", CodeplugConfidence.Inferred),
            DisplaySettings = new DisplaySettings(BacklightDuration.TenSeconds, true, true, CodeplugConfidence.Inferred),
            PowerSettings = new PowerSettings(PowerLevel.High, true, CodeplugConfidence.Inferred),
            SquelchSettings = new SquelchSettings(5, 6, CodeplugConfidence.Inferred),
            RadioIdentity = new RadioIdentitySettings("1234567", "APRS-GPS", CodeplugConfidence.Inferred),
        };

        Dm1702NativeConfigSerializer.Write(image, model);
        Dm1702NativeGpsSerializer.Write(image, model);

        Assert.AreEqual((byte)0x01, image[Dm1702NativeImageAssumptions.ConfigStart]);
        Assert.AreEqual((byte)0x48, image[Dm1702NativeImageAssumptions.ConfigStart + 0x0B]);
        Assert.AreEqual((byte)'A', image[Dm1702NativeImageAssumptions.GpsSystemsStart]);
        Assert.AreEqual((byte)0x06, image[Dm1702NativeImageAssumptions.GpsSystemsStart + 0x09]);

        Assert.AreEqual((byte)'D', image[Dm1702NativeImageAssumptions.ConfigStart + 0x180]);
        Assert.AreEqual((byte)'M', image[Dm1702NativeImageAssumptions.ConfigStart + 0x181]);

        var idBase = Dm1702NativeImageAssumptions.ConfigStart + 0x30;
        Assert.AreEqual(0x87, image[idBase]);
        Assert.AreEqual(0xD6, image[idBase + 1]);
        Assert.AreEqual(0x12, image[idBase + 2]);
        Assert.AreEqual(0x00, image[idBase + 3]);
    }

    [TestMethod]
    public void Write_SerializesDtmfConfig_OemValidated()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            DtmfConfig = new DtmfConfig("1234", "5678", "9012", CodeplugConfidence.Unknown),
        };

        Dm1702NativeConfigSerializer.Write(image, model);

        var cfgBase = Dm1702NativeImageAssumptions.ConfigStart;
        Assert.AreEqual((byte)'1', image[cfgBase + 0x192]);
        Assert.AreEqual((byte)'4', image[cfgBase + 0x195]);
        Assert.AreEqual((byte)'5', image[cfgBase + 0x19C]);
        Assert.AreEqual((byte)'8', image[cfgBase + 0x19F]);
        Assert.AreEqual((byte)'9', image[cfgBase + 0x1A4]);
        Assert.AreEqual((byte)'2', image[cfgBase + 0x1A7]);
    }

    [TestMethod]
    public void Write_SerializesDmrIdLe32_OemValidated()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            RadioIdentity = new RadioIdentitySettings("1000001", "N0CALL", CodeplugConfidence.Inferred),
        };

        Dm1702NativeConfigSerializer.Write(image, model);

        // Synthetic ID 1000001 = 0x000F4241 -> LE bytes: [0x41, 0x42, 0x0F, 0x00]
        var idBase = Dm1702NativeImageAssumptions.ConfigStart + 0x30;
        Assert.AreEqual(0x41, image[idBase]);
        Assert.AreEqual(0x42, image[idBase + 1]);
        Assert.AreEqual(0x0F, image[idBase + 2]);
        Assert.AreEqual(0x00, image[idBase + 3]);
    }

    [TestMethod]
    public void Write_SerializesStartupScreen_OemValidated()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            StartupScreen = new StartupScreen("HELLO", "WORLD", CodeplugConfidence.Inferred),
        };

        Dm1702NativeConfigSerializer.Write(image, model);

        var cfgBase = Dm1702NativeImageAssumptions.ConfigStart;
        Assert.AreEqual((byte)'H', image[cfgBase + 0x1C0]);
        Assert.AreEqual((byte)'O', image[cfgBase + 0x1C4]);
        Assert.AreEqual((byte)'W', image[cfgBase + 0x1D0]);
        Assert.AreEqual((byte)'D', image[cfgBase + 0x1D4]);
    }

    [TestMethod]
    public void Write_SerializesKeyAssignments_OemValidated()
    {
        // OEM evidence: 7th capture has 9 of 14 slots changed from baseline defaults.
        // Baseline:    [05,06,07,00,0C,00,04,0C,00,09,00,17,00,07]
        // 7th capture: [05,1E,07,0A,05,00,04,13,0F,23,14,28,15,07]
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        byte[] assignments = [0x05, 0x1E, 0x07, 0x0A, 0x05, 0x00, 0x04, 0x13, 0x0F, 0x23, 0x14, 0x28, 0x15, 0x07];
        var model = CodeplugImage.CreateEmpty() with
        {
            KeyAssignments = new KeyAssignmentTable(assignments, CodeplugConfidence.Inferred),
        };

        Dm1702NativeConfigSerializer.Write(image, model);

        var cfgBase = Dm1702NativeImageAssumptions.ConfigStart;
        Assert.AreEqual(0x05, image[cfgBase + 0x150]); // SK1 Short = Monitor
        Assert.AreEqual(0x1E, image[cfgBase + 0x151]); // SK1 Long  = DTMF Dial
        Assert.AreEqual(0x07, image[cfgBase + 0x152]); // SK2 Short = VOX
        Assert.AreEqual(0x0A, image[cfgBase + 0x153]); // SK2 Long  = 1750Hz
        Assert.AreEqual(0x05, image[cfgBase + 0x154]); // TK Short  = Monitor
        Assert.AreEqual(0x00, image[cfgBase + 0x155]); // TK Long   = None
        Assert.AreEqual(0x04, image[cfgBase + 0x156]); // P1 Short  = PowerLevel
        Assert.AreEqual(0x13, image[cfgBase + 0x157]); // P1 Long   = GPS
        Assert.AreEqual(0x0F, image[cfgBase + 0x158]); // P2 Short  = DisplayToggle
        Assert.AreEqual(0x23, image[cfgBase + 0x159]); // P2 Long   = Ranging
        Assert.AreEqual(0x14, image[cfgBase + 0x15A]); // P3 Short  = Record
        Assert.AreEqual(0x28, image[cfgBase + 0x15B]); // P3 Long   = MicGainUp
        Assert.AreEqual(0x15, image[cfgBase + 0x15C]); // P4 Short  = Playback
        Assert.AreEqual(0x07, image[cfgBase + 0x15D]); // P4 Long   = VOX
    }

    [TestMethod]
    public void Write_SerializesDefaultKeyAssignments()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty();

        Dm1702NativeConfigSerializer.Write(image, model);

        var cfgBase = Dm1702NativeImageAssumptions.ConfigStart;
        // Default key assignments match stock CPS baseline
        Assert.AreEqual(0x05, image[cfgBase + 0x150]); // SK1 Short = Monitor
        Assert.AreEqual(0x06, image[cfgBase + 0x151]); // SK1 Long  = Scan
        Assert.AreEqual(0x07, image[cfgBase + 0x152]); // SK2 Short = VOX
        Assert.AreEqual(0x00, image[cfgBase + 0x153]); // SK2 Long  = None
        Assert.AreEqual(0x0C, image[cfgBase + 0x154]); // TK Short  = NuisanceDelete
        Assert.AreEqual(0x00, image[cfgBase + 0x155]); // TK Long   = None
        Assert.AreEqual(0x04, image[cfgBase + 0x156]); // P1 Short  = PowerLevel
        Assert.AreEqual(0x0C, image[cfgBase + 0x157]); // P1 Long   = NuisanceDelete
        Assert.AreEqual(0x00, image[cfgBase + 0x158]); // P2 Short  = None
        Assert.AreEqual(0x09, image[cfgBase + 0x159]); // P2 Long   = LoneWorker
        Assert.AreEqual(0x00, image[cfgBase + 0x15A]); // P3 Short  = None
        Assert.AreEqual(0x17, image[cfgBase + 0x15B]); // P3 Long   = FM Radio
        Assert.AreEqual(0x00, image[cfgBase + 0x15C]); // P4 Short  = None
        Assert.AreEqual(0x07, image[cfgBase + 0x15D]); // P4 Long   = VOX
    }

    [TestMethod]
    public void Write_StockDefaults_MatchOemBaselineBytes_OemValidated()
    {
        // OEM evidence: baseline.data config section byte-for-byte comparison.
        // Stock CPS defaults must produce exact OEM byte values at every decoded offset.
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            DisplaySettings = new DisplaySettings(BacklightDuration.TenSeconds, false, false, CodeplugConfidence.Inferred),
            PowerSettings = new PowerSettings(PowerLevel.Medium, false, CodeplugConfidence.Inferred),
            SquelchSettings = new SquelchSettings(5, 5, CodeplugConfidence.Inferred),
            GeneralSettings = new GeneralSettings("", "", "", CodeplugConfidence.Inferred),
            StartupScreen = new StartupScreen("", "", CodeplugConfidence.Inferred),
            RadioIdentity = new RadioIdentitySettings("1", "NOCALL", CodeplugConfidence.Inferred),
            DtmfConfig = new DtmfConfig("", "", "", CodeplugConfidence.Inferred),
            KeyAssignments = new KeyAssignmentTable(
                (byte[])KeyAssignmentTable.DefaultAssignments.Clone(), CodeplugConfidence.Inferred),
        };

        Dm1702NativeConfigSerializer.Write(image, model);

        var cfgBase = Dm1702NativeImageAssumptions.ConfigStart;

        // +0x00: backlight ON
        Assert.AreEqual(0x01, image[cfgBase + 0x00], "+0x00 backlight");

        // +0x01: independently observed reserved value 0x32.
        Assert.AreEqual(0x32, image[cfgBase + 0x01], "+0x01 constant 0x32");

        // +0x02: analog squelch level (stock=5)
        Assert.AreEqual(0x05, image[cfgBase + 0x02], "+0x02 analog squelch");

        // +0x03: keypad lock OFF + no custom features = 0x00 (stock baseline)
        Assert.AreEqual(0x00, image[cfgBase + 0x03], "+0x03 lock/features bitfield");

        // +0x07: ShowChannelNumber OFF = 0x00
        Assert.AreEqual(0x00, image[cfgBase + 0x07], "+0x07 show channel number");

        // +0x0B: ShowClock OFF = 0x47 (stock baseline — NOT 0x40)
        Assert.AreEqual(0x47, image[cfgBase + 0x0B], "+0x0B clock/display flags");

        // +0x0C: DefaultPower Medium = 0x01
        Assert.AreEqual(0x01, image[cfgBase + 0x0C], "+0x0C default power");

        // +0x0D: battery saver OFF, b4 always set = 0x10 (stock baseline — NOT 0x00)
        Assert.AreEqual(0x10, image[cfgBase + 0x0D], "+0x0D power/feature bitfield");

        // +0x0E: digital squelch level (stock=5)
        Assert.AreEqual(0x05, image[cfgBase + 0x0E], "+0x0E digital squelch");

        // +0x20..+0x21: stock constants 0x37, 0x08
        Assert.AreEqual(0x37, image[cfgBase + 0x20], "+0x20 stock constant");
        Assert.AreEqual(0x08, image[cfgBase + 0x21], "+0x21 stock constant");

        // +0x26..+0x27: VHF band low limit constant 0x60, 0x13
        Assert.AreEqual(0x60, image[cfgBase + 0x26], "+0x26 VHF low limit");
        Assert.AreEqual(0x13, image[cfgBase + 0x27], "+0x27 VHF low limit");

        // +0x2A..+0x2B: VHF band high limit constant 0x40, 0x17
        Assert.AreEqual(0x40, image[cfgBase + 0x2A], "+0x2A VHF high limit");
        Assert.AreEqual(0x17, image[cfgBase + 0x2B], "+0x2B VHF high limit");

        // +0x30: DMR ID=1 as LE32
        Assert.AreEqual(0x01, image[cfgBase + 0x30], "+0x30 DMR ID byte 0");
        Assert.AreEqual(0x00, image[cfgBase + 0x31], "+0x31 DMR ID byte 1");

        // +0x039: stock constant 0x08
        Assert.AreEqual(0x08, image[cfgBase + 0x39], "+0x39 stock constant");

        // +0x03C..+0x03D: stock constants 0x03, 0x02
        Assert.AreEqual(0x03, image[cfgBase + 0x3C], "+0x3C stock constant");
        Assert.AreEqual(0x02, image[cfgBase + 0x3D], "+0x3D stock constant");

        // +0x040: stock bitfield 0x25
        Assert.AreEqual(0x25, image[cfgBase + 0x40], "+0x40 stock bitfield");

        // +0x041: stock feature bitmask 0x28 (no advanced features)
        Assert.AreEqual(0x28, image[cfgBase + 0x41], "+0x41 stock feature bitmask");

        // +0x043: constant 0x08
        Assert.AreEqual(0x08, image[cfgBase + 0x43], "+0x43 stock constant");

        // +0x052: constant 0x80
        Assert.AreEqual(0x80, image[cfgBase + 0x52], "+0x52 stock constant");

        // +0x100..+0x10F: stock channel bitmap
        Assert.AreEqual(0x7F, image[cfgBase + 0x100], "+0x100 bitmap");
        Assert.AreEqual(0xFF, image[cfgBase + 0x103], "+0x103 bitmap");
        Assert.AreEqual(0x7F, image[cfgBase + 0x10F], "+0x10F bitmap");
        Assert.AreEqual(0x04, image[cfgBase + 0x110], "+0x110 bitmap trailer");

        // +0x1B5..+0x1B7: constants
        Assert.AreEqual(0xDF, image[cfgBase + 0x1B5], "+0x1B5 constant");
        Assert.AreEqual(0xFC, image[cfgBase + 0x1B6], "+0x1B6 constant");
        Assert.AreEqual(0xFF, image[cfgBase + 0x1B7], "+0x1B7 constant");
    }

    [TestMethod]
    public void Write_AdvancedFeatures_SetsConfigFlagBytes_OemValidated()
    {
        // OEM evidence: 7th capture has config+0x003=0x04, +0x041=0xF8 when system features configured.
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            GeneralSettings = new GeneralSettings("", "", "", CodeplugConfidence.Inferred),
            ParameterSettings = new ParameterSettings(Language.English, false, 1, 30, 12, 1, false, true, CodeplugConfidence.Inferred),
            PowerSettings = new PowerSettings(PowerLevel.High, false, CodeplugConfidence.Inferred),
            LoneWorkerConfig = new LoneWorkerConfig(true, 10, 10, CodeplugConfidence.Inferred),
        };

        Dm1702NativeConfigSerializer.Write(image, model);

        var cfgBase = Dm1702NativeImageAssumptions.ConfigStart;

        Assert.AreEqual(0x24, image[cfgBase + 0x03], "+0x03 lock+features");

        // +0x0D: battery saver OFF, lone worker ON = 0x13 (b4 always set + b0+b1 lone worker echo)
        Assert.AreEqual(0x13, image[cfgBase + 0x0D], "+0x0D lone worker echo, no battery saver");

        // +0x041: advanced features present = 0xF8
        Assert.AreEqual(0xF8, image[cfgBase + 0x41], "+0x41 advanced features bitmask");
    }

    [TestMethod]
    public void Write_BatterySaver_SetsBitfieldCorrectly_OemValidated()
    {
        // Battery saver ON should set b2+b3 (0x0C) on top of base constant b4(0x10).
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];
        var model = CodeplugImage.CreateEmpty() with
        {
            PowerSettings = new PowerSettings(PowerLevel.High, true, CodeplugConfidence.Inferred),
        };

        Dm1702NativeConfigSerializer.Write(image, model);

        var cfgBase = Dm1702NativeImageAssumptions.ConfigStart;

        // +0x0D: b4(0x10) always set + b2+b3(0x0C) for battery saver = 0x1C
        Assert.AreEqual(0x1C, image[cfgBase + 0x0D], "+0x0D battery saver ON");
    }
}
