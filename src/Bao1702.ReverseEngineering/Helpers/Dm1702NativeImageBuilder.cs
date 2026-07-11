using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Builds a complete 245,760-byte (0x3C000) DM-1702 native codeplug image from a
/// <see cref="CodeplugImage"/> model, coordinating all section serializers.
/// </summary>
public static class Dm1702NativeImageBuilder
{
    /// <summary>
    /// Builds a native image entirely from the <see cref="CodeplugImage"/> model.
    /// The model is the sole source of truth — every byte is written from modeled
    /// </summary>
    public static byte[] Build(CodeplugImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var output = new byte[Dm1702NativeImageAssumptions.ImageLength];
        Dm1702NativeSectorSerializer.Initialize(output);

        return ApplyModelToImage(output, image);
    }

    private static byte[] ApplyModelToImage(byte[] output, CodeplugImage image)
    {
        Dm1702NativeContactSerializer.Write(output, image.Contacts);
        IReadOnlyDictionary<string, int> channelIndexByName;
        try
        {
            channelIndexByName = image.Channels.ToDictionary(static channel => channel.Name, static channel => channel.Index, StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException("DM1702 native build failed because duplicate channel names cannot be mapped into native indexes.", ex);
        }

        try
        {
            Dm1702NativeConfigSerializer.Write(output, image);
            Dm1702NativeGpsSerializer.Write(output, image);
            Dm1702NativeSystemSectionSerializer.Write(output, image);
            // ScanList must be written before RxGroup because RxGroup region 2 (groups 14-31)
            // starts at 0xBFF0 which overlaps the ScanList fill region (0xB000..0xC000).
            Dm1702NativeScanListSerializer.Write(output, image.ScanLists, channelIndexByName);
            Dm1702NativeRxGroupSerializer.Write(output, image.RxGroups, image.Contacts);
            Dm1702NativeZoneSerializer.Write(output, image.Zones, channelIndexByName);
            PatchChannels(output, image.Channels, image.Contacts, image.RxGroups, image.ScanLists, image.RadioIdentity.Callsign);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidOperationException($"DM1702 native build failed due to a serializer range assumption mismatch. {ex.Message}", ex);
        }

        WriteChannelHeader(output, image.Channels.Count);

        return output;
    }

    private static void PatchChannels(byte[] output, IReadOnlyList<Channel> channels, IReadOnlyList<Contact> contacts, IReadOnlyList<RxGroup> rxGroups, IReadOnlyList<ScanList> scanLists, string? gpsSystemName)
    {
        if (channels.Count == 0)
        {
            return;
        }

        var outOfRange = channels.Where(c => c.Index - 1 < 0 || c.Index - 1 > Dm1702NativeImageAssumptions.MaxSupportedPatchedChannelIndex).ToList();
        if (outOfRange.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot build native image: {outOfRange.Count} channel(s) have out-of-range index values " +
                $"(valid: 1–{Dm1702NativeImageAssumptions.MaxSupportedPatchedChannelIndex + 1}). " +
                $"First offender: '{outOfRange[0].Name}' at index {outOfRange[0].Index}.");
        }

        // Determine which overflow pages are needed and write their phone-list slot (slot 0).
        // lands at page 0 slot 1 (0xF030); slot 0 of each page is the OEM phone-list record.
        var maxZeroBasedIndex = channels.Max(c => c.Index - 1);
        if (maxZeroBasedIndex >= Dm1702NativeImageAssumptions.LinearChannelCapacity)
        {
            var pagesNeeded = (maxZeroBasedIndex - 0x54) / Dm1702NativeImageAssumptions.ChannelRecordsPerOverflowPage + 1;
            for (int p = 0; p < pagesNeeded; p++)
            {
                Dm1702NativeImageAssumptions.WriteOverflowPagePhoneListSlot(output, p);
            }
        }

        // Write all channel records and contact maps first, then names in a second pass.
        // This ordering is CRITICAL: the last linear channel record (index 84) at 0x3FD0
        // physically extends to 0x3FFF, abutting the name table at 0x4000. If names were
        // written interleaved with records, a later record's Fill(0x00) could clobber
        // previously written name bytes at 0x4000.
        foreach (var channel in channels)
        {
            var zeroBasedIndex = channel.Index - 1;

            Dm1702NativeChannelRecordSerializer.Write(output, channel, zeroBasedIndex, contacts, rxGroups, scanLists, gpsSystemName);

            if (channel is DigitalChannel digital)
            {
                Dm1702NativeChannelRecordSerializer.WriteChannelContactMap(output, zeroBasedIndex, contacts, digital.ContactName);
            }
        }

        // Second pass: write channel names after all records are finalized.
        foreach (var channel in channels)
        {
            var zeroBasedIndex = channel.Index - 1;
            var nameOffset = Dm1702NativeImageAssumptions.GetChannelNameOffset(zeroBasedIndex);

            if (nameOffset >= 0 && nameOffset + Dm1702NativeImageAssumptions.ChannelNameStride <= output.Length)
            {
                WriteFixedAscii(output, nameOffset, Dm1702NativeImageAssumptions.ChannelNameStride, channel.Name);
            }
        }
    }

    /// <summary>
    /// Byte layout: LE16 channel count at +0x00, then 14 zero bytes (+0x02..+0x0F).
    /// Channel record 0 begins immediately at 0x3010; the header must not overwrite it.
    /// </summary>
    private static void WriteChannelHeader(byte[] image, int channelCount)
    {
        var offset = Dm1702NativeImageAssumptions.ChannelCountOffset;
        image[offset] = (byte)(channelCount & 0xFF);
        image[offset + 1] = (byte)((channelCount >> 8) & 0xFF);
        Array.Fill(image, (byte)0x00, offset + 2, 14);
    }

    private static void WriteFixedAscii(byte[] image, int offset, int length, string value)
    {
        Array.Fill(image, (byte)0, offset, length);
        var bytes = System.Text.Encoding.ASCII.GetBytes(value.Length > length ? value[..length] : value);
        Array.Copy(bytes, 0, image, offset, bytes.Length);
    }

}
