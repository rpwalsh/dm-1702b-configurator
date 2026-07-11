using System.Buffers.Binary;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
///
/// Global count byte: image[0xB000] = number of populated scan lists.
///   This byte overlaps SL[0]'s record +0x00, so it is written LAST (same pattern as zone count).
///
/// Per-record layout (0x39 = 57 bytes, area fill = 0xFF):
///   +0x00 = 0xFF (from fill; SL[0]'s +0x00 is overwritten by the global count byte).
///           The firmware does NOT read +0x00 as member count.
///   +0x01..0x0B = name (11 bytes: up to 10 ASCII + NUL/0xFF pad).
///   +0x0E = 0x06   +0x0F = 0x00   +0x10 = 0x01   +0x11..+0x15 = 0x00   +0x16 = 0x0A
///   +0x17 = priority bitmap low (0x00=none, 0x80=has priority). +0x18 = priority bitmap high.
///   +0x19..0x38 = member list (16 × LE16, 1-based channel indices).
///
/// Record stride 0x39 was independently verified through controlled configuration differences.
/// </summary>
public static class Dm1702NativeScanListSerializer
{
    private const int RecordStride = Dm1702NativeImageAssumptions.ScanListRecordStride; // 0x39
    private const int NameOffset = 0x01;
    private const int NameLength = 0x0B;
    private const int ChannelCountOffset = 0x0C;
    private const int MemberListOffset = 0x19;
    private const int MaxMembersPerScanList = 16;  // (0x39 - 0x19) / 2 = 16 LE16 slots

    public static void Write(Span<byte> image, IReadOnlyList<ScanList> scanLists, IReadOnlyDictionary<string, int> channelIndexByName)
    {
        var area = image.Slice(Dm1702NativeImageAssumptions.ScanListsStart, Dm1702NativeImageAssumptions.ScanListsLength);
        area.Fill(0xFF);

        var count = Math.Min(scanLists.Count, Dm1702NativeImageAssumptions.ScanListCapacity);
        for (var index = 0; index < count; index++)
        {
            var list = scanLists[index];
            var recordOffset = index * RecordStride;
            if (recordOffset + RecordStride > area.Length)
            {
                throw new InvalidOperationException($"Scan list record {index} exceeds the assumed scan list storage region.");
            }

            var record = area.Slice(recordOffset, RecordStride);
            record.Fill(0xFF);

            var members = list.ChannelNames
                .Select(name => channelIndexByName.TryGetValue(name, out var channelIndex) ? channelIndex : (int?)null)
                .Where(static value => value.HasValue)
                .Select(static value => value!.Value)
                .Distinct()
                .Take(MaxMembersPerScanList)
                .ToArray();

            // record[0] is left as 0xFF from Fill — the firmware does NOT read +0x00 as member count.
            // SL[0]'s +0x00 will be overwritten by the global scan list count after the loop.
            Dm1702NativeTextEncoding.Write(record.Slice(NameOffset, NameLength), list.Name);
            record[0x0C] = (byte)members.Length;
            record[0x0D] = 0x03;
            record[0x0E] = 0x06;
            record[0x0F] = 0x00; // Independently observed reserved value.
            record[0x10] = 0x01;
            record[0x11] = 0x00; // Independently observed reserved value.
            record[0x12] = 0x00;
            record[0x13] = 0x00;
            record[0x14] = 0x00;
            record[0x15] = 0x00;
            record[0x16] = 0x0A;
            record[0x17] = 0x00;
            record[0x18] = 0x00;

            for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(MemberListOffset + (memberIndex * 2), 2), (ushort)members[memberIndex]);
            }
        }

        // Write scan list count LAST — byte at 0xB000 overlaps SL[0]'s record +0x00,
        // so it must be written after SL[0]'s record to avoid being overwritten by Fill.
        area[0] = (byte)count;
    }
}
