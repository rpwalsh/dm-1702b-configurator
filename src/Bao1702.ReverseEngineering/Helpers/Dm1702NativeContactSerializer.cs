using System.Buffers.Binary;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Serializes contact records, metadata, bitmap, and sorted indexes into the
/// DM-1702 native image paged contact region (base offset 0x1F000, 170 records per 4K page).
/// </summary>
public static class Dm1702NativeContactSerializer
{
    private const int PrivateType = 0x3;
    private const int GroupType = 0x4;
    private const int AllCallType = 0x5;

    /// <summary>
    /// 170 records of 0x18 bytes fit in a 4K page (170 × 24 = 4080), leaving 16 bytes padding.
    /// </summary>
    internal const int ContactRecordsPerPage = Dm1702NativeImageAssumptions.SectorSize / Dm1702NativeImageAssumptions.ContactRecordLength;

    public static void Write(Span<byte> image, IReadOnlyList<Contact> contacts)
    {
        if (image.Length < Dm1702NativeImageAssumptions.ImageLength)
        {
            throw new ArgumentException("Image buffer is too short.", nameof(image));
        }

        var normalized = NormalizeContacts(contacts).Take(Dm1702NativeImageAssumptions.ContactDataCapacity).ToArray();
        WriteContactMeta(image.Slice(Dm1702NativeImageAssumptions.ContactMetaStart, Dm1702NativeImageAssumptions.ContactMetaLength), normalized);
        WriteContactData(image.Slice(Dm1702NativeImageAssumptions.ContactDataStart), normalized);
    }

    public static int? FindContactIndex(IReadOnlyList<Contact> contacts, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = NormalizeContacts(contacts).ToArray();
        for (var index = 0; index < normalized.Length; index++)
        {
            if (string.Equals(normalized[index].Name, name, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return null;
    }

    private static IReadOnlyList<Contact> NormalizeContacts(IReadOnlyList<Contact> contacts)
    {
        var result = new List<Contact>();
        var byId = new HashSet<(int, ContactType)>();
        var byName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var contact in contacts.OrderBy(static contact => contact.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static contact => contact.CallId))
        {
            var type = contact.ContactType;
            var key = (contact.CallId, type);
            if (!byId.Add(key))
            {
                continue;
            }

            var name = contact.Name;
            if (!byName.Add(name))
            {
                var prefix = name.Length > 13 ? name[..13] : name;
                var suffix = 1;
                while (!byName.Add($"{prefix}-{suffix}"))
                {
                    suffix++;
                }

                name = $"{prefix}-{suffix}";
            }

            result.Add(contact with { Name = name });
        }

        return result;
    }

    private static void WriteContactMeta(Span<byte> destination, IReadOnlyList<Contact> contacts)
    {
        destination.Fill(0xFF);

        var groupCount = contacts.Count(static contact => contact.ContactType == ContactType.Group);
        var allCallPresent = contacts.Any(static contact => contact.ContactType == ContactType.AllCall) ? 1 : 0;

        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)contacts.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)groupCount);
        destination[4] = (byte)allCallPresent;

        // Evidence: baseline has factory Chinese contact at Rec0 (not bitmap-tracked) + "Call 1" at Rec1 ? bitmap[0]=0xFD (bit1 cleared).
        // Our serializer writes user contacts starting at Rec0, so bitmap bit index = record index (0-based).
        var bitmap = destination.Slice(0x10, 0x64);
        bitmap.Fill(0xFF);
        for (var index = 0; index < contacts.Count; index++)
        {
            var byteIndex = index / 8;
            var bitIndex = index % 8;
            bitmap[byteIndex] &= (byte)~(1 << bitIndex);
        }

        // This reserved region is zero-filled deterministically. Its semantics are unverified.
        destination.Slice(0x74, 0x8C).Clear();

        WriteSortedIndex(destination.Slice(0x100, 0x640), contacts.OrderBy(static contact => contact.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static contact => contact.CallId).ToArray(), contacts);
        WriteSortedIndex(destination.Slice(0x740, 0x640), contacts.OrderBy(static contact => contact.CallId).ThenBy(static contact => contact.Name, StringComparer.OrdinalIgnoreCase).ToArray(), contacts);
    }

    private static void WriteSortedIndex(Span<byte> destination, IReadOnlyList<Contact> sorted, IReadOnlyList<Contact> source)
    {
        destination.Fill(0x00);
        var slot = 0;
        foreach (var contact in sorted)
        {
            var sourceIndex = -1;
            for (var i = 0; i < source.Count; i++)
            {
                if (EqualityComparer<Contact>.Default.Equals(source[i], contact))
                {
                    sourceIndex = i;
                    break;
                }
            }

            if (sourceIndex < 0 || slot + 1 >= destination.Length)
            {
                continue;
            }

            var typeNibble = EncodeContactType(contact.ContactType) << 12;
            var item = (ushort)((sourceIndex + 1) | typeNibble);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(slot, 2), item);
            slot += 2;
        }

        while (slot + 1 < destination.Length)
        {
            destination[slot++] = 0x00;
            destination[slot++] = 0xF0;
        }
    }

    /// <summary>
    /// Contact data is paged: 170 records per 4K page with 16-byte padding at each page end.
    /// Pages start at <see cref="Dm1702NativeImageAssumptions.ContactDataStart"/> (0x1F000).
    /// </summary>
    internal static int GetContactRecordImageOffset(int contactIndex)
    {
        var page = contactIndex / ContactRecordsPerPage;
        var slot = contactIndex % ContactRecordsPerPage;
        return Dm1702NativeImageAssumptions.ContactDataStart
             + (page * Dm1702NativeImageAssumptions.SectorSize)
             + (slot * Dm1702NativeImageAssumptions.ContactRecordLength);
    }

    private static void WriteContactData(Span<byte> destination, IReadOnlyList<Contact> contacts)
    {
        // Fill only the record slots on each page with 0xFF, leaving the 16-byte page-end
        // padding (which contains sector markers) untouched.
        var totalPages = (Dm1702NativeImageAssumptions.ContactDataCapacity + ContactRecordsPerPage - 1) / ContactRecordsPerPage;
        for (var page = 0; page < totalPages; page++)
        {
            var pageLocalBase = page * Dm1702NativeImageAssumptions.SectorSize;
            var recordsOnPage = Math.Min(ContactRecordsPerPage, Dm1702NativeImageAssumptions.ContactDataCapacity - page * ContactRecordsPerPage);
            var recordAreaLength = recordsOnPage * Dm1702NativeImageAssumptions.ContactRecordLength;
            destination.Slice(pageLocalBase, recordAreaLength).Fill(0xFF);
        }

        for (var index = 0; index < contacts.Count; index++)
        {
            var imageOffset = GetContactRecordImageOffset(index);
            var localOffset = imageOffset - Dm1702NativeImageAssumptions.ContactDataStart;
            var record = destination.Slice(localOffset, Dm1702NativeImageAssumptions.ContactRecordLength);
            record.Fill(0xFF);

            // [18]=0xFF separator, [19..21]=callId (LE24), [22]=type byte, [23]=0xFF trailer.
            // LE32((type<<24)|callId) at [19..22] produces identical bytes to LE24+type because
            // LE32(0xTT00XXYY) stores as [YY XX 00 TT].
            Dm1702NativeTextEncoding.Write(record[2..18], contacts[index].Name);
            var combined = ((uint)EncodeContactType(contacts[index].ContactType) << 24) | (uint)contacts[index].CallId;
            BinaryPrimitives.WriteUInt32LittleEndian(record[19..23], combined);
        }
    }

    private static int EncodeContactType(ContactType type)
        => type switch
        {
            ContactType.Private => PrivateType,
            ContactType.Group => GroupType,
            ContactType.AllCall => AllCallType,
            _ => PrivateType,
        };

}
