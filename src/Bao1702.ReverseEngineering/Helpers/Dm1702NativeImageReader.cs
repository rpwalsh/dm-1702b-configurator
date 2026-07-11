using System.Buffers.Binary;
using System.Text;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Reads a raw 245,760-byte DM-1702 native codeplug image back into a <see cref="CodeplugImage"/> model.
/// The raw image is preserved in <see cref="CodeplugImage.PreservedRawImage"/> for round-trip fidelity.
/// </summary>
public static class Dm1702NativeImageReader
{
    private const int PrivateType = 0x3;
    private const int GroupType = 0x4;
    private const int AllCallType = 0x5;

    public static CodeplugImage ReadFromNative(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length != Dm1702NativeImageAssumptions.ImageLength)
        {
            throw new ArgumentException($"Native image must be exactly {Dm1702NativeImageAssumptions.ImageLength} bytes, got {imageBytes.Length}.");
        }

        var image = imageBytes.AsSpan();
        var config = image.Slice(Dm1702NativeImageAssumptions.ConfigStart, Dm1702NativeImageAssumptions.ConfigLength);

        var contacts = ReadContacts(image);
        var rxGroups = ReadRxGroups(image, contacts);
        var channels = ReadChannels(image, contacts, rxGroups);
        var zones = ReadZones(image, channels);
        var scanLists = ReadScanLists(image, channels);
        var generalSettings = ReadGeneralSettings(config);
        var displaySettings = ReadDisplaySettings(config);
        var powerSettings = ReadPowerSettings(config);
        var squelchSettings = ReadSquelchSettings(config);
        var radioIdentity = ReadRadioIdentity(config);
        var startupScreen = ReadStartupScreen(config);
        var keyAssignments = ReadKeyAssignments(config);
        var dtmfConfig = ReadDtmfConfig(config);
        var parameterSettings = ReadParameterSettings(config);
        var loneWorkerConfig = ReadLoneWorkerConfig(image);
        var privacyEntries = ReadPrivacyEntries(image);
        var emergencySystems = ReadEmergencySystems(image);
        var quickTextMessages = ReadQuickTextMessages(image);
        var emergencyConfig = new EmergencyConfig(0, CodeplugConfidence.Preserved);

        return new CodeplugImage(
            generalSettings,
            displaySettings,
            powerSettings,
            squelchSettings,
            parameterSettings,
            startupScreen,
            radioIdentity,
            new ButtonConfig("Monitor", "Scan", "Power", "Zone", CodeplugConfidence.Preserved),
            keyAssignments,
            dtmfConfig,
            new PrivacyConfig(false, 0, CodeplugConfidence.Preserved),
            privacyEntries,
            emergencySystems,
            emergencyConfig,
            loneWorkerConfig,
            quickTextMessages,
            channels,
            zones,
            contacts,
            [],
            scanLists,
            rxGroups,
            [],
            (byte[])imageBytes.Clone());
    }

    #region Config readers

    private static GeneralSettings ReadGeneralSettings(ReadOnlySpan<byte> config)
    {
        var radioName = ReadAscii(config.Slice(0x180, 16));
        var introLine1 = ReadAscii(config.Slice(0x1C0, 16));
        var introLine2 = ReadAscii(config.Slice(0x1D0, 16));
        return new GeneralSettings(radioName, introLine1, introLine2, CodeplugConfidence.Preserved);
    }

    private static DisplaySettings ReadDisplaySettings(ReadOnlySpan<byte> config)
    {
        var backlightDuration = (BacklightDuration)Math.Clamp((int)config[0x00], 0, 5);
        var showChannelNumber = (config[0x07] & 0x40) != 0;
        var showClock = config[0x0B] == 0x48;
        return new DisplaySettings(backlightDuration, showChannelNumber, showClock, CodeplugConfidence.Preserved);
    }

    private static PowerSettings ReadPowerSettings(ReadOnlySpan<byte> config)
    {
        var defaultPower = config[0x0C] switch
        {
            0x00 => PowerLevel.Low,
            0x01 => PowerLevel.Medium,
            _ => PowerLevel.High,
        };
        var batterySaver = (config[0x0D] & 0x0C) != 0;
        return new PowerSettings(defaultPower, batterySaver, CodeplugConfidence.Preserved);
    }

    private static SquelchSettings ReadSquelchSettings(ReadOnlySpan<byte> config)
    {
        return new SquelchSettings(config[0x02], config[0x0E], CodeplugConfidence.Preserved);
    }

    private static RadioIdentitySettings ReadRadioIdentity(ReadOnlySpan<byte> config)
    {
        var dmrIdRaw = BinaryPrimitives.ReadUInt32LittleEndian(config.Slice(0x30, 4));
        var dmrId = dmrIdRaw.ToString();
        // Callsign is stored at radio name offset in some configurations
        // but the canonical source is the APRS GPS entry. Use radio name as fallback.
        var radioName = ReadAscii(config.Slice(0x180, 16));
        return new RadioIdentitySettings(dmrId, radioName, CodeplugConfidence.Preserved);
    }

    private static StartupScreen ReadStartupScreen(ReadOnlySpan<byte> config)
    {
        var line1 = ReadAscii(config.Slice(0x1C0, 16));
        var line2 = ReadAscii(config.Slice(0x1D0, 16));
        return new StartupScreen(line1, line2, CodeplugConfidence.Preserved);
    }

    private static KeyAssignmentTable ReadKeyAssignments(ReadOnlySpan<byte> config)
    {
        var assignments = config.Slice(0x150, KeyAssignmentTable.TableLength).ToArray();
        return new KeyAssignmentTable(assignments, CodeplugConfidence.Preserved);
    }

    private static DtmfConfig ReadDtmfConfig(ReadOnlySpan<byte> config)
    {
        var pttId = ReadAscii(config.Slice(0x192, 10));
        var killCode = ReadAscii(config.Slice(0x19C, 8));
        var reviveCode = ReadAscii(config.Slice(0x1A4, 8));
        return new DtmfConfig(pttId, killCode, reviveCode, CodeplugConfidence.Preserved);
    }

    /// <summary>
    /// Binary evidence: Language at +0x200 (0=Chinese, 1=English).
    /// VOX enable at +0x003 b0. Keypad lock at +0x003 b5. CTCSS tail revert at +0x003 b6.
    /// VOX Level at +0x072, Mic Gain at +0x070, TX Timeout at +0x073, TX Preamble at +0x075.
    /// </summary>
    private static ParameterSettings ReadParameterSettings(ReadOnlySpan<byte> config)
    {
        var language = config[0x200] == 0x01 ? Language.English : Language.Chinese;
        var voxEnabled = (config[0x03] & 0x01) != 0;
        var keypadLock = (config[0x03] & 0x20) != 0;
        var ctcssTailRevert = (config[0x03] & 0x40) != 0;
        var micGain = Math.Clamp((int)config[0x70], 1, 10);
        var voxLevel = Math.Clamp((int)config[0x72], 1, 10);
        var txTimeout = config[0x73]; // raw value, units TBD (likely x15sec or direct seconds count)
        var txPreamble = config[0x75]; // raw value
        return new ParameterSettings(language, voxEnabled, voxLevel, txTimeout, txPreamble, micGain, ctcssTailRevert, keypadLock, CodeplugConfidence.Preserved);
    }

    private static LoneWorkerConfig ReadLoneWorkerConfig(ReadOnlySpan<byte> image)
    {
        // Lone worker settings at RxListsStart + LoneWorkerOffset (0x8B00)
        var lwBase = Dm1702NativeImageAssumptions.RxListsStart + Dm1702NativeImageAssumptions.LoneWorkerOffset;
        if (lwBase + 3 > image.Length) return new LoneWorkerConfig(false, 10, 10, CodeplugConfidence.Unknown);
        var enabled = image[lwBase] != 0;
        var responseMinutes = image[lwBase + 1];
        var reminderSeconds = image[lwBase + 2];
        return new LoneWorkerConfig(enabled, responseMinutes, reminderSeconds, CodeplugConfidence.Preserved);
    }

    private static IReadOnlyList<PrivacyEntry> ReadPrivacyEntries(ReadOnlySpan<byte> image)
    {
        // to recover the same count (structurally generated round-trip). Header byte semantics in
        // OEM default entries are not fully characterized but do not affect user-configured entries.
        // Entries at 0x8801, stride 0x17 (23 bytes).
        // Each: 10-byte name + 1-byte key type + 8-byte key data (0xFF padded) + 4-byte footer.
        var headerOffset = Dm1702NativeImageAssumptions.RxListsStart + Dm1702NativeImageAssumptions.PrivacyHeaderOffset;
        if (headerOffset >= image.Length) return [];

        var count = image[headerOffset];
        if (count == 0) return [];

        var entries = new List<PrivacyEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var entryOffset = Dm1702NativeImageAssumptions.RxListsStart
                + Dm1702NativeImageAssumptions.PrivacyEntryStart
                + (i * Dm1702NativeImageAssumptions.PrivacyEntryStride);
            if (entryOffset + Dm1702NativeImageAssumptions.PrivacyEntryStride > image.Length) break;

            var record = image.Slice(entryOffset, Dm1702NativeImageAssumptions.PrivacyEntryStride);
            var name = ReadAscii(record.Slice(0, Dm1702NativeImageAssumptions.PrivacyNameLength));
            var keyType = record[0x0A];
            var keyData = record.Slice(0x0B, Dm1702NativeImageAssumptions.PrivacyKeyDataLength).ToArray();
            var footer = record.Slice(0x13, Dm1702NativeImageAssumptions.PrivacyFooterLength).ToArray();

            entries.Add(new PrivacyEntry(name, keyType, keyData, footer, CodeplugConfidence.Preserved));
        }

        return entries;
    }

    private static IReadOnlyList<EmergencySystem> ReadEmergencySystems(ReadOnlySpan<byte> image)
    {
        // to recover the same count (structurally generated round-trip). Count byte semantics in
        // OEM default entries are not fully characterized but do not affect user-configured entries.
        // Entries at 0x8620, stride 0x14 (20 bytes).
        // Each: 10-byte name + Type + Mode + RevertCh + AlarmCallToFollow + ImpoliteRetries +
        // PoliteRetries + VoiceCycles + Reserved + HotMicDuration + RxIntervalDuration.
        var countOffset = Dm1702NativeImageAssumptions.RxListsStart + Dm1702NativeImageAssumptions.EmergencyCountOffset;
        if (countOffset >= image.Length) return [];

        var count = image[countOffset];
        if (count == 0) return [];

        var tableBase = Dm1702NativeImageAssumptions.RxListsStart + Dm1702NativeImageAssumptions.EmergencyTableOffset;
        var entries = new List<EmergencySystem>(count);
        for (var i = 0; i < count; i++)
        {
            var entryOffset = tableBase + (i * Dm1702NativeImageAssumptions.EmergencyEntryStride);
            if (entryOffset + Dm1702NativeImageAssumptions.EmergencyEntryStride > image.Length) break;

            var record = image.Slice(entryOffset, Dm1702NativeImageAssumptions.EmergencyEntryStride);
            var name = ReadAscii(record.Slice(0, 10));

            entries.Add(new EmergencySystem(
                name,
                record[0x0A], record[0x0B], record[0x0C], record[0x0D],
                record[0x0E], record[0x0F], record[0x10], record[0x11],
                record[0x12], record[0x13],
                CodeplugConfidence.Preserved));
        }

        return entries;
    }

    private static IReadOnlyList<QuickTextMessage> ReadQuickTextMessages(ReadOnlySpan<byte> image)
    {
        var baseOffset = Dm1702NativeImageAssumptions.QuickTextStart;
        if (baseOffset >= image.Length) return [];

        var count = image[baseOffset];
        if (count == 0) return [];

        var messages = new List<QuickTextMessage>(count);
        for (var i = 0; i < count; i++)
        {
            var msgOffset = baseOffset
                + Dm1702NativeImageAssumptions.QuickTextHeaderLength
                + (i * Dm1702NativeImageAssumptions.QuickTextMessageStride);
            if (msgOffset + Dm1702NativeImageAssumptions.QuickTextMessageStride > image.Length) break;

            var msgSpan = image.Slice(msgOffset, Dm1702NativeImageAssumptions.QuickTextMessageStride);
            var textLength = Math.Min(msgSpan[0], (byte)(Dm1702NativeImageAssumptions.QuickTextMessageStride - 1));
            var text = textLength > 0
                ? Encoding.ASCII.GetString(msgSpan.Slice(1, textLength))
                : string.Empty;

            messages.Add(new QuickTextMessage(text, CodeplugConfidence.Preserved));
        }

        return messages;
    }

    #endregion

    #region Contact reader

    private static IReadOnlyList<Contact> ReadContacts(ReadOnlySpan<byte> image)
    {
        var contacts = new List<Contact>();
        var stride = Dm1702NativeImageAssumptions.ContactRecordLength;

        for (int i = 0; i < Dm1702NativeImageAssumptions.ContactDataCapacity; i++)
        {
            var offset = Dm1702NativeContactSerializer.GetContactRecordImageOffset(i);
            if (offset + stride > image.Length) break;

            var record = image.Slice(offset, stride);

            // OEM layout: [0..1]=FF, [2..17]=name (ASCII/GB2312, 0x00 padded), [18]=FF,
            // [19..21]=callId (LE24), [22]=type, [23]=FF.
            // Empty slot: name area [2..17] is all 0xFF.
            var nameSlice = record.Slice(2, 16);
            var allFF = true;
            for (int j = 0; j < nameSlice.Length; j++)
            {
                if (nameSlice[j] != 0xFF)
                {
                    allFF = false;
                    break;
                }
            }
            if (allFF) break;

            var name = ReadAscii(nameSlice);
            if (string.IsNullOrWhiteSpace(name)) break;

            var callId = record[19] | (record[20] << 8) | (record[21] << 16);
            var typeByte = record[22];
            var contactType = typeByte switch
            {
                PrivateType => ContactType.Private,
                GroupType => ContactType.Group,
                AllCallType => ContactType.AllCall,
                _ => ContactType.Group,
            };

            contacts.Add(new Contact(name, callId, contactType, CodeplugConfidence.Preserved));
        }

        return contacts;
    }

    #endregion

    #region Channel reader

    private static IReadOnlyList<Channel> ReadChannels(ReadOnlySpan<byte> image, IReadOnlyList<Contact> contacts, IReadOnlyList<RxGroup> rxGroups)
    {
        var channels = new List<Channel>();
        var stride = Dm1702NativeImageAssumptions.ChannelRecordStride;
        var maxChannels = Dm1702NativeImageAssumptions.MaxSupportedPatchedChannelIndex + 1; // 256

        for (int i = 0; i < maxChannels; i++)
        {
            var offset = Dm1702NativeImageAssumptions.GetChannelRecordOffset(i);
            if (offset + stride > image.Length) break;

            var record = image.Slice(offset, stride);

            // Read RX frequency as BCD
            var rxFreqHz = ReadBcdFrequency(record);
            if (rxFreqHz == 0) continue; // Empty channel slot

            var txFreqHz = ReadBcdFrequency(record.Slice(4));
            var modeFlags = record[0x08];
            var isDigital = (modeFlags & 0x40) != 0;
            var isHighPower = (modeFlags & 0x02) != 0;
            var isWideBandwidth = (modeFlags & 0x01) != 0;
            var power = isHighPower ? PowerLevel.High : PowerLevel.Low;
            var bandwidth = isWideBandwidth ? ChannelBandwidth.Wide : ChannelBandwidth.Narrow;

            // Channel name from name tables
            var name = ReadChannelName(image, i);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"CH{i + 1}";
            }

            // Admit criteria from +0x0D
            var admitByte = record[0x0D] & 0x1F; // Mask off EmergAlarmAck bit
            AdmitCriteria admit;
            if (isDigital)
            {
                admit = admitByte switch
                {
                    0x10 => AdmitCriteria.ColorCodeFree,
                    0x08 => AdmitCriteria.ChannelFree,
                    _ => AdmitCriteria.Always,
                };
            }
            else
            {
                admit = admitByte switch
                {
                    0x01 => AdmitCriteria.ChannelFree,
                    0x02 => AdmitCriteria.ColorCodeFree,
                    _ => AdmitCriteria.Always,
                };
            }

            // Reconstruct extended semantics from the channel record bytes.
            var secondaryFlags = record[0x09];
            var scanEmergFlags = record[0x0B];
            var scanListRaw = record[0x0C];
            var encryptKeyIndex = record[0x10];
            var rxGroupRaw = record[0x11];
            var gpsRaw = record[0x12];

            var rxOnly = (modeFlags & 0x20) != 0;
            var loneWorker = (modeFlags & 0x08) != 0;
            var encryptionEnabled = (secondaryFlags & 0x80) != 0;
            var doubleCapacity = (secondaryFlags & 0x01) != 0;
            var autoScan = (scanEmergFlags & 0x80) != 0;
            var emergAlarmInd = (scanEmergFlags & 0x20) != 0;
            var emergCallInd = (scanEmergFlags & 0x01) != 0;
            var emergAlarmAck = (record[0x0D] & 0x20) != 0;
            var privateCallConfirmed = (record[0x0F] & 0x80) != 0;
            var shortDataMessage = (record[0x0F] & 0x01) != 0;
            var talkAroundEnabled = (record[0x17] & 0x10) != 0;
            var displayPttId = (record[0x18] & 0x80) != 0;
            var talkAroundStatus = (record[0x1A] & 0x40) != 0;
            var voxEnabled = (record[0x1A] & 0x10) != 0;

            // +0x0C: 0-based scan list index; use AutoScan flag to distinguish index-0 from no-list.
            // Writer: if hasScanList ? writes 0-based index (so index 1 ? 0x00), AutoScan bit set.
            //         if no scan list ? writes 0x00, AutoScan bit clear.
            int? scanListIndex = autoScan ? (int?)scanListRaw + 1 : null;
            // +0x12: 0 means no GPS system assigned
            int? gpsSystemIndex = gpsRaw > 0 ? (int?)gpsRaw : null;
            // +0x10: 0x01 sentinel means encryption off with no key; otherwise 0-based key index
            var effectiveKeyIndex = encryptionEnabled ? encryptKeyIndex : 0;

            var semantics = new Dm1702NativeSemantics(
                RxOnly: rxOnly,
                VoxEnabled: voxEnabled,
                TalkAroundEnabled: talkAroundEnabled,
                LoneWorkerEnabled: loneWorker,
                AutoScanEnabled: autoScan,
                EmergencyAlarmIndication: emergAlarmInd,
                EmergencyAlarmAck: emergAlarmAck,
                EmergencyCallIndication: emergCallInd,
                PrivateCallConfirmed: privateCallConfirmed,
                ShortDataMessage: shortDataMessage,
                EncryptionEnabled: encryptionEnabled,
                EncryptionKeyIndex: effectiveKeyIndex,
                DoubleCapacityMode: doubleCapacity,
                TalkAroundStatus: talkAroundStatus,
                DisplayPttId: displayPttId,
                GpsSystemIndex: gpsSystemIndex,
                ScanListIndex: scanListIndex,
                EmergencySystemIndex: null,
                PttKeyupMode: null,
                PttKeyupEncodeType: null,
                Confidence: CodeplugConfidence.Preserved);

            if (isDigital)
            {
                var tsCC = record[0x0E];
                var timeSlot = (tsCC & 0x10) != 0 ? 2 : 1;
                var colorCode = tsCC & 0x0F;

                // Look up contact name from channel contact map at 0x1E000.
                // Decode: (highByte >> 4) * 0x100 + lowByte ? 0-based table index.
                string? contactName = null;
                var mapOffset = Dm1702NativeImageAssumptions.ChannelContactMapStart + i * 2;
                if (mapOffset + 2 <= image.Length)
                {
                    var mapHigh = image[mapOffset];
                    var mapLow = image[mapOffset + 1];
                    var contactIndex = ((mapHigh >> 4) * 0x100) + mapLow;
                    if (contactIndex >= 0 && contactIndex < contacts.Count)
                    {
                        contactName = contacts[contactIndex].Name;
                    }
                }

                // +0x11: RxGroup index (1-based); 0xC0 sentinel means None
                string? rxGroupName = null;
                if (rxGroupRaw != 0xC0 && rxGroupRaw > 0 && rxGroupRaw <= rxGroups.Count)
                {
                    rxGroupName = rxGroups[rxGroupRaw - 1].Name;
                }

                channels.Add(new DigitalChannel(
                    i + 1, name, rxFreqHz, txFreqHz,
                    power, bandwidth, admit, [],
                    colorCode, timeSlot, contactName, rxGroupName,
                    CodeplugConfidence.Preserved, semantics));
            }
            else
            {
                var rxTone = ReadTone(record.Slice(0x13, 2));
                var txTone = ReadTone(record.Slice(0x15, 2));

                channels.Add(new AnalogChannel(
                    i + 1, name, rxFreqHz, txFreqHz,
                    power, bandwidth, admit, [],
                    rxTone, txTone,
                    CodeplugConfidence.Preserved, semantics));
            }
        }

        return channels;
    }

    private static string ReadChannelName(ReadOnlySpan<byte> image, int zeroBasedIndex)
    {
        // Contiguous name table: all indices at 0x4000 + index*11.
        var nameOffset = Dm1702NativeImageAssumptions.GetChannelNameOffset(zeroBasedIndex);

        if (nameOffset + Dm1702NativeImageAssumptions.ChannelNameStride > image.Length) return string.Empty;
        return ReadAscii(image.Slice(nameOffset, Dm1702NativeImageAssumptions.ChannelNameStride));
    }

    private static long ReadBcdFrequency(ReadOnlySpan<byte> data)
    {
        // OEM encoding: 4-byte little-endian packed BCD. LSB pair in data[0], MSB pair in data[3].
        // Example: 162.4 MHz = 16240000 ? stored as [0x00, 0x00, 0x24, 0x16] ? read MSB-first as [data[3],data[2],data[1],data[0]].
        // Example: 400.0 MHz = 40000000 ? stored as [0x00, 0x00, 0x00, 0x40] ? "40000000" ? 40000000 * 10 = 400_000_000 Hz.
        ReadOnlySpan<byte> ordered = [data[3], data[2], data[1], data[0]];
        long value = 0;
        for (int i = 0; i < 4; i++)
        {
            var high = (ordered[i] >> 4) & 0x0F;
            var low = ordered[i] & 0x0F;
            if (high > 9 || low > 9) return 0; // Invalid BCD
            value = value * 100 + high * 10 + low;
        }

        // Value is in 10Hz units, convert to Hz
        return value * 10;
    }

    #endregion

    #region Zone reader

    private static IReadOnlyList<Zone> ReadZones(ReadOnlySpan<byte> image, IReadOnlyList<Channel> channels)
    {
        var zones = new List<Zone>();
        const int zoneStride = Dm1702NativeImageAssumptions.ZoneRecordStride; // 0x112
        const int maxZones = Dm1702NativeImageAssumptions.ZoneRecordCapacity; // 250
        const int linearCap = Dm1702NativeImageAssumptions.LinearZoneCapacity; // 14
        const int maxMembers = Dm1702NativeImageAssumptions.MaxMembersPerZone; // 64

        // Zone count byte at file 0x6000
        var zoneCount = image[Dm1702NativeImageAssumptions.ZoneDataStart];
        if (zoneCount == 0 || zoneCount > maxZones) zoneCount = 0;

        for (int i = 0; i < zoneCount; i++)
        {
            var offset = Dm1702NativeImageAssumptions.GetZoneRecordOffset(i);
            if (offset + zoneStride > image.Length) break;

            var record = image.Slice(offset, zoneStride);
            var isLinear = i < linearCap;

            // Layout differs between linear and paged records
            int nameOffset = isLinear ? 0x10 : 0x00;
            int memberCountOffset = isLinear ? 0x20 : 0x10;
            int memberListOffset = isLinear ? 0x21 : 0x11;

            var memberCount = record[memberCountOffset];
            if (memberCount == 0 || memberCount == 0xFF) continue;
            if (memberCount > maxMembers) memberCount = (byte)maxMembers;

            var name = ReadAscii(record.Slice(nameOffset, 0x10));
            if (string.IsNullOrWhiteSpace(name)) continue;

            var channelNames = new List<string>();
            for (int m = 0; m < memberCount; m++)
            {
                var mOffset = memberListOffset + (m * 2);
                if (mOffset + 2 > zoneStride) break;
                var channelIndex = record[mOffset] | (record[mOffset + 1] << 8);
                if (channelIndex == 0 || channelIndex == 0xFFFF) break;

                var ch = channels.FirstOrDefault(c => c.Index == channelIndex);
                channelNames.Add(ch?.Name ?? $"CH{channelIndex}");
            }

            zones.Add(new Zone(name, channelNames, CodeplugConfidence.Preserved));
        }

        return zones;
    }

    #endregion

    #region RxGroup reader

    private static IReadOnlyList<RxGroup> ReadRxGroups(ReadOnlySpan<byte> image, IReadOnlyList<Contact> contacts)
    {
        var rxGroups = new List<RxGroup>();
        const int rxStride = Dm1702NativeImageAssumptions.RxGroupRecordStride; // 0x6D
        const int maxGroups = Dm1702NativeImageAssumptions.RxGroupCapacity;

        for (int i = 0; i < maxGroups; i++)
        {
            var offset = Dm1702NativeRxGroupSerializer.GetRecordImageOffset(i);
            if (offset + rxStride > image.Length) break;

            var record = image.Slice(offset, rxStride);

            // Name at +0x01, 11 bytes ASCII — OEM-confirmed
            var name = ReadAscii(record.Slice(0x01, 11));
            if (string.IsNullOrWhiteSpace(name)) continue;

            var talkGroupId = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x0C, 2));

            // Members at +0x12, 3-byte entries (LE16 1-based contact index + 0x00 pad), terminated by 0x0000, up to 30
            var contactNames = new List<string>();
            for (int m = 0; m < 30; m++)
            {
                var entryOffset = 0x12 + m * 3;
                if (entryOffset + 2 > record.Length) break;

                var contactIndex = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(entryOffset, 2));
                if (contactIndex == 0 || contactIndex == 0xFFFF) break;

                var contact = contactIndex >= 1 && contactIndex <= contacts.Count
                    ? contacts[contactIndex - 1]
                    : null;
                contactNames.Add(contact?.Name ?? $"Contact{contactIndex}");
            }

            rxGroups.Add(new RxGroup(name, contactNames, CodeplugConfidence.Preserved, talkGroupId));
        }

        return rxGroups;
    }

    #endregion

    #region ScanList reader

    private static IReadOnlyList<ScanList> ReadScanLists(ReadOnlySpan<byte> image, IReadOnlyList<Channel> channels)
    {
        var scanLists = new List<ScanList>();
        const int slStart = Dm1702NativeImageAssumptions.ScanListsStart; // 0xB000
        const int slStride = Dm1702NativeImageAssumptions.ScanListRecordStride; // 0x39
        const int maxScanLists = Dm1702NativeImageAssumptions.ScanListCapacity; // 32

        // Scan list count at image[0xB000] — same pattern as zone count at 0x6000.
        var slCount = image[slStart];
        if (slCount == 0 || slCount > maxScanLists) slCount = 0;

        for (int i = 0; i < slCount; i++)
        {
            var offset = slStart + (i * slStride);
            if (offset + slStride > image.Length) break;

            var record = image.Slice(offset, slStride);

            // Name at +0x01 (11 bytes)
            var name = ReadAscii(record.Slice(0x01, 11));
            if (string.IsNullOrWhiteSpace(name)) continue;

            var memberCount = Math.Min((int)record[0x0C], 16);

            var channelNames = new List<string>();
            for (int m = 0; m < memberCount; m++)
            {
                var memberOffset = 0x19 + (m * 2);
                if (memberOffset + 2 > slStride) break;
                var channelIndex = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(memberOffset, 2));
                if (channelIndex == 0 || channelIndex == 0xFFFF) continue;

                var ch = channels.FirstOrDefault(c => c.Index == channelIndex);
                channelNames.Add(ch?.Name ?? $"CH{channelIndex}");
            }

            if (channelNames.Count > 0)
            {
                scanLists.Add(new ScanList(name, channelNames, CodeplugConfidence.Preserved));
            }
        }

        return scanLists;
    }

    #endregion

    #region Helpers

    private static ToneValue ReadTone(ReadOnlySpan<byte> data)
    {
        var lo = data[0];
        var hi = data[1];

        // No tone: 0xFFFF
        if (lo == 0xFF && hi == 0xFF) return ToneValue.None;

        // DCS: high byte has b7 set (0x80 or 0xC0)
        if ((hi & 0x80) != 0)
        {
            // Low byte is BCD of last two DCS digits
            var lastTwo = ((lo >> 4) * 10) + (lo & 0x0F);
            // High byte: Normal = 0x80|firstDigit, Inverted = 0xC0|(code&0x0F)
            bool isInverted = (hi & 0x40) != 0;
            if (isInverted)
            {
                // Inverted: firstDigit is not directly encoded. DCS codes are 0-7 in first digit.
                // High byte = 0xC0|(numericValue & 0x0F). We need to reconstruct the 3-digit code.
                // The first digit can be recovered from context: DCS codes 023-754 have first digits 0-7.
                // We store the low nibble of the full numeric as bits 3:0 of high byte.
                // numericValue & 0x0F = high & 0x0F; lastTwo gives the bottom two decimal digits.
                // The first digit is floor(numeric/100). Since numeric = first*100 + lastTwo and
                // numeric & 0x0F = (first*100 + lastTwo) & 0x0F, we cannot directly recover first digit.
                // Use the same approach as in the serializer comment: for well-known DCS codes, the
                // last nibble of the full code uniquely identifies it along with the last two digits.
                // Reconstruct by brute-force: find first digit d in 0..7 such that (d*100+lastTwo)&0x0F == hi&0x0F
                var lsNibble = hi & 0x0F;
                var firstDigit = 0;
                for (int d = 0; d <= 7; d++)
                {
                    if (((d * 100 + lastTwo) & 0x0F) == lsNibble)
                    {
                        firstDigit = d;
                        break;
                    }
                }
                var numericValue = firstDigit * 100 + lastTwo;
                return ToneValue.Parse($"D{numericValue:D3}I");
            }
            else
            {
                // Normal: high byte = 0x80 | firstDigit
                var firstDigit = hi & 0x0F;
                var numericValue = firstDigit * 100 + lastTwo;
                return ToneValue.Parse($"D{numericValue:D3}N");
            }
        }

        // CTCSS: two BCD bytes encoding freq*10 in 4 digits (lo = digits[2..3], hi = digits[0..1])
        var loHigh = (lo >> 4) & 0x0F;
        var loLow = lo & 0x0F;
        var hiHigh = (hi >> 4) & 0x0F;
        var hiLow = hi & 0x0F;
        if (loHigh > 9 || loLow > 9 || hiHigh > 9 || hiLow > 9) return ToneValue.None;
        var raw = hiHigh * 1000 + hiLow * 100 + loHigh * 10 + loLow;
        var freqMHz = raw / 10m;
        return ToneValue.Parse(freqMHz.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string ReadAscii(ReadOnlySpan<byte> data)
    {
        var end = data.Length;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0x00 || data[i] == 0xFF)
            {
                end = i;
                break;
            }
        }

        if (end == 0) return string.Empty;
        return Encoding.ASCII.GetString(data[..end]).TrimEnd();
    }

    #endregion
}
