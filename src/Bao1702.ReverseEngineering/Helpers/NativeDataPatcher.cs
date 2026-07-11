using System.Globalization;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Patches individual channel records and names into an existing native image byte array,
/// supporting in-place edits without full image rebuild.
/// </summary>
public static class NativeDataPatcher
{
    private const int NameStride = 11;

    public static byte[] PatchFromRecoveredCodeplug(byte[] baseImage, RecoveredNativeCodeplug codeplug)
    {
        ArgumentNullException.ThrowIfNull(baseImage);
        ArgumentNullException.ThrowIfNull(codeplug);

        var output = (byte[])baseImage.Clone();
        foreach (var channel in codeplug.Channels)
        {
            PatchChannel(output, channel);
        }

        return output;
    }

    public static RecoveredNativeCodeplug BuildRecoveredNativeCodeplug(SavedCodeplugDataDump dump)
    {
        ArgumentNullException.ThrowIfNull(dump);

        var channels = dump.TypedSnapshot.Channels
            .OrderBy(static channel => channel.Index)
            .Select(channel => new RecoveredNativeChannel(
                channel.Index,
                channel.Name,
                channel.RxFrequencyMHz,
                channel.TxFrequencyMHz,
                channel.Mode == ProvisionalChannelMode.Digital ? ChannelKind.Digital : ChannelKind.Analog,
                ToPower(channel.Power),
                ToBandwidth(channel.Bandwidth),
                channel.RxOnly,
                channel.TalkAroundEnabled,
                channel.Mode == ProvisionalChannelMode.Digital ? 1 : null,
                channel.Mode == ProvisionalChannelMode.Digital ? 1 : null,
                InferContact(channel),
                InferRxGroup(channel),
                InferGpsSystem(channel),
                ResolveNameOffset(channel.Index),
                ResolveRecordOffset(channel.Index),
                channel.Confidence))
            .ToArray();

        var rebuiltImage = NativeCodeplugRebuilder.BuildCodeplugImage(dump);

        return new RecoveredNativeCodeplug(
            channels,
            rebuiltImage.Contacts,
            rebuiltImage.RxGroups,
            rebuiltImage.Zones,
            File.ReadAllBytes(dump.FilePath),
            CodeplugConfidence.Inferred);
    }

    private static void PatchChannel(byte[] image, RecoveredNativeChannel channel)
    {
        if (!channel.RecordOffset.HasValue)
        {
            return;
        }

        var recordOffset = channel.RecordOffset.Value;
        if (recordOffset + 0x2F >= image.Length)
        {
            return;
        }

        WriteObservedFrequency(image, recordOffset, channel.RxFrequencyMHz);
        WriteObservedFrequency(image, recordOffset + 4, channel.TxFrequencyMHz);
        image[recordOffset + 7] = channel.Power switch
        {
            PowerLevel.Low => 0x00,
            PowerLevel.Medium => 0x02,
            _ => 0x09,
        };

        if (channel.NameOffset.HasValue && channel.NameOffset.Value + NameStride <= image.Length)
        {
            WriteFixedAscii(image, channel.NameOffset.Value, NameStride, channel.Name);
        }
    }

    private static int ResolveNameOffset(int index)
    {
        if (index >= 0 && index <= Dm1702NativeImageAssumptions.MaxSupportedPatchedChannelIndex)
        {
            return Dm1702NativeImageAssumptions.GetChannelNameOffset(index);
        }

        return -1;
    }

    private static int? ResolveRecordOffset(int index)
    {
        if (index >= 0 && index <= Dm1702NativeImageAssumptions.MaxSupportedPatchedChannelIndex)
        {
            return Dm1702NativeImageAssumptions.GetChannelRecordOffset(index);
        }

        return null;
    }

    private static void WriteFixedAscii(byte[] image, int offset, int length, string value)
    {
        Array.Fill(image, (byte)0, offset, length);
        var bytes = System.Text.Encoding.ASCII.GetBytes(value.Length > length ? value[..length] : value);
        Array.Copy(bytes, 0, image, offset, bytes.Length);
    }

    private static void WriteObservedFrequency(byte[] image, int offset, double frequencyMHz)
    {
        var digits = ((int)Math.Round(frequencyMHz * 100000d, MidpointRounding.AwayFromZero)).ToString("D8", CultureInfo.InvariantCulture);
        image[offset] = byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        image[offset + 1] = byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        image[offset + 2] = byte.Parse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        image[offset + 3] = byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static PowerLevel ToPower(ProvisionalPowerLevel power)
        => power switch
        {
            ProvisionalPowerLevel.Low => PowerLevel.Low,
            ProvisionalPowerLevel.Medium => PowerLevel.Medium,
            _ => PowerLevel.High,
        };

    private static ChannelBandwidth ToBandwidth(ProvisionalBandwidth bandwidth)
        => bandwidth == ProvisionalBandwidth.Wide ? ChannelBandwidth.Wide : ChannelBandwidth.Narrow;

    private static string? InferContact(TypedChannelSnapshot channel) => null;

    private static string? InferRxGroup(TypedChannelSnapshot channel) => null;

    private static string? InferGpsSystem(TypedChannelSnapshot channel) => null;
}
