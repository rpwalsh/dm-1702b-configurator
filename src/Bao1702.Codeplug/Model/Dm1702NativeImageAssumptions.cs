namespace Bao1702.Codeplug.Model;

/// <summary>
/// Binary layout constants for the 245,760-byte DM-1702 native codeplug image,
/// including section offsets, strides, and record counts.
/// </summary>
public static class Dm1702NativeImageAssumptions
{
    public const int ImageLength = 245760;
    public const int SectorSize = 0x1000;

    //   Paged ch85 "70cm SSTV" at 0xF030 (page header = 0x30 bytes, first record at pageBase+0x30).
    public const int ChannelRecordStart = 0x3010;
    public const int ChannelRecordStride = 0x30;
    public const int ChannelNameTable1Start = 0x4000;
    public const int ChannelNameTable2Start = 0x1C000; // Independently observed secondary name table.
    public const int ChannelNameStride = 11;

    public const int ContactMetaStart = 0x7000;
    public const int ContactMetaLength = 0x0D80;
    public const int ContactDataStart = 0x1F000;
    public const int ContactRecordLength = 0x18;
    public const int ContactDataCapacity = 800;

    public const int ChannelContactMapStart = 0x1E000;
    public const int ChannelContactMapLength = 0x0800;

    public const int ConfigStart = 0x5000;
    public const int ConfigLength = 0x0500;
    public const int GpsSystemsStart = 0x5500;
    public const int GpsSystemsLength = 0x0100;

    public const int RxListsStart = 0x8000;
    public const int RxListsLength = 0x1600;
    public const int RxGroupCapacity = 32;

    // Address formula (absolute index, not region-relative):
    //   Groups 0-13:  image[index * 0x6D + 0x8010]  (index*0x6D + param_1+0x8064, param_1+0x54 base)
    //   Groups 14-31: image[index * 0x6D + 0xBA0A]  (index*0x6D + param_1+0xBA5E, param_1+0x54 base)
    public const int RxGroupRecordStride = 0x6D;
    public const int RxGroupRegion1Start = 0x8010;         // file offset for group 0 record
    public const int RxGroupRegion1Capacity = 14;           // groups 0..13
    public const int RxGroupRegion2FormulaBase = 0xBA0A;    // groups 14..31: index*0x6D + 0xBA0A
    public const int RxGroupRegion2Capacity = 18;           // groups 14..31

    public const int EmergencyTableOffset = 0x620;     // relative to RxListsStart
    public const int EmergencyEntryStride = 0x14;
    public const int EmergencyCountOffset = 0x6F0;     // relative to RxListsStart

    public const int PrivacyHeaderOffset = 0x800;      // relative to RxListsStart
    public const int PrivacyEntryStart = 0x801;        // relative to RxListsStart (first entry)
    public const int PrivacyEntryStride = 0x17;
    public const int PrivacyNameLength = 10;
    public const int PrivacyKeyDataLength = 8;
    public const int PrivacyFooterLength = 4;

    public const int LoneWorkerOffset = 0xB00;         // relative to RxListsStart

    public const int QuickTextStart = 0xA000;
    public const int QuickTextHeaderLength = 0x10;
    public const int QuickTextMessageStride = 0x81;    // 1 byte length + 128 bytes text
    public const int QuickTextMaxTextLength = 128;

    // ------------------------------------------------------------------------------
    // ZONE RECORD LAYOUT & PAGING
    // ------------------------------------------------------------------------------
    //
    // Zone count: single byte at file 0x6000 (param_1+0x6054, buffer base param_1+0x54).
    //
    //
    // Linear region: zones 0–13 (i < 0xE).
    //   Record base: ZoneDataStart + i * 0x112.
    //   Layout:  +0x00..+0x0F = reserved zeros (zone[0] +0x00 holds the global zone count),
    //            +0x10 = name (16 bytes), +0x20 = auth member count (firmware reads here),
    //            +0x21 = member list (64 × LE16), +0xA1 = echo member count, +0xA2 = echo members.
    //
    // Paged region: zones 14+ (i >= 0xE).
    //   Page base 0x2B000, 14 zones per page (0xE × 0x112 = 0xEFC ≤ 0x1000).
    //   Paged layout:  +0x00 = name (16 bytes), +0x10 = auth member count,
    //                  +0x11 = member list (64 × LE16), +0x91 = echo member count, +0x92 = echo.
    //
    //   Member list: 128 bytes = 64 × LE16 (1-based channel indices).
    //   Echo list: identical copy (sub-record stride 0x81).
    //
    // ------------------------------------------------------------------------------
    public const int ZoneDataStart = 0x6000;
    public const int ZoneRecordStride = 0x112;
    public const int ZoneRecordCapacity = 250;
    public const int MaxMembersPerZone = 64;

    /// <summary>Number of zone records in the linear region (indices 0–13, i.e. i &lt; 0xE).</summary>
    public const int LinearZoneCapacity = 14;

    /// <summary>
    /// Image offset of the first zone overflow page.
    /// </summary>
    public const int ZoneOverflowPageStart = 0x2B000;

    /// <summary>Number of zone records per overflow page (0xE = 14).</summary>
    public const int ZonesPerOverflowPage = 14;

    /// <summary>
    /// Returns the image file offset for a zone record at the given zero-based index.
    ///   Linear (i &lt; 0xE):  ZoneDataStart + i * 0x112
    ///   Paged  (i >= 0xE):  ((i - 0xE) / 0xE + 0x2B) * 0x1000 + ((i - 0xE) % 0xE) * 0x112
    /// Binary-confirmed: zone[0] at 0x6000, zone[14] at 0x2B000.
    /// </summary>
    public static int GetZoneRecordOffset(int zeroBasedIndex)
    {
        if (zeroBasedIndex < LinearZoneCapacity)
        {
            return ZoneDataStart + (zeroBasedIndex * ZoneRecordStride);
        }

        var j = zeroBasedIndex - LinearZoneCapacity;
        return (j / ZonesPerOverflowPage + 0x2B) * SectorSize + (j % ZonesPerOverflowPage) * ZoneRecordStride;
    }

    // ------------------------------------------------------------------------------
    // SCAN LIST RECORD LAYOUT
    // Layout independently verified through controlled differences and round-trip testing.
    // ------------------------------------------------------------------------------
    //
    // Scan list count: single byte at image[0xB000], overlays scanList[0]+0x00.
    //   Written LAST by the CPS after all records (same pattern as zone count).
    //
    // Max: 32 scan lists.
    //
    //   and pre-populates 32 GB2312 template records (扫描列表2, 扫描列表3, …).
    //
    // Record layout (0x39 bytes):
    //   +0x00      = 0xFF from area fill (SL[0]+0x00 overwritten by global count).
    //   +0x01..+0x0B = name (10 ASCII chars + pad byte).
    //   +0x0E      = constant 0x06.
    //   +0x0F      = constant 0x00.
    //   +0x10..+0x15 = priority channel refs (CPS-maintained, participate in channel-remap).
    //   +0x16      = constant 0x0A.
    //   +0x17      = priority bitmap low (0x00=none, 0x80=has priority).
    //   +0x18      = priority bitmap high / per-member digital flags.
    //   +0x19..+0x38 = member list (16 × LE16, 1-based channel indices).
    //
    // ------------------------------------------------------------------------------
    public const int ScanListsStart = 0xB000;
    public const int ScanListsLength = 0x1000;
    public const int ScanListCapacity = 32;
    public const int ScanListRecordStride = 0x39;


    // ------------------------------------------------------------------------------
    // CHANNEL RECORD PAGING
    // ------------------------------------------------------------------------------
    //
    // Linear region: indices 0–84 (0x00–0x54) at i*0x30 + 0x3010.
    //   Index 84 record at 0x3FD0, last byte at 0x3FFF.
    //
    // Paged region: indices 85–1023 (0x55–0x3FF).
    //   Let j = i - 0x54 (= i - 84):
    //     page   = j / 0x55       (0x55 = 85 slots per page)
    //     slot   = j % 0x55
    //     offset = (page + 0xF) * 0x1000 + slot * 0x30
    //
    //   Slot 0 of each overflow page (e.g. 0xF000) holds the OEM phone-list record — it is
    //   never a channel record. Channel slots start at slot 1:
    //     i=85: j=1, page=0, slot=1 ? 0xF000 + 1*0x30 = 0xF030 (binary-confirmed: ch85 "70cm SSTV" 430.9 MHz)
    //     i=169: j=85, page=1, slot=0 ? 0x10000 + 0*0x30 = 0x10000 (phone list record of page 1)
    //     i=170: j=86, page=1, slot=1 ? 0x10000 + 1*0x30 = 0x10030 (first channel in page 1)
    //
    //   Pages beyond the independently verified range are treated as compatibility assumptions.
    //   This project writes up to 256 channels (indices 0–255), which occupies page 0 slots 1–84
    //   (indices 85–168) and page 1 slots 1–87 (indices 169–255), overwriting phone list data.
    //
    // Channel count: LE16 at file offset 0x3000.
    //
    // Name table: 0x4000 + i*11 for all indices (single contiguous region).
    // ------------------------------------------------------------------------------

    /// <summary>Number of channel records in the linear region (indices 0–84, i.e. i &lt; 0x55).</summary>
    public const int LinearChannelCapacity = 85;

    /// <summary>
    /// Image offset of the first overflow page.
    /// </summary>
    public const int ChannelOverflowPageStart = 0xF000;

    /// <summary>
    /// Number of record slots per overflow page (0x55 = 85).
    /// Slot 0 is the OEM phone-list record; channel slots are 1–84 (84 usable per page).
    /// </summary>
    public const int ChannelRecordsPerOverflowPage = 85;

    /// <summary>
    /// Number of overflow pages this project writes to support up to 256 channels.
    /// Page 0 (0xF000): slots 1–84 → indices 85–168.
    /// Page 1 (0x10000): slots 1–87 → indices 169–255.
    /// </summary>
    public const int ChannelOverflowPageCount = 2;

    /// <summary>Contiguous name table base for all channel indices (0x4000, stride 11).</summary>
    public const int ChannelNameTableBase = 0x4000;

    /// <summary>Maximum number of channel names the name table can hold (floor(4096/11)).</summary>
    public const int ChannelNameCapacity = 372;

    public const int ChannelCountOffset = 0x3000;

    /// <summary>
    /// Maximum zero-based channel index that can be stored in the native image.
    /// 85 linear + (3 × 84) paged = 337 record slots; capped at 256 channels (index 255).
    /// Name table supports 372 names in Table 1 alone.
    /// </summary>
    public const int MaxSupportedPatchedChannelIndex = 255;

    /// <summary>
    /// Returns the image file offset for a channel record at the given zero-based index.
    ///   Linear (i &lt; 0x55):  i * 0x30 + 0x3010
    ///   Paged  (i >= 0x55):  ((i - 0x54) / 0x55 + 0xF) * 0x1000 + ((i - 0x54) % 0x55) * 0x30
    /// Binary-confirmed: ch0 at 0x3010 (162.4 MHz BCD), ch85 at 0xF030 (430.950 MHz BCD).
    /// </summary>
    public static int GetChannelRecordOffset(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0x55)
        {
            return zeroBasedIndex * ChannelRecordStride + ChannelRecordStart;
        }

        var j = zeroBasedIndex - 0x54;
        return (j / 0x55 + 0xF) * SectorSize + (j % 0x55) * ChannelRecordStride;
    }

    /// <summary>
    /// Returns the image offset for a channel name at the given zero-based index.
    /// All names are in a single contiguous region at 0x4000, stride 11.
    /// </summary>
    public static int GetChannelNameOffset(int zeroBasedIndex)
    {
        return ChannelNameTableBase + (zeroBasedIndex * ChannelNameStride);
    }

    /// <summary>
    /// Writes the OEM phone-list record into slot 0 of an overflow page (0x30 bytes).
    /// Slot 0 of each overflow page is a phone-list record, not a channel record.
    /// Channel records occupy slots 1-84 (+0x30 to +0xFC0) within the page.
    /// </summary>
    public static void WriteOverflowPagePhoneListSlot(Span<byte> image, int pageIndex)
    {
        var offset = ChannelOverflowPageStart + (pageIndex * SectorSize);
        var slot = image.Slice(offset, ChannelRecordStride);

        slot.Fill(0xFF);
        slot[0] = 0x00;
        slot[1] = 0x01;
        slot[2] = 0x02;
        slot[3] = 0x03;
        slot[4] = 0x04;

        slot.Slice(0x11, 0x0F).Fill(0x00);

        ReadOnlySpan<byte> phoneListText = [0xB5, 0xE7, 0xBB, 0xB0, 0xC1, 0xD0, 0xB1, 0xED, 0x20, 0x32, 0x00, 0x00];
        phoneListText.CopyTo(slot.Slice(0x20, 12));

        slot.Slice(0x2C, 4).Fill(0x00);
    }
}
