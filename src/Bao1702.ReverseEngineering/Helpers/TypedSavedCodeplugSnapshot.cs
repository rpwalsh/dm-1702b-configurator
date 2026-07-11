using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

public enum ProvisionalChannelMode { Unknown, Analog, Digital }
public enum ProvisionalBandwidth { Unknown, Narrow, Wide }
public enum ProvisionalPowerLevel { Unknown, Low, Medium, High }

public sealed record TypedChannelSnapshot(
    int Index, string Name, double RxFrequencyMHz, double TxFrequencyMHz,
    string? Service, ProvisionalChannelMode Mode, ProvisionalBandwidth Bandwidth,
    ProvisionalPowerLevel Power, bool? RxOnly, bool? TalkAroundEnabled,
    int? StepFrequencyKHz, int RawFlagsLow, int RawFlagsHigh,
    string Evidence, CodeplugConfidence Confidence);

public sealed record TypedContactSnapshot(int Index, string Name, int? CallId, string? Label, CodeplugConfidence Confidence);
public sealed record TypedNamedListSnapshot(int Index, string Name, string? Label, CodeplugConfidence Confidence);
public sealed record TypedProfileSnapshot(int Index, string Name, string ProfileType, CodeplugConfidence Confidence);
public sealed record TypedSavedCodeplugSnapshot(
    IReadOnlyList<TypedChannelSnapshot> Channels,
    IReadOnlyList<TypedContactSnapshot> Contacts,
    IReadOnlyList<TypedNamedListSnapshot> NamedLists,
    IReadOnlyList<TypedProfileSnapshot> Profiles);

/// <summary>Builds snapshots exclusively from fields present in the supplied image.</summary>
public static class TypedSavedCodeplugSnapshotBuilder
{
    public static TypedSavedCodeplugSnapshot Build(SavedCodeplugDataDump dump)
    {
        ArgumentNullException.ThrowIfNull(dump);

        var structuredByName = dump.StructuredChannelCandidates
            .GroupBy(static item => item.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        var channels = dump.ObservedChannelRecords
            .Where(static record => !string.IsNullOrWhiteSpace(record.Name))
            .Select(record =>
            {
                structuredByName.TryGetValue(record.Name!, out var structured);
                var mode = (record.Byte8 & 0x40) != 0 ? ProvisionalChannelMode.Digital : ProvisionalChannelMode.Analog;
                var power = (record.Byte8 & 0x02) != 0 ? ProvisionalPowerLevel.High : ProvisionalPowerLevel.Low;
                var bandwidth = (record.Byte8 & 0x01) != 0 ? ProvisionalBandwidth.Wide : ProvisionalBandwidth.Narrow;
                var receiveOnly = (record.Byte8 & 0x20) != 0;
                return new TypedChannelSnapshot(
                    record.Index,
                    record.Name!,
                    structured?.RxFrequencyMHz ?? record.RxFrequencyMHz,
                    structured?.TxFrequencyMHz ?? record.TxFrequencyMHz,
                    structured?.Service,
                    mode,
                    bandwidth,
                    power,
                    receiveOnly,
                    null,
                    null,
                    record.Byte4,
                    record.Byte8,
                    string.Join(" ", new[] { record.Evidence, structured?.Evidence }.Where(static text => !string.IsNullOrWhiteSpace(text))),
                    structured?.Confidence ?? record.Confidence);
            })
            .OrderBy(static channel => channel.Index)
            .ToArray();

        return new TypedSavedCodeplugSnapshot(
            channels,
            dump.StructuredContactCandidates.Select(item => new TypedContactSnapshot(item.Index, item.Name, item.CallId, item.Label, item.Confidence)).ToArray(),
            dump.StructuredNamedListCandidates.Select(item => new TypedNamedListSnapshot(item.Index, item.Name, item.Label, item.Confidence)).ToArray(),
            dump.StructuredProfileCandidates.Select(item => new TypedProfileSnapshot(item.Index, item.Name, item.ProfileType, item.Confidence)).ToArray());
    }
}
