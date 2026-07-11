namespace Bao1702.Codeplug.Model;

/// <summary>Analog or digital channel modulation type.</summary>
public enum ChannelKind
{
    Analog,
    Digital,
}

public enum PowerLevel
{
    Low,
    Medium,
    High,
}

public enum ChannelBandwidth
{
    Narrow,
    Wide,
}

public enum AdmitCriteria
{
    Always,
    ChannelFree,
    ColorCodeFree,
}

public enum ContactType
{
    Group,
    Private,
    AllCall,
}

public enum CodeplugConfidence
{
    Confirmed,
    Inferred,
    Unknown,
    Preserved,
    RequiresHardwareVerification,
}

public enum Language
{
    Chinese = 0,
    English = 1,
}

/// <summary>
/// Binary evidence: orig=1(10s), eng=5(Always). OEM CPS dropdown values.
/// </summary>
public enum BacklightDuration
{
    FiveSeconds = 0,
    TenSeconds = 1,
    FifteenSeconds = 2,
    TwentySeconds = 3,
    TwentyFiveSeconds = 4,
    Always = 5,
}

/// <summary>
/// Key function codes for programmable key assignments (config+0x150).
/// </summary>
public enum KeyFunction : byte
{
    None = 0x00,
    ToggleAllAlertTones = 0x01,
    EmergencyOn = 0x02,
    EmergencyOff = 0x03,
    PowerLevel = 0x04,
    Monitor = 0x05,
    Scan = 0x06,
    VOX = 0x07,
    SquelchTightToggle = 0x08,
    LoneWorker = 0x09,
    ToneHz1750 = 0x0A,
    Repeater = 0x0B,
    NuisanceDelete = 0x0C,
    PrivacyToggle = 0x0D,
    ManualDial = 0x0E,
    DisplayToggle = 0x0F,
    TalkaRound = 0x10,
    LED = 0x11,
    ZoneUp = 0x12,
    GPS = 0x13,
    Record = 0x14,
    Playback = 0x15,
    BatteryIndicator = 0x16,
    FmRadio = 0x17,
    Backlight = 0x18,
    OneTouchCall1 = 0x19,
    OneTouchCall2 = 0x1A,
    OneTouchCall3 = 0x1B,
    OneTouchCall4 = 0x1C,
    OneTouchCall5 = 0x1D,
    OneTouchCall6 = 0x1E,
    Voltage = 0x1F,
    ChannelUp = 0x20,
    ChannelDown = 0x21,
    ZoneDown = 0x22,
    KeyLock = 0x23,
    DigitalContactDial1 = 0x24,
    DigitalContactDial2 = 0x25,
    DigitalContactDial3 = 0x26,
    DigitalContactDial4 = 0x27,
    DigitalContactDial5 = 0x28,
    DigitalContactDial6 = 0x29,
}
