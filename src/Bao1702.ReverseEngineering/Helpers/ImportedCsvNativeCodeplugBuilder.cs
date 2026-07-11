using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Builds a native codeplug image by merging a CSV-imported <see cref="CodeplugImage"/>
/// with a baseline OEM dump, patching channel records and names into the binary layout.
/// </summary>
public static class ImportedCsvNativeCodeplugBuilder
{
    public static RecoveredNativeCodeplug Build(CodeplugImage importedImage, SavedCodeplugDataDump baseDump)
    {
        ArgumentNullException.ThrowIfNull(importedImage);
        ArgumentNullException.ThrowIfNull(baseDump);

        var baseRecovered = NativeDataPatcher.BuildRecoveredNativeCodeplug(baseDump);
        var importedByIndex = importedImage.Channels
            .ToDictionary(static channel => channel.Index - 1);

        var mergedChannels = new List<RecoveredNativeChannel>();
        foreach (var existing in baseRecovered.Channels.OrderBy(static channel => channel.Index))
        {
            if (!importedByIndex.TryGetValue(existing.Index, out var imported))
            {
                mergedChannels.Add(existing);
                continue;
            }

            mergedChannels.Add(Merge(existing, imported));
        }

        return baseRecovered with
        {
            Channels = mergedChannels,
            Contacts = importedImage.Contacts,
            RxGroups = importedImage.RxGroups,
            Zones = importedImage.Zones,
            RawImage = File.ReadAllBytes(baseDump.FilePath),
        };
    }

    private static RecoveredNativeChannel Merge(RecoveredNativeChannel existing, Channel imported)
    {
        return imported switch
        {
            AnalogChannel analog => existing with
            {
                Name = analog.Name,
                RxFrequencyMHz = analog.RxFrequencyHz / 1_000_000d,
                TxFrequencyMHz = analog.TxFrequencyHz / 1_000_000d,
                Kind = ChannelKind.Analog,
                Power = analog.Power,
                Bandwidth = analog.Bandwidth,
            },
            DigitalChannel digital => existing with
            {
                Name = digital.Name,
                RxFrequencyMHz = digital.RxFrequencyHz / 1_000_000d,
                TxFrequencyMHz = digital.TxFrequencyHz / 1_000_000d,
                Kind = ChannelKind.Digital,
                Power = digital.Power,
                Bandwidth = digital.Bandwidth,
                ColorCode = digital.ColorCode,
                TimeSlot = digital.TimeSlot,
                ContactName = digital.ContactName,
                RxGroupName = digital.RxGroupName,
            },
            _ => existing,
        };
    }
}
