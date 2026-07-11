using System.Collections.ObjectModel;
using Bao1702.Codeplug.Model;
using Bao1702.Codeplug.Validation;
using Bao1702.Desktop.Commands;

namespace Bao1702.Desktop.ViewModels;

/// <summary>
/// ViewModel for the main codeplug editor — manages channel, zone, scan list, contact,
/// RX group, and group list collections with full add/delete/edit support.
/// </summary>
public sealed class CodeplugEditorViewModel : ObservableObject
{
    private CodeplugImage _codeplug = CodeplugImage.CreateEmpty();
    private string _validationSummary = "No validation run yet.";
    private int _selectedEditorTab;
    private bool _isDirty;

    // Editable settings backing fields
    private string _radioName = string.Empty;
    private string _introLine1 = string.Empty;
    private string _introLine2 = string.Empty;
    private string _dmrId = string.Empty;
    private string _callsign = string.Empty;
    private int _analogSquelch;
    private int _digitalSquelch;

    // Parameter settings backing fields
    private Language _language = Language.English;
    private BacklightDuration _backlightDuration = BacklightDuration.TenSeconds;
    private PowerLevel _defaultPower = PowerLevel.High;
    private bool _voxEnabled;
    private int _voxLevel = 1;
    private int _txTimeout = 30;
    private int _txPreambleDuration = 12;
    private int _micGain = 1;
    private bool _ctcssTailRevert;
    private bool _keypadLockEnabled;

    // Key assignment backing fields
    private KeyFunction _sk1Short = KeyFunction.Monitor;
    private KeyFunction _sk1Long = KeyFunction.Scan;
    private KeyFunction _sk2Short = KeyFunction.VOX;
    private KeyFunction _sk2Long = KeyFunction.None;
    private KeyFunction _tkShort = KeyFunction.NuisanceDelete;
    private KeyFunction _tkLong = KeyFunction.None;
    private KeyFunction _p1Short = KeyFunction.PowerLevel;
    private KeyFunction _p1Long = KeyFunction.NuisanceDelete;
    private KeyFunction _p2Short = KeyFunction.None;
    private KeyFunction _p2Long = KeyFunction.LoneWorker;
    private KeyFunction _p3Short = KeyFunction.None;
    private KeyFunction _p3Long = KeyFunction.FmRadio;
    private KeyFunction _p4Short = KeyFunction.None;
    private KeyFunction _p4Long = KeyFunction.VOX;

    public CodeplugEditorViewModel()
    {
        AddAnalogChannelCommand = new RelayCommand(AddAnalogChannel);
        AddDigitalChannelCommand = new RelayCommand(AddDigitalChannel);
        DeleteChannelCommand = new RelayCommand(DeleteSelectedChannel, () => SelectedChannel is not null);
        ApplyChannelEditCommand = new RelayCommand(ApplyChannelEdit, () => SelectedChannel is not null);
        AddContactCommand = new RelayCommand(AddContact);
        DeleteContactCommand = new RelayCommand(DeleteSelectedContact, () => SelectedContact is not null);
        ApplyContactEditCommand = new RelayCommand(ApplyContactEdit, () => SelectedContact is not null);
        AddZoneCommand = new RelayCommand(AddZone);
        DeleteZoneCommand = new RelayCommand(DeleteSelectedZone, () => SelectedZone is not null);
        ApplyZoneEditCommand = new RelayCommand(ApplyZoneEdit, () => SelectedZone is not null);
        AddScanListCommand = new RelayCommand(AddScanList);
        DeleteScanListCommand = new RelayCommand(DeleteSelectedScanList, () => SelectedScanList is not null);
        ApplyScanListEditCommand = new RelayCommand(ApplyScanListEdit, () => SelectedScanList is not null);
        AddRxGroupCommand = new RelayCommand(AddRxGroup);
        DeleteRxGroupCommand = new RelayCommand(DeleteSelectedRxGroup, () => SelectedRxGroup is not null);
        ApplyRxGroupEditCommand = new RelayCommand(ApplyRxGroupEdit, () => SelectedRxGroup is not null);
        AddGroupListCommand = new RelayCommand(AddGroupList);
        DeleteGroupListCommand = new RelayCommand(DeleteSelectedGroupList, () => SelectedGroupList is not null);
        ApplyGroupListEditCommand = new RelayCommand(ApplyGroupListEdit, () => SelectedGroupList is not null);
    }

    public CodeplugImage Codeplug
    {
        get => _codeplug;
        set
        {
            if (SetProperty(ref _codeplug, value))
            {
                RefreshCollections();
                RefreshSettingsFromCodeplug();
                IsDirty = false;
            }
        }
    }

    public ObservableCollection<Channel> Channels { get; } = [];
    public ObservableCollection<Zone> Zones { get; } = [];
    public ObservableCollection<Contact> Contacts { get; } = [];
    public ObservableCollection<GroupList> GroupLists { get; } = [];
    public ObservableCollection<ScanList> ScanLists { get; } = [];
    public ObservableCollection<RxGroup> RxGroups { get; } = [];

    public int ChannelCount => Channels.Count;
    public int ZoneCount => Zones.Count;
    public int ContactCount => Contacts.Count;
    public int GroupListCount => GroupLists.Count;
    public int ScanListCount => ScanLists.Count;
    public int RxGroupCount => RxGroups.Count;

    public int SelectedEditorTab
    {
        get => _selectedEditorTab;
        set => SetProperty(ref _selectedEditorTab, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        set => SetProperty(ref _validationSummary, value);
    }

    #region Editable settings properties

    public string RadioName
    {
        get => _radioName;
        set { if (SetProperty(ref _radioName, value)) MarkDirty(); }
    }

    public string IntroLine1
    {
        get => _introLine1;
        set { if (SetProperty(ref _introLine1, value)) MarkDirty(); }
    }

    public string IntroLine2
    {
        get => _introLine2;
        set { if (SetProperty(ref _introLine2, value)) MarkDirty(); }
    }

    public string DmrId
    {
        get => _dmrId;
        set { if (SetProperty(ref _dmrId, value)) MarkDirty(); }
    }

    public string Callsign
    {
        get => _callsign;
        set { if (SetProperty(ref _callsign, value)) MarkDirty(); }
    }

    public int AnalogSquelch
    {
        get => _analogSquelch;
        set { if (SetProperty(ref _analogSquelch, value)) MarkDirty(); }
    }

    public int DigitalSquelch
    {
        get => _digitalSquelch;
        set { if (SetProperty(ref _digitalSquelch, value)) MarkDirty(); }
    }

    public string DefaultPower => Codeplug.PowerSettings.DefaultPower.ToString();

    // Enum source arrays for ComboBox binding
    public Language[] LanguageValues { get; } = Enum.GetValues<Language>();
    public BacklightDuration[] BacklightDurationValues { get; } = Enum.GetValues<BacklightDuration>();
    public PowerLevel[] PowerLevelValues { get; } = Enum.GetValues<PowerLevel>();
    public KeyFunction[] KeyFunctionValues { get; } = Enum.GetValues<KeyFunction>();

    #endregion

    #region Parameter settings properties

    public Language Language
    {
        get => _language;
        set { if (SetProperty(ref _language, value)) MarkDirty(); }
    }

    public BacklightDuration BacklightDuration
    {
        get => _backlightDuration;
        set { if (SetProperty(ref _backlightDuration, value)) MarkDirty(); }
    }

    public PowerLevel DefaultPowerLevel
    {
        get => _defaultPower;
        set { if (SetProperty(ref _defaultPower, value)) MarkDirty(); }
    }

    public bool VoxEnabled
    {
        get => _voxEnabled;
        set { if (SetProperty(ref _voxEnabled, value)) MarkDirty(); }
    }

    public int VoxLevel
    {
        get => _voxLevel;
        set { if (SetProperty(ref _voxLevel, value)) MarkDirty(); }
    }

    public int TxTimeout
    {
        get => _txTimeout;
        set { if (SetProperty(ref _txTimeout, value)) MarkDirty(); }
    }

    public int TxPreambleDuration
    {
        get => _txPreambleDuration;
        set { if (SetProperty(ref _txPreambleDuration, value)) MarkDirty(); }
    }

    public int MicGain
    {
        get => _micGain;
        set { if (SetProperty(ref _micGain, value)) MarkDirty(); }
    }

    public bool CtcssTailRevert
    {
        get => _ctcssTailRevert;
        set { if (SetProperty(ref _ctcssTailRevert, value)) MarkDirty(); }
    }

    public bool KeypadLockEnabled
    {
        get => _keypadLockEnabled;
        set { if (SetProperty(ref _keypadLockEnabled, value)) MarkDirty(); }
    }

    #endregion

    #region Key assignment properties

    public KeyFunction SK1Short
    {
        get => _sk1Short;
        set { if (SetProperty(ref _sk1Short, value)) MarkDirty(); }
    }

    public KeyFunction SK1Long
    {
        get => _sk1Long;
        set { if (SetProperty(ref _sk1Long, value)) MarkDirty(); }
    }

    public KeyFunction SK2Short
    {
        get => _sk2Short;
        set { if (SetProperty(ref _sk2Short, value)) MarkDirty(); }
    }

    public KeyFunction SK2Long
    {
        get => _sk2Long;
        set { if (SetProperty(ref _sk2Long, value)) MarkDirty(); }
    }

    public KeyFunction TKShort
    {
        get => _tkShort;
        set { if (SetProperty(ref _tkShort, value)) MarkDirty(); }
    }

    public KeyFunction TKLong
    {
        get => _tkLong;
        set { if (SetProperty(ref _tkLong, value)) MarkDirty(); }
    }

    public KeyFunction P1Short
    {
        get => _p1Short;
        set { if (SetProperty(ref _p1Short, value)) MarkDirty(); }
    }

    public KeyFunction P1Long
    {
        get => _p1Long;
        set { if (SetProperty(ref _p1Long, value)) MarkDirty(); }
    }

    public KeyFunction P2Short
    {
        get => _p2Short;
        set { if (SetProperty(ref _p2Short, value)) MarkDirty(); }
    }

    public KeyFunction P2Long
    {
        get => _p2Long;
        set { if (SetProperty(ref _p2Long, value)) MarkDirty(); }
    }

    public KeyFunction P3Short
    {
        get => _p3Short;
        set { if (SetProperty(ref _p3Short, value)) MarkDirty(); }
    }

    public KeyFunction P3Long
    {
        get => _p3Long;
        set { if (SetProperty(ref _p3Long, value)) MarkDirty(); }
    }

    public KeyFunction P4Short
    {
        get => _p4Short;
        set { if (SetProperty(ref _p4Short, value)) MarkDirty(); }
    }

    public KeyFunction P4Long
    {
        get => _p4Long;
        set { if (SetProperty(ref _p4Long, value)) MarkDirty(); }
    }

    #endregion

    #region Selection tracking

    private Channel? _selectedChannel;
    public Channel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (SetProperty(ref _selectedChannel, value))
            {
                DeleteChannelCommand.RaiseCanExecuteChanged();
                ApplyChannelEditCommand.RaiseCanExecuteChanged();
                LoadChannelEditFields(value);
            }
        }
    }

    #region Channel detail editing

    private string _editChannelName = string.Empty;
    private string _editRxFrequency = string.Empty;
    private string _editTxFrequency = string.Empty;
    private PowerLevel _editPower = PowerLevel.High;
    private ChannelBandwidth _editBandwidth = ChannelBandwidth.Wide;
    private AdmitCriteria _editAdmitCriteria = AdmitCriteria.Always;
    private string _editRxTone = string.Empty;
    private string _editTxTone = string.Empty;
    private int _editColorCode = 1;
    private int _editTimeSlot = 1;
    private string _editChContactName = string.Empty;
    private string _editChRxGroupName = string.Empty;

    public string EditChannelName
    {
        get => _editChannelName;
        set => SetProperty(ref _editChannelName, value);
    }

    public string EditRxFrequency
    {
        get => _editRxFrequency;
        set => SetProperty(ref _editRxFrequency, value);
    }

    public string EditTxFrequency
    {
        get => _editTxFrequency;
        set => SetProperty(ref _editTxFrequency, value);
    }

    public PowerLevel EditPower
    {
        get => _editPower;
        set => SetProperty(ref _editPower, value);
    }

    public ChannelBandwidth EditBandwidth
    {
        get => _editBandwidth;
        set => SetProperty(ref _editBandwidth, value);
    }

    public AdmitCriteria EditAdmitCriteria
    {
        get => _editAdmitCriteria;
        set => SetProperty(ref _editAdmitCriteria, value);
    }

    public string EditRxTone
    {
        get => _editRxTone;
        set => SetProperty(ref _editRxTone, value);
    }

    public string EditTxTone
    {
        get => _editTxTone;
        set => SetProperty(ref _editTxTone, value);
    }

    public int EditColorCode
    {
        get => _editColorCode;
        set => SetProperty(ref _editColorCode, value);
    }

    public int EditTimeSlot
    {
        get => _editTimeSlot;
        set => SetProperty(ref _editTimeSlot, value);
    }

    public string EditChContactName
    {
        get => _editChContactName;
        set => SetProperty(ref _editChContactName, value);
    }

    public string EditChRxGroupName
    {
        get => _editChRxGroupName;
        set => SetProperty(ref _editChRxGroupName, value);
    }

    public bool IsAnalogSelected => SelectedChannel is AnalogChannel;
    public bool IsDigitalSelected => SelectedChannel is DigitalChannel;

    private void LoadChannelEditFields(Channel? channel)
    {
        if (channel is null)
        {
            _editChannelName = string.Empty;
            _editRxFrequency = string.Empty;
            _editTxFrequency = string.Empty;
        }
        else
        {
            _editChannelName = channel.Name;
            _editRxFrequency = FormatFrequency(channel.RxFrequencyHz);
            _editTxFrequency = FormatFrequency(channel.TxFrequencyHz);
            _editPower = channel.Power;
            _editBandwidth = channel.Bandwidth;
            _editAdmitCriteria = channel.AdmitCriteria;

            if (channel is AnalogChannel analog)
            {
                _editRxTone = analog.RxTone.ToString();
                _editTxTone = analog.TxTone.ToString();
            }
            else
            {
                _editRxTone = string.Empty;
                _editTxTone = string.Empty;
            }

            if (channel is DigitalChannel digital)
            {
                _editColorCode = digital.ColorCode;
                _editTimeSlot = digital.TimeSlot;
                _editChContactName = digital.ContactName ?? string.Empty;
                _editChRxGroupName = digital.RxGroupName ?? string.Empty;
            }
            else
            {
                _editColorCode = 1;
                _editTimeSlot = 1;
                _editChContactName = string.Empty;
                _editChRxGroupName = string.Empty;
            }
        }

        OnPropertyChanged(nameof(EditChannelName));
        OnPropertyChanged(nameof(EditRxFrequency));
        OnPropertyChanged(nameof(EditTxFrequency));
        OnPropertyChanged(nameof(EditPower));
        OnPropertyChanged(nameof(EditBandwidth));
        OnPropertyChanged(nameof(EditAdmitCriteria));
        OnPropertyChanged(nameof(EditRxTone));
        OnPropertyChanged(nameof(EditTxTone));
        OnPropertyChanged(nameof(EditColorCode));
        OnPropertyChanged(nameof(EditTimeSlot));
        OnPropertyChanged(nameof(EditChContactName));
        OnPropertyChanged(nameof(EditChRxGroupName));
        OnPropertyChanged(nameof(IsAnalogSelected));
        OnPropertyChanged(nameof(IsDigitalSelected));
    }

    private static string FormatFrequency(long hz)
        => (hz / 1_000_000d).ToString("F6");

    private static long ParseFrequencyHz(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mhz))
            return (long)Math.Round(mhz * 1_000_000d, MidpointRounding.AwayFromZero);
        return 0;
    }

    #endregion

    private Contact? _selectedContact;
    public Contact? SelectedContact
    {
        get => _selectedContact;
        set
        {
            if (SetProperty(ref _selectedContact, value))
            {
                DeleteContactCommand.RaiseCanExecuteChanged();
                ApplyContactEditCommand.RaiseCanExecuteChanged();
                LoadContactEditFields(value);
            }
        }
    }

    #region Contact detail editing

    private string _editContactName = string.Empty;
    private string _editContactCallId = string.Empty;
    private ContactType _editContactType = ContactType.Group;

    public string EditContactName
    {
        get => _editContactName;
        set => SetProperty(ref _editContactName, value);
    }

    public string EditContactCallId
    {
        get => _editContactCallId;
        set => SetProperty(ref _editContactCallId, value);
    }

    public ContactType EditContactType
    {
        get => _editContactType;
        set => SetProperty(ref _editContactType, value);
    }

    private void LoadContactEditFields(Contact? contact)
    {
        if (contact is null)
        {
            _editContactName = string.Empty;
            _editContactCallId = string.Empty;
            _editContactType = ContactType.Group;
        }
        else
        {
            _editContactName = contact.Name;
            _editContactCallId = contact.CallId.ToString();
            _editContactType = contact.ContactType;
        }

        OnPropertyChanged(nameof(EditContactName));
        OnPropertyChanged(nameof(EditContactCallId));
        OnPropertyChanged(nameof(EditContactType));
    }

    #endregion

    private Zone? _selectedZone;
    public Zone? SelectedZone
    {
        get => _selectedZone;
        set
        {
            if (SetProperty(ref _selectedZone, value))
            {
                DeleteZoneCommand.RaiseCanExecuteChanged();
                ApplyZoneEditCommand.RaiseCanExecuteChanged();
                LoadZoneEditFields(value);
            }
        }
    }

    #region Zone detail editing

    private string _editZoneName = string.Empty;
    private string _editZoneChannels = string.Empty;

    public string EditZoneName
    {
        get => _editZoneName;
        set => SetProperty(ref _editZoneName, value);
    }

    public string EditZoneChannels
    {
        get => _editZoneChannels;
        set => SetProperty(ref _editZoneChannels, value);
    }

    private void LoadZoneEditFields(Zone? zone)
    {
        if (zone is null)
        {
            _editZoneName = string.Empty;
            _editZoneChannels = string.Empty;
        }
        else
        {
            _editZoneName = zone.Name;
            _editZoneChannels = string.Join(", ", zone.ChannelNames);
        }

        OnPropertyChanged(nameof(EditZoneName));
        OnPropertyChanged(nameof(EditZoneChannels));
    }

    #endregion

    private ScanList? _selectedScanList;
    public ScanList? SelectedScanList
    {
        get => _selectedScanList;
        set
        {
            if (SetProperty(ref _selectedScanList, value))
            {
                DeleteScanListCommand.RaiseCanExecuteChanged();
                ApplyScanListEditCommand.RaiseCanExecuteChanged();
                LoadScanListEditFields(value);
            }
        }
    }

    #region Scan list detail editing

    private string _editScanListName = string.Empty;
    private string _editScanListChannels = string.Empty;

    public string EditScanListName
    {
        get => _editScanListName;
        set => SetProperty(ref _editScanListName, value);
    }

    public string EditScanListChannels
    {
        get => _editScanListChannels;
        set => SetProperty(ref _editScanListChannels, value);
    }

    private void LoadScanListEditFields(ScanList? scanList)
    {
        if (scanList is null)
        {
            _editScanListName = string.Empty;
            _editScanListChannels = string.Empty;
        }
        else
        {
            _editScanListName = scanList.Name;
            _editScanListChannels = string.Join(", ", scanList.ChannelNames);
        }

        OnPropertyChanged(nameof(EditScanListName));
        OnPropertyChanged(nameof(EditScanListChannels));
    }

    #endregion

    private RxGroup? _selectedRxGroup;
    public RxGroup? SelectedRxGroup
    {
        get => _selectedRxGroup;
        set
        {
            if (SetProperty(ref _selectedRxGroup, value))
            {
                DeleteRxGroupCommand.RaiseCanExecuteChanged();
                ApplyRxGroupEditCommand.RaiseCanExecuteChanged();
                LoadRxGroupEditFields(value);
            }
        }
    }

    #region RxGroup detail editing

    private string _editRxGroupName = string.Empty;
    private string _editRxGroupContacts = string.Empty;

    public string EditRxGroupEditName
    {
        get => _editRxGroupName;
        set => SetProperty(ref _editRxGroupName, value);
    }

    public string EditRxGroupContacts
    {
        get => _editRxGroupContacts;
        set => SetProperty(ref _editRxGroupContacts, value);
    }

    private void LoadRxGroupEditFields(RxGroup? rxGroup)
    {
        if (rxGroup is null)
        {
            _editRxGroupName = string.Empty;
            _editRxGroupContacts = string.Empty;
        }
        else
        {
            _editRxGroupName = rxGroup.Name;
            _editRxGroupContacts = string.Join(", ", rxGroup.ContactNames);
        }

        OnPropertyChanged(nameof(EditRxGroupEditName));
        OnPropertyChanged(nameof(EditRxGroupContacts));
    }

    #endregion

    private GroupList? _selectedGroupList;
    public GroupList? SelectedGroupList
    {
        get => _selectedGroupList;
        set
        {
            if (SetProperty(ref _selectedGroupList, value))
            {
                DeleteGroupListCommand.RaiseCanExecuteChanged();
                ApplyGroupListEditCommand.RaiseCanExecuteChanged();
                LoadGroupListEditFields(value);
            }
        }
    }

    #region GroupList detail editing

    private string _editGroupListName = string.Empty;
    private string _editGroupListContacts = string.Empty;

    public string EditGroupListEditName
    {
        get => _editGroupListName;
        set => SetProperty(ref _editGroupListName, value);
    }

    public string EditGroupListContacts
    {
        get => _editGroupListContacts;
        set => SetProperty(ref _editGroupListContacts, value);
    }

    private void LoadGroupListEditFields(GroupList? groupList)
    {
        if (groupList is null)
        {
            _editGroupListName = string.Empty;
            _editGroupListContacts = string.Empty;
        }
        else
        {
            _editGroupListName = groupList.Name;
            _editGroupListContacts = string.Join(", ", groupList.ContactNames);
        }

        OnPropertyChanged(nameof(EditGroupListEditName));
        OnPropertyChanged(nameof(EditGroupListContacts));
    }

    #endregion

    #endregion

    #region Commands

    public RelayCommand AddAnalogChannelCommand { get; }
    public RelayCommand AddDigitalChannelCommand { get; }
    public RelayCommand DeleteChannelCommand { get; }
    public RelayCommand ApplyChannelEditCommand { get; }
    public RelayCommand AddContactCommand { get; }
    public RelayCommand DeleteContactCommand { get; }
    public RelayCommand ApplyContactEditCommand { get; }
    public RelayCommand AddZoneCommand { get; }
    public RelayCommand DeleteZoneCommand { get; }
    public RelayCommand ApplyZoneEditCommand { get; }
    public RelayCommand AddScanListCommand { get; }
    public RelayCommand DeleteScanListCommand { get; }
    public RelayCommand ApplyScanListEditCommand { get; }
    public RelayCommand AddRxGroupCommand { get; }
    public RelayCommand DeleteRxGroupCommand { get; }
    public RelayCommand ApplyRxGroupEditCommand { get; }
    public RelayCommand AddGroupListCommand { get; }
    public RelayCommand DeleteGroupListCommand { get; }
    public RelayCommand ApplyGroupListEditCommand { get; }

    #endregion

    #region Add/Delete implementations

    private void AddAnalogChannel()
    {
        var nextIndex = Channels.Count > 0 ? Channels.Max(c => c.Index) + 1 : 1;
        var channel = new AnalogChannel(
            nextIndex, $"CH{nextIndex}", 440_000_000, 440_000_000,
            PowerLevel.High, ChannelBandwidth.Wide, AdmitCriteria.Always,
            [], ToneValue.None, ToneValue.None, CodeplugConfidence.Inferred);
        Channels.Add(channel);
        SelectedChannel = channel;
        MarkDirty();
        RaiseCountChanged();
    }

    private void AddDigitalChannel()
    {
        var nextIndex = Channels.Count > 0 ? Channels.Max(c => c.Index) + 1 : 1;
        var channel = new DigitalChannel(
            nextIndex, $"CH{nextIndex}", 440_000_000, 440_000_000,
            PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.Always,
            [], 1, 1, null, null, CodeplugConfidence.Inferred);
        Channels.Add(channel);
        SelectedChannel = channel;
        MarkDirty();
        RaiseCountChanged();
    }

    private void DeleteSelectedChannel()
    {
        if (SelectedChannel is not null)
        {
            Channels.Remove(SelectedChannel);
            SelectedChannel = null;
            MarkDirty();
            RaiseCountChanged();
        }
    }

    private void ApplyChannelEdit()
    {
        if (SelectedChannel is null) return;

        var index = Channels.IndexOf(SelectedChannel);
        if (index < 0) return;

        var rxHz = ParseFrequencyHz(EditRxFrequency);
        var txHz = ParseFrequencyHz(EditTxFrequency);
        var name = string.IsNullOrWhiteSpace(EditChannelName) ? SelectedChannel.Name : EditChannelName.Trim();
        var zones = SelectedChannel.ZoneNames;
        var semantics = SelectedChannel.NativeSemantics;

        Channel replacement;
        if (SelectedChannel is DigitalChannel)
        {
            replacement = new DigitalChannel(
                SelectedChannel.Index, name, rxHz, txHz,
                EditPower, EditBandwidth, EditAdmitCriteria, zones,
                EditColorCode, EditTimeSlot,
                string.IsNullOrWhiteSpace(EditChContactName) ? null : EditChContactName.Trim(),
                string.IsNullOrWhiteSpace(EditChRxGroupName) ? null : EditChRxGroupName.Trim(),
                CodeplugConfidence.Inferred, semantics);
        }
        else if (SelectedChannel is AnalogChannel)
        {
            replacement = new AnalogChannel(
                SelectedChannel.Index, name, rxHz, txHz,
                EditPower, EditBandwidth, EditAdmitCriteria, zones,
                ToneValue.Parse(EditRxTone ?? string.Empty),
                ToneValue.Parse(EditTxTone ?? string.Empty),
                CodeplugConfidence.Inferred, semantics);
        }
        else
        {
            return;
        }

        Channels[index] = replacement;
        SelectedChannel = replacement;
        MarkDirty();
    }

    private void AddContact()
    {
        var contact = new Contact($"Contact {Contacts.Count + 1}", 1, ContactType.Group, CodeplugConfidence.Inferred);
        Contacts.Add(contact);
        SelectedContact = contact;
        MarkDirty();
        RaiseCountChanged();
    }

    private void DeleteSelectedContact()
    {
        if (SelectedContact is not null)
        {
            Contacts.Remove(SelectedContact);
            SelectedContact = null;
            MarkDirty();
            RaiseCountChanged();
        }
    }

    private void AddZone()
    {
        var zone = new Zone($"Zone {Zones.Count + 1}", [], CodeplugConfidence.Inferred);
        Zones.Add(zone);
        SelectedZone = zone;
        MarkDirty();
        RaiseCountChanged();
    }

    private void DeleteSelectedZone()
    {
        if (SelectedZone is not null)
        {
            Zones.Remove(SelectedZone);
            SelectedZone = null;
            MarkDirty();
            RaiseCountChanged();
        }
    }

    private void AddScanList()
    {
        var scanList = new ScanList($"Scan {ScanLists.Count + 1}", [], CodeplugConfidence.Inferred);
        ScanLists.Add(scanList);
        SelectedScanList = scanList;
        MarkDirty();
        RaiseCountChanged();
    }

    private void DeleteSelectedScanList()
    {
        if (SelectedScanList is not null)
        {
            ScanLists.Remove(SelectedScanList);
            SelectedScanList = null;
            MarkDirty();
            RaiseCountChanged();
        }
    }

    private void AddRxGroup()
    {
        var rxGroup = new RxGroup($"RxGrp {RxGroups.Count + 1}", [], CodeplugConfidence.Inferred);
        RxGroups.Add(rxGroup);
        SelectedRxGroup = rxGroup;
        MarkDirty();
        RaiseCountChanged();
    }

    private void DeleteSelectedRxGroup()
    {
        if (SelectedRxGroup is not null)
        {
            RxGroups.Remove(SelectedRxGroup);
            SelectedRxGroup = null;
            MarkDirty();
            RaiseCountChanged();
        }
    }

    private void AddGroupList()
    {
        var groupList = new GroupList($"Group {GroupLists.Count + 1}", [], CodeplugConfidence.Inferred);
        GroupLists.Add(groupList);
        SelectedGroupList = groupList;
        MarkDirty();
        RaiseCountChanged();
    }

    private void DeleteSelectedGroupList()
    {
        if (SelectedGroupList is not null)
        {
            GroupLists.Remove(SelectedGroupList);
            SelectedGroupList = null;
            MarkDirty();
            RaiseCountChanged();
        }
    }

    private void ApplyContactEdit()
    {
        if (SelectedContact is null) return;
        var index = Contacts.IndexOf(SelectedContact);
        if (index < 0) return;

        var name = string.IsNullOrWhiteSpace(EditContactName) ? SelectedContact.Name : EditContactName.Trim();
        int.TryParse(EditContactCallId, out var callId);
        var replacement = new Contact(name, callId, EditContactType, CodeplugConfidence.Inferred);
        Contacts[index] = replacement;
        SelectedContact = replacement;
        MarkDirty();
    }

    private void ApplyZoneEdit()
    {
        if (SelectedZone is null) return;
        var index = Zones.IndexOf(SelectedZone);
        if (index < 0) return;

        var name = string.IsNullOrWhiteSpace(EditZoneName) ? SelectedZone.Name : EditZoneName.Trim();
        var channels = ParseCommaSeparatedList(EditZoneChannels);
        var replacement = new Zone(name, channels, CodeplugConfidence.Inferred);
        Zones[index] = replacement;
        SelectedZone = replacement;
        MarkDirty();
    }

    private void ApplyScanListEdit()
    {
        if (SelectedScanList is null) return;
        var index = ScanLists.IndexOf(SelectedScanList);
        if (index < 0) return;

        var name = string.IsNullOrWhiteSpace(EditScanListName) ? SelectedScanList.Name : EditScanListName.Trim();
        var channels = ParseCommaSeparatedList(EditScanListChannels);
        var replacement = new ScanList(name, channels, CodeplugConfidence.Inferred);
        ScanLists[index] = replacement;
        SelectedScanList = replacement;
        MarkDirty();
    }

    private void ApplyRxGroupEdit()
    {
        if (SelectedRxGroup is null) return;
        var index = RxGroups.IndexOf(SelectedRxGroup);
        if (index < 0) return;

        var name = string.IsNullOrWhiteSpace(EditRxGroupEditName) ? SelectedRxGroup.Name : EditRxGroupEditName.Trim();
        var contacts = ParseCommaSeparatedList(EditRxGroupContacts);
        var replacement = new RxGroup(name, contacts, CodeplugConfidence.Inferred, SelectedRxGroup.TalkGroupId);
        RxGroups[index] = replacement;
        SelectedRxGroup = replacement;
        MarkDirty();
    }

    private void ApplyGroupListEdit()
    {
        if (SelectedGroupList is null) return;
        var index = GroupLists.IndexOf(SelectedGroupList);
        if (index < 0) return;

        var name = string.IsNullOrWhiteSpace(EditGroupListEditName) ? SelectedGroupList.Name : EditGroupListEditName.Trim();
        var contacts = ParseCommaSeparatedList(EditGroupListContacts);
        var replacement = new GroupList(name, contacts, CodeplugConfidence.Inferred);
        GroupLists[index] = replacement;
        SelectedGroupList = replacement;
        MarkDirty();
    }

    private static IReadOnlyList<string> ParseCommaSeparatedList(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(s => s.Length > 0)
                   .ToArray();
    }

    #endregion

    /// <summary>
    /// Rebuilds the immutable <see cref="CodeplugImage"/> from the current editor state.
    /// Called before save or write-to-radio operations so the model reflects all edits.
    /// </summary>
    public CodeplugImage RebuildCodeplug()
    {
        var keyAssignments = new byte[KeyAssignmentTable.TableLength];
        Array.Copy(_codeplug.KeyAssignments.Assignments, keyAssignments, Math.Min(_codeplug.KeyAssignments.Assignments.Length, KeyAssignmentTable.TableLength));
        keyAssignments[KeyAssignmentTable.SK1Short] = (byte)SK1Short;
        keyAssignments[KeyAssignmentTable.SK1Long] = (byte)SK1Long;
        keyAssignments[KeyAssignmentTable.SK2Short] = (byte)SK2Short;
        keyAssignments[KeyAssignmentTable.SK2Long] = (byte)SK2Long;
        keyAssignments[KeyAssignmentTable.TKShort] = (byte)TKShort;
        keyAssignments[KeyAssignmentTable.TKLong] = (byte)TKLong;
        keyAssignments[KeyAssignmentTable.P1Short] = (byte)P1Short;
        keyAssignments[KeyAssignmentTable.P1Long] = (byte)P1Long;
        keyAssignments[KeyAssignmentTable.P2Short] = (byte)P2Short;
        keyAssignments[KeyAssignmentTable.P2Long] = (byte)P2Long;
        keyAssignments[KeyAssignmentTable.P3Short] = (byte)P3Short;
        keyAssignments[KeyAssignmentTable.P3Long] = (byte)P3Long;
        keyAssignments[KeyAssignmentTable.P4Short] = (byte)P4Short;
        keyAssignments[KeyAssignmentTable.P4Long] = (byte)P4Long;

        var updated = _codeplug with
        {
            GeneralSettings = _codeplug.GeneralSettings with
            {
                RadioName = RadioName,
                IntroLine1 = IntroLine1,
                IntroLine2 = IntroLine2,
            },
            DisplaySettings = _codeplug.DisplaySettings with
            {
                BacklightDuration = BacklightDuration,
            },
            PowerSettings = _codeplug.PowerSettings with
            {
                DefaultPower = DefaultPowerLevel,
            },
            RadioIdentity = _codeplug.RadioIdentity with
            {
                DmrId = DmrId,
                Callsign = Callsign,
            },
            SquelchSettings = _codeplug.SquelchSettings with
            {
                AnalogLevel = AnalogSquelch,
                DigitalLevel = DigitalSquelch,
            },
            ParameterSettings = _codeplug.ParameterSettings with
            {
                Language = Language,
                VoxEnabled = VoxEnabled,
                VoxLevel = VoxLevel,
                TxTimeout = TxTimeout,
                TxPreambleDuration = TxPreambleDuration,
                MicGain = MicGain,
                CtcssTailRevert = CtcssTailRevert,
                KeypadLockEnabled = KeypadLockEnabled,
            },
            KeyAssignments = new KeyAssignmentTable(keyAssignments, CodeplugConfidence.Confirmed),
            StartupScreen = _codeplug.StartupScreen with
            {
                Line1 = IntroLine1,
                Line2 = IntroLine2,
            },
            Channels = [.. Channels],
            Zones = [.. Zones],
            Contacts = [.. Contacts],
            GroupLists = [.. GroupLists],
            ScanLists = [.. ScanLists],
            RxGroups = [.. RxGroups],
        };
        _codeplug = updated;
        return updated;
    }

    public void Validate()
    {
        RebuildCodeplug();
        var issues = CodeplugValidator.Validate(Codeplug);
        if (issues.Count == 0)
        {
            ValidationSummary = $"✓ Validation passed — {ChannelCount} channel(s), {ZoneCount} zone(s), {ContactCount} contact(s), {GroupListCount} group list(s).";
        }
        else
        {
            var errors = issues.Count(i => i.Severity == ValidationSeverity.Error);
            var warnings = issues.Count(i => i.Severity == ValidationSeverity.Warning);
            var header = $"Validation: {errors} error(s), {warnings} warning(s)";
            ValidationSummary = header + Environment.NewLine + string.Join(Environment.NewLine,
                issues.Select(issue => $"  [{issue.Severity}] {issue.Message}"));
        }
    }

    private void MarkDirty()
    {
        IsDirty = true;
    }

    private void RaiseCountChanged()
    {
        OnPropertyChanged(nameof(ChannelCount));
        OnPropertyChanged(nameof(ZoneCount));
        OnPropertyChanged(nameof(ContactCount));
        OnPropertyChanged(nameof(GroupListCount));
        OnPropertyChanged(nameof(ScanListCount));
        OnPropertyChanged(nameof(RxGroupCount));
    }

    private void RaiseEditorCommandStates()
    {
        DeleteChannelCommand.RaiseCanExecuteChanged();
        ApplyChannelEditCommand.RaiseCanExecuteChanged();
        DeleteContactCommand.RaiseCanExecuteChanged();
        ApplyContactEditCommand.RaiseCanExecuteChanged();
        DeleteZoneCommand.RaiseCanExecuteChanged();
        ApplyZoneEditCommand.RaiseCanExecuteChanged();
        DeleteScanListCommand.RaiseCanExecuteChanged();
        ApplyScanListEditCommand.RaiseCanExecuteChanged();
        DeleteRxGroupCommand.RaiseCanExecuteChanged();
        ApplyRxGroupEditCommand.RaiseCanExecuteChanged();
        DeleteGroupListCommand.RaiseCanExecuteChanged();
        ApplyGroupListEditCommand.RaiseCanExecuteChanged();
    }

    private void RefreshSettingsFromCodeplug()
    {
        _radioName = _codeplug.GeneralSettings.RadioName;
        _introLine1 = _codeplug.GeneralSettings.IntroLine1;
        _introLine2 = _codeplug.GeneralSettings.IntroLine2;
        _dmrId = _codeplug.RadioIdentity.DmrId;
        _callsign = _codeplug.RadioIdentity.Callsign;
        _analogSquelch = _codeplug.SquelchSettings.AnalogLevel;
        _digitalSquelch = _codeplug.SquelchSettings.DigitalLevel;

        // Parameter settings
        _language = _codeplug.ParameterSettings.Language;
        _backlightDuration = _codeplug.DisplaySettings.BacklightDuration;
        _defaultPower = _codeplug.PowerSettings.DefaultPower;
        _voxEnabled = _codeplug.ParameterSettings.VoxEnabled;
        _voxLevel = _codeplug.ParameterSettings.VoxLevel;
        _txTimeout = _codeplug.ParameterSettings.TxTimeout;
        _txPreambleDuration = _codeplug.ParameterSettings.TxPreambleDuration;
        _micGain = _codeplug.ParameterSettings.MicGain;
        _ctcssTailRevert = _codeplug.ParameterSettings.CtcssTailRevert;
        _keypadLockEnabled = _codeplug.ParameterSettings.KeypadLockEnabled;

        // Key assignments
        var keys = _codeplug.KeyAssignments.Assignments;
        if (keys.Length >= KeyAssignmentTable.TableLength)
        {
            _sk1Short = (KeyFunction)keys[KeyAssignmentTable.SK1Short];
            _sk1Long = (KeyFunction)keys[KeyAssignmentTable.SK1Long];
            _sk2Short = (KeyFunction)keys[KeyAssignmentTable.SK2Short];
            _sk2Long = (KeyFunction)keys[KeyAssignmentTable.SK2Long];
            _tkShort = (KeyFunction)keys[KeyAssignmentTable.TKShort];
            _tkLong = (KeyFunction)keys[KeyAssignmentTable.TKLong];
            _p1Short = (KeyFunction)keys[KeyAssignmentTable.P1Short];
            _p1Long = (KeyFunction)keys[KeyAssignmentTable.P1Long];
            _p2Short = (KeyFunction)keys[KeyAssignmentTable.P2Short];
            _p2Long = (KeyFunction)keys[KeyAssignmentTable.P2Long];
            _p3Short = (KeyFunction)keys[KeyAssignmentTable.P3Short];
            _p3Long = (KeyFunction)keys[KeyAssignmentTable.P3Long];
            _p4Short = (KeyFunction)keys[KeyAssignmentTable.P4Short];
            _p4Long = (KeyFunction)keys[KeyAssignmentTable.P4Long];
        }

        OnPropertyChanged(nameof(RadioName));
        OnPropertyChanged(nameof(IntroLine1));
        OnPropertyChanged(nameof(IntroLine2));
        OnPropertyChanged(nameof(DmrId));
        OnPropertyChanged(nameof(Callsign));
        OnPropertyChanged(nameof(AnalogSquelch));
        OnPropertyChanged(nameof(DigitalSquelch));
        OnPropertyChanged(nameof(DefaultPower));

        // Parameter settings notifications
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(BacklightDuration));
        OnPropertyChanged(nameof(DefaultPowerLevel));
        OnPropertyChanged(nameof(VoxEnabled));
        OnPropertyChanged(nameof(VoxLevel));
        OnPropertyChanged(nameof(TxTimeout));
        OnPropertyChanged(nameof(TxPreambleDuration));
        OnPropertyChanged(nameof(MicGain));
        OnPropertyChanged(nameof(CtcssTailRevert));
        OnPropertyChanged(nameof(KeypadLockEnabled));

        // Key assignment notifications
        OnPropertyChanged(nameof(SK1Short));
        OnPropertyChanged(nameof(SK1Long));
        OnPropertyChanged(nameof(SK2Short));
        OnPropertyChanged(nameof(SK2Long));
        OnPropertyChanged(nameof(TKShort));
        OnPropertyChanged(nameof(TKLong));
        OnPropertyChanged(nameof(P1Short));
        OnPropertyChanged(nameof(P1Long));
        OnPropertyChanged(nameof(P2Short));
        OnPropertyChanged(nameof(P2Long));
        OnPropertyChanged(nameof(P3Short));
        OnPropertyChanged(nameof(P3Long));
        OnPropertyChanged(nameof(P4Short));
        OnPropertyChanged(nameof(P4Long));
    }

    private void RefreshCollections()
    {
        Channels.Clear();
        foreach (var ch in _codeplug.Channels) Channels.Add(ch);

        Zones.Clear();
        foreach (var z in _codeplug.Zones) Zones.Add(z);

        Contacts.Clear();
        foreach (var c in _codeplug.Contacts) Contacts.Add(c);

        GroupLists.Clear();
        foreach (var gl in _codeplug.GroupLists) GroupLists.Add(gl);

        ScanLists.Clear();
        foreach (var sl in _codeplug.ScanLists) ScanLists.Add(sl);

        RxGroups.Clear();
        foreach (var rg in _codeplug.RxGroups) RxGroups.Add(rg);

        SelectedChannel = null;
        SelectedContact = null;
        SelectedZone = null;
        SelectedScanList = null;
        SelectedRxGroup = null;
        SelectedGroupList = null;

        RaiseCountChanged();
        RaiseEditorCommandStates();
    }
}
