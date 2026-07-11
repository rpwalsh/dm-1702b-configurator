namespace Bao1702.Codeplug.Model;

/// <summary>A channel record decoded from the native binary image with full semantic field mapping.</summary>
public sealed record RecoveredNativeChannel(
    int Index,
    string Name,
    double RxFrequencyMHz,
    double TxFrequencyMHz,
    ChannelKind Kind,
    PowerLevel Power,
    ChannelBandwidth Bandwidth,
    bool? RxOnly,
    bool? TalkAround,
    int? ColorCode,
    int? TimeSlot,
    string? ContactName,
    string? RxGroupName,
    string? GpsSystemName,
    int? NameOffset,
    int? RecordOffset,
    CodeplugConfidence Confidence);

public sealed record RecoveredNativeCodeplug(
    IReadOnlyList<RecoveredNativeChannel> Channels,
    IReadOnlyList<Contact> Contacts,
    IReadOnlyList<RxGroup> RxGroups,
    IReadOnlyList<Zone> Zones,
    byte[] RawImage,
    CodeplugConfidence Confidence);
