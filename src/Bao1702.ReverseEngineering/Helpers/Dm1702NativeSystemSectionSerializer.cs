using System.Text;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Serializes the DM-1702 native image system section (base offset 0x8000+).
/// </summary>
/// <remarks>
/// <para>Section layout:</para>
/// <list type="bullet">
///   <item>Privacy: header byte at +0x800, entries at +0x801, stride 0x17.</item>
///   <item>Emergency: entries at +0x620, stride 0x14, summary byte at +0x6F0.</item>
///   <item>Lone Worker: enable at +0xB00, response time at +0xB01, reminder interval at +0xB02.</item>
///   <item>Quick Text: count at absolute 0xA000, messages at +0x10, stride 0x81.</item>
///   <item>One-touch call table at 0x1FA00: reserved (not written; left at zero-fill default).</item>
/// </list>
/// </remarks>
public static class Dm1702NativeSystemSectionSerializer
{

    public static void Write(Span<byte> image, CodeplugImage model)
    {
        WritePrivacySection(image, model.PrivacyEntries);
        WriteEmergencySection(image, model.EmergencySystems);
        WriteLoneWorkerSection(image, model.LoneWorkerConfig);
        WriteQuickTextSection(image, model.QuickTextMessages);
        // One-touch call table region at 0x1FA00 is reserved; entries use a 24-byte stride
        // (index + type + name). Button key assignments are handled separately by WriteKeyAssignments
        // at config+0x150. This region is left at the zero-fill default.
    }

    /// <summary>
    /// Header byte is [STRUCTURALLY GENERATED] as entries.Count — round-trips correctly for
    /// user-configured entries. OEM default entry behavior is not fully characterized.
    /// Each entry: 10-byte name + 1-byte key type + 8-byte key data (BCD, 0xFF padded) + 4-byte footer.
    /// </summary>
    private static void WritePrivacySection(Span<byte> image, IReadOnlyList<PrivacyEntry> entries)
    {
        var baseOffset = Dm1702NativeImageAssumptions.RxListsStart + Dm1702NativeImageAssumptions.PrivacyHeaderOffset;
        var sectionSpan = image.Slice(baseOffset, 0x100);
        sectionSpan.Fill(0x00);

        image[baseOffset] = (byte)entries.Count;

        for (var i = 0; i < entries.Count; i++)
        {
            var entryOffset = Dm1702NativeImageAssumptions.RxListsStart
                + Dm1702NativeImageAssumptions.PrivacyEntryStart
                + (i * Dm1702NativeImageAssumptions.PrivacyEntryStride);

            var entry = image.Slice(entryOffset, Dm1702NativeImageAssumptions.PrivacyEntryStride);
            entry.Fill(0x00);

            WriteAscii(entry.Slice(0, Dm1702NativeImageAssumptions.PrivacyNameLength),
                entries[i].Name, Dm1702NativeImageAssumptions.PrivacyNameLength);

            entry[0x0A] = entries[i].KeyType;

            var keyData = entries[i].KeyData;
            var keySpan = entry.Slice(0x0B, Dm1702NativeImageAssumptions.PrivacyKeyDataLength);
            keySpan.Fill(0xFF);
            keyData.AsSpan(0, Math.Min(keyData.Length, Dm1702NativeImageAssumptions.PrivacyKeyDataLength)).CopyTo(keySpan);

            var footer = entries[i].Footer;
            footer.AsSpan(0, Math.Min(footer.Length, Dm1702NativeImageAssumptions.PrivacyFooterLength))
                .CopyTo(entry.Slice(0x13, Dm1702NativeImageAssumptions.PrivacyFooterLength));
        }
    }

    /// <summary>
    /// Byte at 0x86F0 is [STRUCTURALLY GENERATED] as entries.Count — round-trips correctly for
    /// user-configured entries. OEM default entry behavior is not fully characterized.
    /// Each entry: 10-byte name + Type + Mode + RevertCh + AlarmCallToFollow +
    /// ImpoliteRetries + PoliteRetries + VoiceCycles + Reserved + HotMicDuration + RxIntervalDuration.
    /// </summary>
    private static void WriteEmergencySection(Span<byte> image, IReadOnlyList<EmergencySystem> entries)
    {
        var tableBase = Dm1702NativeImageAssumptions.RxListsStart + Dm1702NativeImageAssumptions.EmergencyTableOffset;
        var tableSpan = image.Slice(tableBase, 0xD0);
        tableSpan.Fill(0x00);

        var countOffset = Dm1702NativeImageAssumptions.RxListsStart + Dm1702NativeImageAssumptions.EmergencyCountOffset;
        image[countOffset] = (byte)entries.Count;

        for (var i = 0; i < entries.Count; i++)
        {
            var entryOffset = tableBase + (i * Dm1702NativeImageAssumptions.EmergencyEntryStride);
            var entry = image.Slice(entryOffset, Dm1702NativeImageAssumptions.EmergencyEntryStride);
            entry.Fill(0x00);

            WriteAscii(entry.Slice(0, 10), entries[i].Name, 10);

            entry[0x0A] = entries[i].EmergencyType;
            entry[0x0B] = entries[i].EmergencyMode;
            entry[0x0C] = entries[i].RevertChannel;
            entry[0x0D] = entries[i].AlarmCallToFollow;
            entry[0x0E] = entries[i].ImpoliteRetries;
            entry[0x0F] = entries[i].PoliteRetries;
            entry[0x10] = entries[i].VoiceCycles;
            entry[0x11] = entries[i].Reserved;
            entry[0x12] = entries[i].HotMicDuration;
            entry[0x13] = entries[i].RxIntervalDuration;
        }
    }

    /// <summary>
    /// </summary>
    private static void WriteLoneWorkerSection(Span<byte> image, LoneWorkerConfig config)
    {
        var baseOffset = Dm1702NativeImageAssumptions.RxListsStart + Dm1702NativeImageAssumptions.LoneWorkerOffset;
        image[baseOffset] = config.Enabled ? (byte)0x01 : (byte)0x00;
        image[baseOffset + 1] = config.ResponseTimeMinutes;
        image[baseOffset + 2] = config.ReminderTimeSeconds;
    }

    /// <summary>
    /// Each message: 1-byte length + 128 bytes ASCII text (null-padded).
    /// </summary>
    private static void WriteQuickTextSection(Span<byte> image, IReadOnlyList<QuickTextMessage> messages)
    {
        var baseOffset = Dm1702NativeImageAssumptions.QuickTextStart;
        var headerSpan = image.Slice(baseOffset, Dm1702NativeImageAssumptions.QuickTextHeaderLength);
        headerSpan.Fill(0x00);

        image[baseOffset] = (byte)messages.Count;

        for (var i = 0; i < messages.Count; i++)
        {
            var msgOffset = baseOffset
                + Dm1702NativeImageAssumptions.QuickTextHeaderLength
                + (i * Dm1702NativeImageAssumptions.QuickTextMessageStride);

            var msgSpan = image.Slice(msgOffset, Dm1702NativeImageAssumptions.QuickTextMessageStride);
            msgSpan.Fill(0x00);

            var text = messages[i].Text ?? string.Empty;
            var textBytes = Encoding.ASCII.GetBytes(
                text.Length > Dm1702NativeImageAssumptions.QuickTextMaxTextLength
                    ? text[..Dm1702NativeImageAssumptions.QuickTextMaxTextLength]
                    : text);

            msgSpan[0] = (byte)textBytes.Length;
            textBytes.CopyTo(msgSpan.Slice(1));
        }
    }

    private static void WriteAscii(Span<byte> destination, string value, int maxLength)
    {
        destination.Fill(0x00);
        var bytes = Encoding.ASCII.GetBytes(value.Length >= maxLength ? value[..(maxLength - 1)] : value);
        bytes.CopyTo(destination);
    }
}
