using System.Buffers.Binary;
using System.Text;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Serializes the DM-1702 native image configuration section (base offset 0x5000),
/// including DMR ID, radio name, DTMF settings, startup text, and key assignments.
/// </summary>
public static class Dm1702NativeConfigSerializer
{
    // Independently verified: DTMF PttId/KillCode/ReviveCode at config+0x192.
    // 3 fields: PttId(10 bytes), KillCode(8 bytes), ReviveCode(8 bytes).
    private const int DtmfPttIdOffset = 0x192;
    private const int DtmfPttIdLength = 10;
    private const int DtmfKillCodeOffset = 0x19C;
    private const int DtmfReviveCodeOffset = 0x1A4;
    private const int DtmfCodeLength = 8;

    // 7 programmable keys × 2 actions (short/long press), each a single function code byte.
    // Evidence: baseline=[05,06,07,00,0C,00,04,0C,00,09,00,17,00,07],
    //   7th capture=[05,1E,07,0A,05,00,04,13,0F,23,14,28,15,07] (9 of 14 changed),
    private const int KeyAssignmentOffset = 0x150;

    private const int RadioNameOffset = 0x180;
    private const int RadioNameLength = 16;

    private const int StartupScreenOffset = 0x1C0;
    private const int StartupScreenLineLength = 16;

    public static void Write(Span<byte> image, CodeplugImage model)
    {
        var config = image.Slice(Dm1702NativeImageAssumptions.ConfigStart, Dm1702NativeImageAssumptions.ConfigLength);

        // Zero the config region so every un-modeled field starts at 0x00.
        // The model is the sole source of truth — all known OEM bytes are
        // written explicitly below.
        config.Fill(0x00);

        // Binary evidence: orig=1(10s), eng=5(Always). NOT a boolean.
        config[0x00] = (byte)Math.Clamp((int)model.DisplaySettings.BacklightDuration, 0, 5);

        config[0x01] = 0x32;

        config[0x02] = (byte)Math.Clamp(model.SquelchSettings.AnalogLevel, 0, 15);

        // b0(0x01) = VOX enable. b5(0x20) = keypad lock. b2(0x04) = system features modified flag.
        // b6(0x40) = CTCSS/DCS tail revert.
        config[0x03] = (byte)(
            (model.ParameterSettings.VoxEnabled ? 0x01 : 0x00) |
            ConfigFeatureFlags(model) |
            (model.ParameterSettings.KeypadLockEnabled ? 0x20 : 0x00) |
            (model.ParameterSettings.CtcssTailRevert ? 0x40 : 0x00));

        config[0x07] = model.DisplaySettings.ShowChannelNumber ? (byte)0x40 : (byte)0x00;

        // b6(0x40) always set. b3(0x08) = show clock. Default has lower bits 0x07 set (baseline mask).
        // When clock is enabled, lower bits clear and b3 sets instead.
        config[0x0B] = model.DisplaySettings.ShowClock ? (byte)0x48 : (byte)0x47;

        config[0x0C] = (byte)(model.PowerSettings.DefaultPower switch
        {
            PowerLevel.Low => 0x00,
            PowerLevel.Medium => 0x01,
            _ => 0x02,
        });

        // b4(0x10) always set in all captures. Lower nibble varies:
        // b0+b1 (0x03): Lone worker echo flag — perfect 1:1 correlation with lone worker enabled at 0x8B00.
        config[0x0D] = (byte)(0x10 |
            (model.PowerSettings.BatterySaverEnabled ? 0x0C : 0x00) |
            (model.LoneWorkerConfig.Enabled ? 0x03 : 0x00));

        config[0x0E] = (byte)Math.Clamp(model.SquelchSettings.DigitalLevel, 0, 15);

        WriteDateStamp(config.Slice(0x10, 16), DateTime.UtcNow);

        config[0x20] = 0x37;
        config[0x21] = 0x08;
        config[0x26] = 0x60;
        config[0x27] = 0x13;
        config[0x2A] = 0x40;
        config[0x2B] = 0x17;

        WriteDmrIdLe32(config.Slice(0x30, 4), model.RadioIdentity.DmrId);

        config[0x39] = 0x08;

        config[0x3C] = 0x03;
        config[0x3D] = 0x02;

        // Default=0x25 (b5+b2+b0). b5 always set.
        config[0x40] = 0x25;

        // Default=0x28 (b3+b5), advanced=0xF8 (b3+b4+b5+b6+b7).
        config[0x41] = ConfigHasAdvancedFeatures(model) ? (byte)0xF8 : (byte)0x28;

        config[0x43] = 0x08;

        config[0x52] = 0x80;

        config[0x55] = 0x01;
        config[0x56] = 0x02;
        config[0x57] = 0x5F;

        config[0x70] = (byte)Math.Clamp(model.ParameterSettings.MicGain, 1, 10);
        config[0x71] = 0x05;
        config[0x72] = (byte)Math.Clamp(model.ParameterSettings.VoxLevel, 1, 10);
        config[0x73] = (byte)Math.Clamp(model.ParameterSettings.TxTimeout, 0, 255);
        config[0x75] = (byte)Math.Clamp(model.ParameterSettings.TxPreambleDuration, 0, 255);

        config[0x100] = 0x7F;
        config[0x101] = 0x7F;
        config[0x102] = 0x7F;
        config[0x103] = 0xFF;
        config[0x104] = 0xFF;
        config[0x105] = 0xFF;
        config[0x106] = 0x7F;
        config[0x107] = 0x7F;
        config[0x108] = 0x7F;
        config[0x109] = 0x7F;
        config[0x10A] = 0x7F;
        config[0x10B] = 0x7F;
        config[0x10C] = 0x7F;
        config[0x10D] = 0x7F;
        config[0x10E] = 0x7F;
        config[0x10F] = 0x7F;
        config[0x110] = 0x04;

        config[0x1AB] = 0x01;
        config[0x1B2] = 0x01;
        config[0x1B5] = 0xDF;
        config[0x1B6] = 0xFC;
        config[0x1B7] = 0xFF;

        WriteAscii(config.Slice(RadioNameOffset, RadioNameLength), model.GeneralSettings.RadioName, RadioNameLength);

        WriteDtmfConfig(config, model.DtmfConfig);

        WriteKeyAssignments(config, model.KeyAssignments);

        WriteAscii(config.Slice(StartupScreenOffset, StartupScreenLineLength), model.StartupScreen.Line1, StartupScreenLineLength);
        WriteAscii(config.Slice(StartupScreenOffset + StartupScreenLineLength, StartupScreenLineLength), model.StartupScreen.Line2, StartupScreenLineLength);

        // Binary evidence: orig=0x00(Chinese), eng=0x01(English).
        config[0x200] = (byte)model.ParameterSettings.Language;
    }

    /// <summary>
    /// </summary>
    private static void WriteDmrIdLe32(Span<byte> destination, string dmrId)
    {
        var numeric = string.IsNullOrWhiteSpace(dmrId) ? "0" : new string(dmrId.Where(char.IsAsciiDigit).ToArray());
        if (numeric.Length == 0) numeric = "0";
        var id = uint.TryParse(numeric, out var parsed) ? parsed : 0u;
        BinaryPrimitives.WriteUInt32LittleEndian(destination, id);
    }

    /// <summary>
    /// config+0x19C (KillCode, 8 bytes), config+0x1A4 (ReviveCode, 8 bytes).
    /// </summary>
    private static void WriteDtmfConfig(Span<byte> config, DtmfConfig dtmf)
    {
        WriteAscii(config.Slice(DtmfPttIdOffset, DtmfPttIdLength), dtmf.PttId, DtmfPttIdLength);
        WriteAscii(config.Slice(DtmfKillCodeOffset, DtmfCodeLength), dtmf.KillCode, DtmfCodeLength);
        WriteAscii(config.Slice(DtmfReviveCodeOffset, DtmfCodeLength), dtmf.ReviveCode, DtmfCodeLength);
    }

    private static void WriteDateStamp(Span<byte> destination, DateTime utcNow)
    {
        destination.Fill(0x00);
        var stamp = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var bytes = Encoding.ASCII.GetBytes(stamp);
        bytes.CopyTo(destination);
    }

    private static void WriteAscii(Span<byte> destination, string value, int maxLength)
    {
        destination.Fill(0x00);
        var bytes = Encoding.ASCII.GetBytes(value.Length >= maxLength ? value[..(maxLength - 1)] : value);
        bytes.CopyTo(destination);
    }

    /// <summary>
    /// 7 keys × short/long press = 14 function code bytes.
    /// Evidence: identical defaults in baseline, D1, and baseline+ch2_digital;
    /// </summary>
    private static void WriteKeyAssignments(Span<byte> config, KeyAssignmentTable keyAssignments)
    {
        var assignments = keyAssignments.Assignments;
        var length = Math.Min(assignments.Length, KeyAssignmentTable.TableLength);
        for (var i = 0; i < length; i++)
        {
            config[KeyAssignmentOffset + i] = assignments[i];
        }

        // Pad remaining slots with 0x00 (None) if fewer than 14 provided.
        for (var i = length; i < KeyAssignmentTable.TableLength; i++)
        {
            config[KeyAssignmentOffset + i] = 0x00;
        }
    }

    /// <summary>
    /// Set when any non-default system-level features are configured (privacy, emergency,
    /// lone worker, custom key assignments, quick text, etc.).
    /// Evidence: baseline=0x00 (default), 7th=0x04 (privacy+emergency+loneworker+keys changed),
    /// </summary>
    private static byte ConfigFeatureFlags(CodeplugImage model)
    {
        var hasCustomFeatures =
            model.LoneWorkerConfig.Enabled ||
            model.PrivacyEntries.Count > 0 ||
            model.EmergencySystems.Count > 0 ||
            model.QuickTextMessages.Count > 0 ||
            !model.KeyAssignments.Assignments.AsSpan().SequenceEqual(KeyAssignmentTable.DefaultAssignments);

        return hasCustomFeatures ? (byte)0x04 : (byte)0x00;
    }

    /// <summary>
    /// any advanced system-level features are configured beyond defaults.
    /// </summary>
    private static bool ConfigHasAdvancedFeatures(CodeplugImage model)
    {
        return model.LoneWorkerConfig.Enabled ||
               model.PrivacyEntries.Count > 0 ||
               model.EmergencySystems.Count > 0 ||
               model.QuickTextMessages.Count > 0 ||
               !model.KeyAssignments.Assignments.AsSpan().SequenceEqual(KeyAssignmentTable.DefaultAssignments) ||
               model.GeneralSettings.RadioName is not ("BAO1702" or "");
    }
}
