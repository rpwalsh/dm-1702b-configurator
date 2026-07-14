using System.Buffers.Binary;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Observed split-region index formula:
///   groups 0-13:  image[index * 0x6D + 0x8010]
///   groups 14-31: image[index * 0x6D + 0xBA0A]
///
///   +0x00        (1 byte)   = 0x00 (zero in all user-written records)
///   +0x01..+0x0B (11 bytes) = Name (ASCII, 0x00 padded)
///   +0x0C..+0x0E (3 bytes)  = TalkGroup ID as LE16 + 0x00 pad
///   +0x0F..+0x11 (3 bytes)  = Reserved zeros
///   +0x12..+0x6C (91 bytes) = Member 1-based contact indices (LE16 + 0x00 pad, stride 3, max 30)
/// </summary>
public static class Dm1702NativeRxGroupSerializer
{
    private const int RecordStride = Dm1702NativeImageAssumptions.RxGroupRecordStride; // 0x6D
    private const int NameOffset = 0x01;        // OEM-confirmed: name starts at +0x01
    private const int NameLength = 11;          // OEM-confirmed: 11 bytes, +0x01..+0x0B
    private const int TalkGroupIdOffset = 0x0C; // OEM-confirmed: TalkGroup ID LE16 at +0x0C (sarah [TEST-GROUP-001]: 8B 03 = 907)
    private const int MemberListOffset = 0x12;  // OEM-confirmed: first 1-based contact index at +0x12 (sarah [MO LAKES]: 90 01 = 400)
    private const int MemberEntrySize = 3;      // LE16 + 0x00 pad; stride-3 verified
    private const int MaxMembersPerGroup = 30;  // 30 × 3 = 90 bytes, +0x12..+0x6B

    public static void Write(Span<byte> image, IReadOnlyList<RxGroup> rxGroups, IReadOnlyList<Contact> contacts)
    {
        // Zero-fill region 1 (groups 0-13 starting at 0x8010)
        var region1Length = Dm1702NativeImageAssumptions.RxGroupRegion1Capacity * RecordStride;
        image.Slice(Dm1702NativeImageAssumptions.RxGroupRegion1Start, region1Length).Fill(0x00);

        // Zero-fill region 2 (groups 14-31 using absolute formula base 0xBA0A)
        var region2Start = Dm1702NativeImageAssumptions.RxGroupRegion1Capacity * RecordStride
                         + Dm1702NativeImageAssumptions.RxGroupRegion2FormulaBase;
        var region2Length = Dm1702NativeImageAssumptions.RxGroupRegion2Capacity * RecordStride;
        image.Slice(region2Start, region2Length).Fill(0x00);

        var count = Math.Min(rxGroups.Count, Dm1702NativeImageAssumptions.RxGroupCapacity);
        for (var index = 0; index < count; index++)
        {
            var imageOffset = GetRecordImageOffset(index);
            var record = image.Slice(imageOffset, RecordStride);
            record.Fill(0x00);

            var group = rxGroups[index];

            // +0x01..+0x0B: name (11 bytes ASCII, 0x00 padded) — OEM-confirmed offset
            Dm1702NativeTextEncoding.Write(record.Slice(NameOffset, NameLength), group.Name);

            // +0x0C..+0x0E: TalkGroup ID as LE16 + 0x00 pad
            // Use model TalkGroupId if set; otherwise derive from first member's call ID.
            var talkGroupId = group.TalkGroupId != 0
                ? group.TalkGroupId
                : contacts.FirstOrDefault(c => group.ContactNames.Count > 0 &&
                    string.Equals(c.Name, group.ContactNames[0], StringComparison.Ordinal))?.CallId ?? 0;
            BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(TalkGroupIdOffset, 2), (ushort)talkGroupId);
            record[TalkGroupIdOffset + 2] = 0x00;

            // +0x12..+0x6B: member 1-based contact indices (LE16 + 0x00 pad, stride 3, max 30)
            var members = group.ContactNames
                .Select(memberName => Dm1702NativeContactSerializer.FindContactIndex(contacts, memberName))
                .Where(static idx => idx.HasValue)
                .Select(static idx => idx!.Value)
                .Take(MaxMembersPerGroup)
                .ToArray();

            for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
            {
                var entryOffset = MemberListOffset + memberIndex * MemberEntrySize;
                BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(entryOffset, 2), (ushort)members[memberIndex]);
                record[entryOffset + 2] = 0x00;
            }
        }
    }

    /// <summary>
    /// Returns the absolute image offset for a given RxGroup index (0-31).
    ///   Groups 0-13:  image[index * 0x6D + 0x8010]
    ///   Groups 14-31: image[index * 0x6D + 0xBA0A]
    /// </summary>
    internal static int GetRecordImageOffset(int groupIndex)
    {
        if (groupIndex < Dm1702NativeImageAssumptions.RxGroupRegion1Capacity)
        {
            return groupIndex * RecordStride + Dm1702NativeImageAssumptions.RxGroupRegion1Start;
        }

        return groupIndex * RecordStride + Dm1702NativeImageAssumptions.RxGroupRegion2FormulaBase;
    }
}
