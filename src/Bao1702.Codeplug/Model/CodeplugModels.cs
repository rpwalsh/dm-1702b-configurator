namespace Bao1702.Codeplug.Model;

/// <summary>Radio-wide general settings: radio name and intro lines.</summary>
public sealed record GeneralSettings(string RadioName, string IntroLine1, string IntroLine2, CodeplugConfidence Confidence);

public sealed record DisplaySettings(
    BacklightDuration BacklightDuration,
    bool ShowChannelNumber,
    bool ShowClock,
    CodeplugConfidence Confidence);

public sealed record PowerSettings(PowerLevel DefaultPower, bool BatterySaverEnabled, CodeplugConfidence Confidence);

/// <summary>
/// Config region fields independently verified through controlled configuration differences.
/// </summary>
public sealed record ParameterSettings(
    Language Language,
    bool VoxEnabled,
    int VoxLevel,
    int TxTimeout,
    int TxPreambleDuration,
    int MicGain,
    bool CtcssTailRevert,
    bool KeypadLockEnabled,
    CodeplugConfidence Confidence);

public sealed record SquelchSettings(int AnalogLevel, int DigitalLevel, CodeplugConfidence Confidence);

public sealed record StartupScreen(string Line1, string Line2, CodeplugConfidence Confidence);

public sealed record RadioIdentitySettings(string DmrId, string Callsign, CodeplugConfidence Confidence);

public sealed record ButtonConfig(string SideButton1Short, string SideButton1Long, string SideButton2Short, string SideButton2Long, CodeplugConfidence Confidence);

/// <summary>
/// 7 programmable keys × 2 actions (short/long press), each a single function code byte.
/// Key order: SK1, SK2, TK, P1, P2, P3, P4. Each key has [short, long].
/// Function codes: 0x00=None, 0x04=PowerLevel, 0x05=Monitor, 0x06=Scan, 0x07=VOX,
/// 0x09=LoneWorker, 0x0A=1750Hz, 0x0C=NuisanceDelete, 0x0F=DisplayToggle,
/// 0x13=GPS, 0x14=Record, 0x15=Playback, 0x17=FM Radio, 0x1E=DTMF Dial, etc.
/// Evidence: baseline defaults [05,06,07,00,0C,00,04,0C,00,09,00,17,00,07],
/// </summary>
public sealed record KeyAssignmentTable(byte[] Assignments, CodeplugConfidence Confidence)
{
    /// <summary>Stock CPS default key assignments (14 bytes).</summary>
    public static readonly byte[] DefaultAssignments =
        [0x05, 0x06, 0x07, 0x00, 0x0C, 0x00, 0x04, 0x0C, 0x00, 0x09, 0x00, 0x17, 0x00, 0x07];

    public const int TableLength = 14;

    // Key slot indices (each key has short=even, long=odd).
    public const int SK1Short = 0;
    public const int SK1Long = 1;
    public const int SK2Short = 2;
    public const int SK2Long = 3;
    public const int TKShort = 4;
    public const int TKLong = 5;
    public const int P1Short = 6;
    public const int P1Long = 7;
    public const int P2Short = 8;
    public const int P2Long = 9;
    public const int P3Short = 10;
    public const int P3Long = 11;
    public const int P4Short = 12;
    public const int P4Long = 13;
}

public sealed record DtmfConfig(string PttId, string KillCode, string ReviveCode, CodeplugConfidence Confidence);

public sealed record PrivacyConfig(bool BasicPrivacyEnabled, int KeyIndex, CodeplugConfidence Confidence);

public sealed record EmergencyConfig(int SelectedSystemIndex, CodeplugConfidence Confidence);

/// <summary>
/// Name (10 bytes ASCII) + KeyType (1 byte) + KeyData (8 bytes BCD, 0xFF padded) + Footer (4 bytes).
/// </summary>
public sealed record PrivacyEntry(string Name, byte KeyType, byte[] KeyData, byte[] Footer, CodeplugConfidence Confidence);

/// <summary>
/// Name (10 bytes ASCII) + Type + Mode + RevertChannel + AlarmCallToFollow +
/// ImpoliteRetries + PoliteRetries + VoiceCycles + Reserved + HotMicDuration + RxIntervalDuration.
/// </summary>
public sealed record EmergencySystem(
    string Name,
    byte EmergencyType,
    byte EmergencyMode,
    byte RevertChannel,
    byte AlarmCallToFollow,
    byte ImpoliteRetries,
    byte PoliteRetries,
    byte VoiceCycles,
    byte Reserved,
    byte HotMicDuration,
    byte RxIntervalDuration,
    CodeplugConfidence Confidence);

/// <summary>
/// </summary>
public sealed record LoneWorkerConfig(bool Enabled, byte ResponseTimeMinutes, byte ReminderTimeSeconds, CodeplugConfidence Confidence);

/// <summary>
/// Count at 0xA000.
/// </summary>
public sealed record QuickTextMessage(string Text, CodeplugConfidence Confidence);

public sealed record Dm1702NativeSemantics(
    bool RxOnly,
    bool VoxEnabled,
    bool TalkAroundEnabled,
    bool LoneWorkerEnabled,
    bool AutoScanEnabled,
    bool EmergencyAlarmIndication,
    bool EmergencyAlarmAck,
    bool EmergencyCallIndication,
    bool PrivateCallConfirmed,
    bool ShortDataMessage,
    bool EncryptionEnabled,
    int EncryptionKeyIndex,
    bool DoubleCapacityMode,
    bool TalkAroundStatus,
    bool DisplayPttId,
    int? GpsSystemIndex,
    int? ScanListIndex,
    int? EmergencySystemIndex,
    string? PttKeyupMode,
    string? PttKeyupEncodeType,
    CodeplugConfidence Confidence);

public sealed record Contact(string Name, int CallId, ContactType ContactType, CodeplugConfidence Confidence);

public sealed record GroupList(string Name, IReadOnlyList<string> ContactNames, CodeplugConfidence Confidence);

public sealed record ScanList(string Name, IReadOnlyList<string> ChannelNames, CodeplugConfidence Confidence);

/// <summary>
/// TalkGroupId is the DMR talkgroup ID stored at record +0x0C as LE16 + 0x00 pad.
/// When zero, the serializer derives it from the first contact's CallId.
/// </summary>
public sealed record RxGroup(string Name, IReadOnlyList<string> ContactNames, CodeplugConfidence Confidence, int TalkGroupId = 0);

public sealed record Zone(string Name, IReadOnlyList<string> ChannelNames, CodeplugConfidence Confidence);

public sealed record UnknownCodeplugSegment(string Name, int Offset, byte[] Data, CodeplugConfidence Confidence);

public abstract record Channel(
    int Index,
    string Name,
    long RxFrequencyHz,
    long TxFrequencyHz,
    PowerLevel Power,
    ChannelBandwidth Bandwidth,
    AdmitCriteria AdmitCriteria,
    IReadOnlyList<string> ZoneNames,
    CodeplugConfidence Confidence,
    Dm1702NativeSemantics? NativeSemantics = null)
{
    public abstract ChannelKind Kind { get; }

    /// <summary>Explicit transmit inhibit. This is independent of frequency or channel naming.</summary>
    public bool ReceiveOnly { get; init; } = NativeSemantics?.RxOnly == true;
}

public sealed record AnalogChannel(
    int Index,
    string Name,
    long RxFrequencyHz,
    long TxFrequencyHz,
    PowerLevel Power,
    ChannelBandwidth Bandwidth,
    AdmitCriteria AdmitCriteria,
    IReadOnlyList<string> ZoneNames,
    ToneValue RxTone,
    ToneValue TxTone,
    CodeplugConfidence Confidence,
    Dm1702NativeSemantics? NativeSemantics = null) : Channel(Index, Name, RxFrequencyHz, TxFrequencyHz, Power, Bandwidth, AdmitCriteria, ZoneNames, Confidence, NativeSemantics)
{
    public override ChannelKind Kind => ChannelKind.Analog;
}

public sealed record DigitalChannel(
    int Index,
    string Name,
    long RxFrequencyHz,
    long TxFrequencyHz,
    PowerLevel Power,
    ChannelBandwidth Bandwidth,
    AdmitCriteria AdmitCriteria,
    IReadOnlyList<string> ZoneNames,
    int ColorCode,
    int TimeSlot,
    string? ContactName,
    string? RxGroupName,
    CodeplugConfidence Confidence,
    Dm1702NativeSemantics? NativeSemantics = null) : Channel(Index, Name, RxFrequencyHz, TxFrequencyHz, Power, Bandwidth, AdmitCriteria, ZoneNames, Confidence, NativeSemantics)
{
    public override ChannelKind Kind => ChannelKind.Digital;
}

public sealed record CodeplugImage(
    GeneralSettings GeneralSettings,
    DisplaySettings DisplaySettings,
    PowerSettings PowerSettings,
    SquelchSettings SquelchSettings,
    ParameterSettings ParameterSettings,
    StartupScreen StartupScreen,
    RadioIdentitySettings RadioIdentity,
    ButtonConfig ButtonConfig,
    KeyAssignmentTable KeyAssignments,
    DtmfConfig DtmfConfig,
    PrivacyConfig PrivacyConfig,
    IReadOnlyList<PrivacyEntry> PrivacyEntries,
    IReadOnlyList<EmergencySystem> EmergencySystems,
    EmergencyConfig EmergencyConfig,
    LoneWorkerConfig LoneWorkerConfig,
    IReadOnlyList<QuickTextMessage> QuickTextMessages,
    IReadOnlyList<Channel> Channels,
    IReadOnlyList<Zone> Zones,
    IReadOnlyList<Contact> Contacts,
    IReadOnlyList<GroupList> GroupLists,
    IReadOnlyList<ScanList> ScanLists,
    IReadOnlyList<RxGroup> RxGroups,
    IReadOnlyList<UnknownCodeplugSegment> UnknownSegments,
    byte[] PreservedRawImage)
{
    public static CodeplugImage CreateEmpty() => new(
        new GeneralSettings("BAO1702", "BAO1702", "READY", CodeplugConfidence.Inferred),
        new DisplaySettings(BacklightDuration.TenSeconds, true, false, CodeplugConfidence.Inferred),
        new PowerSettings(PowerLevel.High, true, CodeplugConfidence.Inferred),
        new SquelchSettings(5, 5, CodeplugConfidence.Inferred),
        new ParameterSettings(Language.English, false, 1, 30, 12, 1, false, false, CodeplugConfidence.Inferred),
        new StartupScreen("BAO1702", "READY", CodeplugConfidence.Inferred),
        new RadioIdentitySettings("0000000", "NOCALL", CodeplugConfidence.Inferred),
        new ButtonConfig("Monitor", "Scan", "Power", "Zone", CodeplugConfidence.Inferred),
        new KeyAssignmentTable((byte[])KeyAssignmentTable.DefaultAssignments.Clone(), CodeplugConfidence.Inferred),
        new DtmfConfig(string.Empty, string.Empty, string.Empty, CodeplugConfidence.Unknown),
        new PrivacyConfig(false, 0, CodeplugConfidence.Unknown),
        [],
        [],
        new EmergencyConfig(0, CodeplugConfidence.Unknown),
        new LoneWorkerConfig(false, 10, 10, CodeplugConfidence.Inferred),
        [],
        [], [], [], [], [], [], [], Array.Empty<byte>());
}
