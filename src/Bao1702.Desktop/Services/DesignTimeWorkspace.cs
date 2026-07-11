using Bao1702.Codeplug.Model;

namespace Bao1702.Desktop.Services;

/// <summary>
/// Provides a sample <see cref="CodeplugImage"/> loaded at startup to populate the editor
/// with representative data before a radio connection is established.
/// </summary>
public static class DesignTimeWorkspace
{
    public static CodeplugImage CreateSampleCodeplug() => CodeplugImage.CreateEmpty() with
    {
        Channels =
        [
            new AnalogChannel(1, "Local Analog", 145_500_000, 145_500_000, PowerLevel.High, ChannelBandwidth.Wide, AdmitCriteria.Always, ["Simplex"], ToneValue.Parse("67.0"), ToneValue.Parse("67.0"), CodeplugConfidence.Inferred),
            new DigitalChannel(2, "DMR TG91", 439_987_500, 430_987_500, PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.ColorCodeFree, ["Worldwide"], 1, 1, "TG91 Worldwide", "World Wide", CodeplugConfidence.Inferred),
        ],
        Zones =
        [
            new Zone("Simplex", ["Local Analog"], CodeplugConfidence.Inferred),
            new Zone("Worldwide", ["DMR TG91"], CodeplugConfidence.Inferred),
        ],
        Contacts =
        [
            new Contact("TG91 Worldwide", 91, ContactType.Group, CodeplugConfidence.Inferred),
        ],
    };
}
