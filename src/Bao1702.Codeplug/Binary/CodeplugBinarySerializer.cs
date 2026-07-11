using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bao1702.Codeplug.Model;

namespace Bao1702.Codeplug.Binary;

/// <summary>
/// Deterministic provisional binary serializer used for offline tooling and test fixtures.
/// This is not yet the verified native on-radio codeplug layout.
/// </summary>
public static class CodeplugBinarySerializer
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("B1702CP1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static byte[] Serialize(CodeplugImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var dto = CodeplugDto.FromModel(image);
        var payload = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
        var checksum = SHA256.HashData(payload);
        using var stream = new MemoryStream();
        stream.Write(Magic);
        stream.Write(BitConverter.GetBytes(payload.Length));
        stream.Write(checksum, 0, 16);
        stream.Write(payload, 0, payload.Length);
        return stream.ToArray();
    }

    public static CodeplugImage Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < Magic.Length + 4 + 16)
        {
            throw new InvalidDataException("Codeplug image is too short to contain the provisional header.");
        }

        if (!buffer[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Codeplug image does not match the provisional Bao1702 research container signature.");
        }

        var payloadLength = BitConverter.ToInt32(buffer.Slice(Magic.Length, 4));
        var expectedTotalLength = Magic.Length + 4 + 16 + payloadLength;
        if (buffer.Length != expectedTotalLength)
        {
            throw new InvalidDataException($"Codeplug image length mismatch. Expected {expectedTotalLength}, got {buffer.Length}.");
        }

        var storedChecksum = buffer.Slice(Magic.Length + 4, 16);
        var payload = buffer[(Magic.Length + 4 + 16)..].ToArray();
        var actualChecksum = SHA256.HashData(payload);
        if (!storedChecksum.SequenceEqual(actualChecksum.AsSpan(0, 16)))
        {
            throw new InvalidDataException("Codeplug image checksum mismatch.");
        }

        var dto = JsonSerializer.Deserialize<CodeplugDto>(payload, JsonOptions)
            ?? throw new InvalidDataException("Codeplug image payload could not be deserialized.");
        return dto.ToModel(buffer.ToArray());
    }

    private sealed class CodeplugDto
    {
        public GeneralSettings GeneralSettings { get; set; } = CodeplugImage.CreateEmpty().GeneralSettings;
        public DisplaySettings DisplaySettings { get; set; } = CodeplugImage.CreateEmpty().DisplaySettings;
        public PowerSettings PowerSettings { get; set; } = CodeplugImage.CreateEmpty().PowerSettings;
        public SquelchSettings SquelchSettings { get; set; } = CodeplugImage.CreateEmpty().SquelchSettings;
        public ParameterSettings ParameterSettings { get; set; } = CodeplugImage.CreateEmpty().ParameterSettings;
        public StartupScreen StartupScreen { get; set; } = CodeplugImage.CreateEmpty().StartupScreen;
        public RadioIdentitySettings RadioIdentity { get; set; } = CodeplugImage.CreateEmpty().RadioIdentity;
        public ButtonConfig ButtonConfig { get; set; } = CodeplugImage.CreateEmpty().ButtonConfig;
        public KeyAssignmentTable KeyAssignments { get; set; } = CodeplugImage.CreateEmpty().KeyAssignments;
        public DtmfConfig DtmfConfig { get; set; } = CodeplugImage.CreateEmpty().DtmfConfig;
        public PrivacyConfig PrivacyConfig { get; set; } = CodeplugImage.CreateEmpty().PrivacyConfig;
        public List<PrivacyEntry> PrivacyEntries { get; set; } = [];
        public List<EmergencySystem> EmergencySystems { get; set; } = [];
        public EmergencyConfig EmergencyConfig { get; set; } = CodeplugImage.CreateEmpty().EmergencyConfig;
        public LoneWorkerConfig LoneWorkerConfig { get; set; } = CodeplugImage.CreateEmpty().LoneWorkerConfig;
        public List<QuickTextMessage> QuickTextMessages { get; set; } = [];
        public List<ChannelDto> Channels { get; set; } = [];
        public List<Zone> Zones { get; set; } = [];
        public List<Contact> Contacts { get; set; } = [];
        public List<GroupList> GroupLists { get; set; } = [];
        public List<ScanList> ScanLists { get; set; } = [];
        public List<RxGroup> RxGroups { get; set; } = [];
        public List<UnknownCodeplugSegment> UnknownSegments { get; set; } = [];

        public static CodeplugDto FromModel(CodeplugImage image) => new()
        {
            GeneralSettings = image.GeneralSettings,
            DisplaySettings = image.DisplaySettings,
            PowerSettings = image.PowerSettings,
            SquelchSettings = image.SquelchSettings,
            ParameterSettings = image.ParameterSettings,
            StartupScreen = image.StartupScreen,
            RadioIdentity = image.RadioIdentity,
            ButtonConfig = image.ButtonConfig,
            KeyAssignments = image.KeyAssignments,
            DtmfConfig = image.DtmfConfig,
            PrivacyConfig = image.PrivacyConfig,
            PrivacyEntries = image.PrivacyEntries.ToList(),
            EmergencySystems = image.EmergencySystems.ToList(),
            EmergencyConfig = image.EmergencyConfig,
            LoneWorkerConfig = image.LoneWorkerConfig,
            QuickTextMessages = image.QuickTextMessages.ToList(),
            Channels = image.Channels.Select(ChannelDto.FromModel).ToList(),
            Zones = image.Zones.ToList(),
            Contacts = image.Contacts.ToList(),
            GroupLists = image.GroupLists.ToList(),
            ScanLists = image.ScanLists.ToList(),
            RxGroups = image.RxGroups.ToList(),
            UnknownSegments = image.UnknownSegments.ToList(),
        };

        public CodeplugImage ToModel(byte[] rawImage) => new(
            GeneralSettings,
            DisplaySettings,
            PowerSettings,
            SquelchSettings,
            ParameterSettings,
            StartupScreen,
            RadioIdentity,
            ButtonConfig,
            KeyAssignments,
            DtmfConfig,
            PrivacyConfig,
            PrivacyEntries,
            EmergencySystems,
            EmergencyConfig,
            LoneWorkerConfig,
            QuickTextMessages,
            Channels.Select(channel => channel.ToModel()).ToList(),
            Zones,
            Contacts,
            GroupLists,
            ScanLists,
            RxGroups,
            UnknownSegments,
            rawImage);
    }

    private sealed class ChannelDto
    {
        public ChannelKind Kind { get; set; }
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public long RxFrequencyHz { get; set; }
        public long TxFrequencyHz { get; set; }
        public PowerLevel Power { get; set; }
        public ChannelBandwidth Bandwidth { get; set; }
        public AdmitCriteria AdmitCriteria { get; set; }
        public bool ReceiveOnly { get; set; }
        public List<string> ZoneNames { get; set; } = [];
        public string? RxTone { get; set; }
        public string? TxTone { get; set; }
        public int? ColorCode { get; set; }
        public int? TimeSlot { get; set; }
        public string? ContactName { get; set; }
        public string? RxGroupName { get; set; }
        public CodeplugConfidence Confidence { get; set; }

        public static ChannelDto FromModel(Channel channel) => channel switch
        {
            AnalogChannel analog => new ChannelDto
            {
                Kind = ChannelKind.Analog,
                Index = analog.Index,
                Name = analog.Name,
                RxFrequencyHz = analog.RxFrequencyHz,
                TxFrequencyHz = analog.TxFrequencyHz,
                Power = analog.Power,
                Bandwidth = analog.Bandwidth,
                AdmitCriteria = analog.AdmitCriteria,
                ReceiveOnly = analog.ReceiveOnly,
                ZoneNames = analog.ZoneNames.ToList(),
                RxTone = analog.RxTone.RawValue,
                TxTone = analog.TxTone.RawValue,
                Confidence = analog.Confidence,
            },
            DigitalChannel digital => new ChannelDto
            {
                Kind = ChannelKind.Digital,
                Index = digital.Index,
                Name = digital.Name,
                RxFrequencyHz = digital.RxFrequencyHz,
                TxFrequencyHz = digital.TxFrequencyHz,
                Power = digital.Power,
                Bandwidth = digital.Bandwidth,
                AdmitCriteria = digital.AdmitCriteria,
                ReceiveOnly = digital.ReceiveOnly,
                ZoneNames = digital.ZoneNames.ToList(),
                ColorCode = digital.ColorCode,
                TimeSlot = digital.TimeSlot,
                ContactName = digital.ContactName,
                RxGroupName = digital.RxGroupName,
                Confidence = digital.Confidence,
            },
            _ => throw new InvalidOperationException($"Unsupported channel type {channel.GetType().FullName}.")
        };

        public Channel ToModel() => Kind switch
        {
            ChannelKind.Analog => new AnalogChannel(Index, Name, RxFrequencyHz, TxFrequencyHz, Power, Bandwidth, AdmitCriteria, ZoneNames, ToneValue.Parse(RxTone), ToneValue.Parse(TxTone), Confidence) { ReceiveOnly = ReceiveOnly },
            ChannelKind.Digital => new DigitalChannel(Index, Name, RxFrequencyHz, TxFrequencyHz, Power, Bandwidth, AdmitCriteria, ZoneNames, ColorCode ?? 1, TimeSlot ?? 1, ContactName, RxGroupName, Confidence) { ReceiveOnly = ReceiveOnly },
            _ => throw new InvalidOperationException($"Unsupported channel kind {Kind}.")
        };
    }
}
