using System.Buffers.Binary;
using System.Text;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Stride 0x112 (274 bytes), max 250 zones, max 64 members per zone.
///
/// Linear region (zones 0–13, file base 0x6000):
///   +0x00..+0x0F = reserved (zeros for zones 1-13; zone 0 byte +0x00 is the global zone count).
///   +0x10 = name (16 bytes ASCII, NUL-padded).
///   +0x21 = member list (64 × LE16, 1-based channel indices).
///   +0xA1 = echo member count.  +0xA2 = echo member list (copy of member list).
///
/// Paged region (zones 14+, page base 0x2B000, 14 zones per page):
///   +0x00 = name (16 bytes ASCII, NUL-padded).
///   +0x10 = member count.  +0x11 = member list (64 × LE16).
///   +0x91 = echo count.    +0x92 = echo members.
///
///   zone[14] "25k Repeater 2/2" at paged offset 0x2B000.
/// </summary>
public static class Dm1702NativeZoneSerializer
{
    private const int RecordStride = Dm1702NativeImageAssumptions.ZoneRecordStride; // 0x112
    private const int NameLength = 0x10;
    private const int MaxMembers = Dm1702NativeImageAssumptions.MaxMembersPerZone; // 64

    // Linear record offsets (zones 0–13).
    // +0x00..+0x0F is reserved padding (zeros). The firmware does NOT read +0x00 as member count.
    private const int LinearNameOffset = 0x10;
    private const int LinearMemberCountOffset = 0x20;
    private const int LinearMemberListOffset = 0x21;
    private const int LinearEchoCountOffset = 0xA1;
    private const int LinearEchoListOffset = 0xA2;

    // Paged record offsets (zones 14+)
    private const int PagedNameOffset = 0x00;
    private const int PagedMemberCountOffset = 0x10;
    private const int PagedMemberListOffset = 0x11;
    private const int PagedEchoCountOffset = 0x91;
    private const int PagedEchoListOffset = 0x92;

    public static void Write(Span<byte> image, IReadOnlyList<Zone> zones, IReadOnlyDictionary<string, int> channelIndexByName)
    {
        var count = Math.Min(zones.Count, Dm1702NativeImageAssumptions.ZoneRecordCapacity);

        for (var index = 0; index < count; index++)
        {
            var zone = zones[index];
            var recordOffset = Dm1702NativeImageAssumptions.GetZoneRecordOffset(index);

            if (recordOffset + RecordStride > image.Length)
            {
                throw new InvalidOperationException($"Zone record {index} at offset 0x{recordOffset:X5} exceeds image bounds.");
            }

            var record = image.Slice(recordOffset, RecordStride);
            record.Fill(0x00);

            var members = zone.ChannelNames
                .Select(name => channelIndexByName.TryGetValue(name, out var channelIndex) ? channelIndex : (int?)null)
                .Where(static value => value.HasValue)
                .Select(static value => value!.Value)
                .Distinct()
                .Take(MaxMembers)
                .ToArray();

            var isLinear = index < Dm1702NativeImageAssumptions.LinearZoneCapacity;

            if (isLinear)
            {
                // +0x00..+0x0F remains zero (from Fill) — OEM-confirmed: zones 1-13 all zeros here.
                // The firmware reads member count from +0x20, NOT +0x00.
                WriteAscii(record.Slice(LinearNameOffset, NameLength), zone.Name, NameLength);
                record[LinearMemberCountOffset] = (byte)members.Length;
                WriteMemberList(record.Slice(LinearMemberListOffset), members);
                var echoSpan = record.Slice(LinearEchoListOffset);
                var echoMax = Math.Min(members.Length, echoSpan.Length / 2);
                record[LinearEchoCountOffset] = (byte)echoMax;
                WriteMemberList(echoSpan, members[..echoMax]);
            }
            else
            {
                WriteAscii(record.Slice(PagedNameOffset, NameLength), zone.Name, NameLength);
                record[PagedMemberCountOffset] = (byte)members.Length;
                WriteMemberList(record.Slice(PagedMemberListOffset), members);
                var echoSpan = record.Slice(PagedEchoListOffset);
                var echoMax = Math.Min(members.Length, echoSpan.Length / 2);
                record[PagedEchoCountOffset] = (byte)echoMax;
                WriteMemberList(echoSpan, members[..echoMax]);
            }
        }

        // Write zone count LAST — byte at 0x6000 overlaps zone[0]'s record header,
        // so it must be written after zone[0]'s record to avoid being overwritten.
        image[Dm1702NativeImageAssumptions.ZoneDataStart] = (byte)count;
    }

    private static void WriteMemberList(Span<byte> destination, int[] members)
    {
        var maxFit = destination.Length / 2;
        var count = Math.Min(members.Length, maxFit);
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(i * 2, 2), (ushort)members[i]);
        }
    }

    private static void WriteAscii(Span<byte> destination, string value, int maxLength)
    {
        destination.Fill(0x00);
        var bytes = Encoding.ASCII.GetBytes(value.Length >= maxLength ? value[..(maxLength - 1)] : value);
        bytes.CopyTo(destination);
    }
}
