using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Reconstructs a <see cref="CodeplugImage"/> model from a saved codeplug data dump,
/// decoding channels, zones, scan lists, contacts, and configuration fields.
/// </summary>
public static class NativeCodeplugRebuilder
{
    public static CodeplugImage BuildCodeplugImage(SavedCodeplugDataDump dump)
    {
        ArgumentNullException.ThrowIfNull(dump);

        var channels = BuildChannels(dump.TypedSnapshot.Channels);
        var contacts = dump.TypedSnapshot.Contacts
            .Select(contact => new Contact(
                contact.Name,
                contact.CallId ?? 1,
                InferContactType(contact),
                contact.Confidence))
            .ToList();

        var rxGroups = dump.TypedSnapshot.NamedLists
            .Where(static entry => entry.Name.Contains('[', StringComparison.Ordinal))
            .Select(entry => new RxGroup(entry.Name, ResolveRxGroupContacts(entry, contacts), entry.Confidence))
            .ToList();

        var zones = BuildZones(dump.TypedSnapshot.Channels.Where(static channel => channel.Index <= Dm1702NativeImageAssumptions.MaxSupportedPatchedChannelIndex).ToArray());
        var scanLists = zones
            .Select(zone => new ScanList(zone.Name, zone.ChannelNames, zone.Confidence))
            .ToArray();

        var unknownSegments = new List<UnknownCodeplugSegment>
        {
            new("PreservedSavedData", 0, File.ReadAllBytes(dump.FilePath), CodeplugConfidence.Preserved)
        };

        return CodeplugImage.CreateEmpty() with
        {
            Channels = channels,
            Contacts = contacts,
            RxGroups = rxGroups,
            Zones = zones,
            ScanLists = scanLists,
            UnknownSegments = unknownSegments,
            PreservedRawImage = File.ReadAllBytes(dump.FilePath),
        };
    }

    private static IReadOnlyList<Channel> BuildChannels(IReadOnlyList<TypedChannelSnapshot> typedChannels)
    {
        var result = new List<Channel>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in typedChannels.OrderBy(static channel => channel.Index))
        {
            if (!usedNames.Add(channel.Name))
            {
                continue;
            }

            var zoneNames = InferZoneNames(channel);
            if (channel.Mode == ProvisionalChannelMode.Digital)
            {
                result.Add(new DigitalChannel(
                    channel.Index + 1,
                    channel.Name,
                    ToHz(channel.RxFrequencyMHz),
                    ToHz(channel.TxFrequencyMHz),
                    ToPowerLevel(channel.Power),
                    ToBandwidth(channel.Bandwidth),
                    AdmitCriteria.Always,
                    zoneNames,
                    InferColorCode(channel),
                    InferTimeSlot(channel),
                    InferContactName(channel),
                    InferRxGroupName(channel),
                    channel.Confidence) { ReceiveOnly = channel.RxOnly == true });
            }
            else
            {
                result.Add(new AnalogChannel(
                    channel.Index + 1,
                    channel.Name,
                    ToHz(channel.RxFrequencyMHz),
                    ToHz(channel.TxFrequencyMHz),
                    ToPowerLevel(channel.Power),
                    ToBandwidth(channel.Bandwidth),
                    AdmitCriteria.Always,
                    zoneNames,
                    ToneValue.Parse(string.Empty),
                    ToneValue.Parse(string.Empty),
                    channel.Confidence) { ReceiveOnly = channel.RxOnly == true });
            }
        }

        return result;
    }

    private static IReadOnlyList<Zone> BuildZones(IReadOnlyList<TypedChannelSnapshot> channels)
    {
        var groups = channels
            .GroupBy(channel => channel.Service ?? InferFallbackZone(channel), StringComparer.OrdinalIgnoreCase)
            .Select(group => new Zone(
                group.Key,
                group.OrderBy(static channel => channel.Index).Select(static channel => channel.Name).ToArray(),
                group.All(static channel => channel.Confidence == CodeplugConfidence.Confirmed) ? CodeplugConfidence.Confirmed : CodeplugConfidence.Inferred))
            .OrderBy(static zone => zone.Name, StringComparer.Ordinal)
            .ToArray();
        return groups;
    }

    private static IReadOnlyList<string> ResolveRxGroupContacts(TypedNamedListSnapshot entry, IReadOnlyList<Contact> contacts)
    {
        if (entry.Label is null)
        {
            return [];
        }

        var matches = contacts
            .Where(contact => contact.Name.Contains(entry.Label, StringComparison.OrdinalIgnoreCase)
                || string.Equals(contact.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
            .Select(static contact => contact.Name)
            .Take(16)
            .ToArray();
        return matches.Length == 0 ? [entry.Name] : matches;
    }

    private static ContactType InferContactType(TypedContactSnapshot contact)
    {
        if (contact.Label is not null)
        {
            return ContactType.Group;
        }

        return ContactType.Private;
    }

    private static IReadOnlyList<string> InferZoneNames(TypedChannelSnapshot channel)
    {
        return [channel.Service ?? InferFallbackZone(channel)];
    }

    private static string InferFallbackZone(TypedChannelSnapshot channel)
    {
        return "Imported";
    }

    private static string? InferContactName(TypedChannelSnapshot channel)
    {
        if (!channel.Name.EndsWith("u", StringComparison.OrdinalIgnoreCase)
            && !channel.Name.EndsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return null;
    }

    private static string? InferRxGroupName(TypedChannelSnapshot channel)
    {
        if (!channel.Name.EndsWith("u", StringComparison.OrdinalIgnoreCase)
            && !channel.Name.EndsWith("v", StringComparison.OrdinalIgnoreCase)
            && !channel.Name.Contains("vhf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return null;
    }

    private static int InferColorCode(TypedChannelSnapshot channel)
    {
        return 1;
    }

    private static int InferTimeSlot(TypedChannelSnapshot channel)
        => 1;

    private static long ToHz(double frequencyMHz)
        => (long)Math.Round(frequencyMHz * 1_000_000d, MidpointRounding.AwayFromZero);

    private static PowerLevel ToPowerLevel(ProvisionalPowerLevel power)
        => power switch
        {
            ProvisionalPowerLevel.Low => PowerLevel.Low,
            ProvisionalPowerLevel.Medium => PowerLevel.Medium,
            _ => PowerLevel.High,
        };

    private static ChannelBandwidth ToBandwidth(ProvisionalBandwidth bandwidth)
        => bandwidth == ProvisionalBandwidth.Wide ? ChannelBandwidth.Wide : ChannelBandwidth.Narrow;
}
