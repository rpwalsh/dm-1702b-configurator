using Bao1702.Codeplug.Model;

namespace Bao1702.Desktop.Converters;

/// <summary>
/// Provides static arrays of codeplug enum values for XAML ComboBox <c>ItemsSource</c> bindings.
/// </summary>
public static class EnumSource
{
    public static PowerLevel[] PowerLevels { get; } = Enum.GetValues<PowerLevel>();
    public static ChannelBandwidth[] Bandwidths { get; } = Enum.GetValues<ChannelBandwidth>();
    public static AdmitCriteria[] AdmitCriteria { get; } = Enum.GetValues<AdmitCriteria>();
    public static ContactType[] ContactTypes { get; } = Enum.GetValues<ContactType>();
}
