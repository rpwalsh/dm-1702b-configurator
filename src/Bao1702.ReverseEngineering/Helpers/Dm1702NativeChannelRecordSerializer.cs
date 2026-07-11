using System.Globalization;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Serializes a single <see cref="Channel"/> into the 48-byte (0x30-stride) DM-1702 native
/// channel record format, encoding frequencies as BCD and packing mode/flag bits.
/// </summary>
public static class Dm1702NativeChannelRecordSerializer
{
    public static void Write(
        Span<byte> image,
        Channel channel,
        int zeroBasedIndex,
        IReadOnlyList<Contact> contacts,
        IReadOnlyList<RxGroup> rxGroups,
        IReadOnlyList<ScanList> scanLists,
        string? gpsSystemName)
    {
        var recordOffset = Dm1702NativeImageAssumptions.GetChannelRecordOffset(zeroBasedIndex);
        if (recordOffset + Dm1702NativeImageAssumptions.ChannelRecordStride > image.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex), "Channel record exceeds native DM1702 image bounds.");
        }

        var record = image.Slice(recordOffset, Dm1702NativeImageAssumptions.ChannelRecordStride);
        record.Fill(0x00);

        WriteObservedFrequency(record, 0x00, channel.RxFrequencyHz / 1_000_000d);
        WriteObservedFrequency(record, 0x04, channel.TxFrequencyHz / 1_000_000d);

        // b6=Digital, b5=RxOnly, b3=LoneWorker, b1=HighPower, b0=WideBandwidth
        record[0x08] = BuildModeFlags(channel);

        // b7=EncryptEnabled, b0=DoubleCapacityMode
        record[0x09] = BuildSecondaryFlags(channel);


        var scanListIndex = FindScanListIndex(scanLists, channel.Name);
        var semantics = channel.NativeSemantics;
        var effectiveScanListIndex = semantics?.ScanListIndex ?? scanListIndex;

        // b7=AutoStartScan, b5=EmergAlarmIndication, b0=EmergCallIndication
        record[0x0B] = BuildScanEmergencyFlags(semantics, effectiveScanListIndex.HasValue);

        record[0x0C] = (byte)(effectiveScanListIndex.HasValue ? Math.Max(effectiveScanListIndex.Value - 1, 0) : 0x00);

        // Admit: Digital Always=0x00, ChannelFree=0x08, ColorCodeFree=0x10.
        //        Analog  Always=0x00, ChannelFree=0x01, CTCSS/CDCSS=0x02.
        // EmergAlarmAck is a scatter bit at b5 of this same byte.
        // Evidence: baseline Ch2 +0x0D=0x30 = CCFree(0x10) + EmergAlarmAck(0x20).
        record[0x0D] = (byte)(BuildAdmitCriteria(channel) | (semantics?.EmergencyAlarmAck == true ? 0x20 : 0x00));

        if (channel is DigitalChannel digital)
        {
            // TS1=0x00, TS2=0x10. CC in low nibble directly.
            record[0x0E] = (byte)(
                EncodeTimeSlot(digital.TimeSlot) |
                Math.Clamp(digital.ColorCode, 0, 15) |
                (semantics?.EmergencyCallIndication == true ? 0x20 : 0x00));

            record[0x0F] = (byte)(
                (semantics?.PrivateCallConfirmed == true ? 0x80 : 0x00) |
                (semantics?.ShortDataMessage == true ? 0x01 : 0x00));

            // Encrypt ON: 0-based key index (baseline Ch2 Privacy1 ? 0x00).
            // Analog/unconfigured: 0x00 (baseline Ch1).
            record[0x10] = (byte)(semantics?.EncryptionEnabled == true
                ? Math.Clamp(semantics.EncryptionKeyIndex, 0, 0x0F)
                : (channel is DigitalChannel ? 0x01 : 0x00));

            // Assigned group: 1-based index (baseline Ch2 List1 ? 0x01).
            // Analog/unconfigured: 0x00 (baseline Ch1).
            record[0x11] = (byte)(FindRxGroupIndex(rxGroups, digital.RxGroupName) ?? 0xC0);

            record[0x12] = (byte)(semantics?.GpsSystemIndex ?? (string.IsNullOrWhiteSpace(gpsSystemName) ? 0 : 1));

            // Digital channels: tones at +0x13..+0x16 are 0xFF (no tone).
            WriteTone(record, 0x13, ToneValue.None);
            WriteTone(record, 0x15, ToneValue.None);
        }
        else if (channel is AnalogChannel analog)
        {
            record[0x12] = 0x00;

            // Analog channels: tones at +0x13..+0x16.
            WriteTone(record, 0x13, analog.RxTone);
            WriteTone(record, 0x15, analog.TxTone);
        }

        record[0x17] = (byte)(semantics?.TalkAroundEnabled == true ? 0x10 : 0x00);

        // Evidence: Ch1 +0x18 0x00?0x80 when Display PTT-ID enabled (7th capture).
        record[0x18] = (byte)(semantics?.DisplayPttId == true ? 0x80 : 0x00);

        // Controlled-difference evidence: 0x00=TA-Off, 0x10=TA-On, 0x40=TA-Enabled; baseline 0x40,
        // This was previously misidentified as a hardcoded CPS default.
        record[0x1A] = (byte)(
            (semantics?.TalkAroundStatus == true ? 0x40 : 0x00) |
            (semantics?.VoxEnabled == true ? 0x10 : 0x00));

        // No PTT key-up encoding ever observed. CPS default is 0x00.
        record[0x1B] = 0x00;

        // Writing 0x51 for now; bit 3 may need to be derived from an unidentified channel property.
        record[0x20] = 0x0A;
        record[0x21] = 0x0A;
        record[0x22] = 0x51;

        // Bytes +0x23..+0x2F: not CPS-configurable. No per-channel DTMF field exists in the CPS UI.
        //   Ch0-49:  all zeros (safely before any overlap region).
        //   Ch50-71: repeating non-ASCII pattern (B0 C1 D0 B1 ED 20 32) — GB2312 text or runtime state.
        //     Present in both digital and analog channels.
        //   Ch72-84: ASCII DTMF sequence "34567890*#123" — per-channel auto-dial, distinct from global
        //     PttId "2345678" at config+0x192. Mix of digital and analog channels.
        //     Ch84 last byte is 0x3FFF; linear record region ends exactly at 0x3FFF (no gap before Table1).
        //   Ch85+:   paged region (0xF030+); no record-area overlap with name tables.
        // None of this data is CPS-written. Zero-fill is the correct CPS serialization strategy.
        // The OEM CPS writes channel names to the separate name tables, not into these record bytes.
    }

    public static void WriteChannelContactMap(Span<byte> image, int zeroBasedIndex, IReadOnlyList<Contact> contacts, string? contactName)
    {
        var mapOffset = Dm1702NativeImageAssumptions.ChannelContactMapStart + (zeroBasedIndex * 2);
        if (mapOffset + 2 > image.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex), "Channel contact map entry exceeds native DM1702 image bounds.");
        }

        var destination = image.Slice(mapOffset, 2);
        var contactIndex = Dm1702NativeContactSerializer.FindContactIndex(contacts, contactName);
        if (!contactIndex.HasValue)
        {
            destination[0] = 0x00;
            destination[1] = 0x00;
            return;
        }

        // highByte = (index >> 8) << 4, lowByte = index & 0xFF.
        // FindContactIndex returns 1-based; subtract 1 to get 0-based before storing.
        var idx = contactIndex.Value - 1;
        destination[0] = (byte)((idx >> 8) << 4);
        destination[1] = (byte)(idx & 0xFF);
    }

    /// <summary>
    /// b6(0x40)=Digital, b5(0x20)=RxOnly, b3(0x08)=LoneWorker, b1(0x02)=HighPower, b0(0x01)=WideBandwidth.
    /// Evidence: Ch1 Analog Wide LowPower RxOnly=0x21, Ch2 Digital High LoneWorker=0x4A.
    /// </summary>
    private static byte BuildModeFlags(Channel channel)
    {
        byte value = 0x00;
        var semantics = channel.NativeSemantics;

        if (channel is DigitalChannel)
        {
            value |= 0x40;
        }

        if (channel.ReceiveOnly)
        {
            value |= 0x20;
        }

        if (semantics?.LoneWorkerEnabled == true)
        {
            value |= 0x08;
        }

        if (IsHighPower(channel switch { DigitalChannel d => d.Power, AnalogChannel a => a.Power, _ => PowerLevel.Low }))
        {
            value |= 0x02;
        }

        if (channel is AnalogChannel analog && analog.Bandwidth == ChannelBandwidth.Wide)
        {
            value |= 0x01;
        }

        return value;
    }

    /// <summary>
    /// b7(0x80)=EncryptEnabled, b0(0x01)=DoubleCapacityMode.
    /// Evidence: Ch2 Digital Encrypt=ON DCM=ON ? 0x81.
    /// </summary>
    private static byte BuildSecondaryFlags(Channel channel)
    {
        var semantics = channel.NativeSemantics;
        byte value = 0x00;

        if (semantics?.EncryptionEnabled == true)
        {
            value |= 0x80;
        }

        if (semantics?.DoubleCapacityMode == true)
        {
            value |= 0x01;
        }

        return value;
    }

    /// <summary>
    /// b7(0x80)=AutoStartScan, b5(0x20)=EmergAlarmIndication, b0(0x01)=EmergCallIndication.
    /// Evidence: Ch2 AutoScan+EmergAlarm+EmergCall ? 0xA1.
    /// </summary>
    private static byte BuildScanEmergencyFlags(Dm1702NativeSemantics? semantics, bool hasScanList)
    {
        byte value = 0x00;

        if (hasScanList && semantics?.AutoScanEnabled != false)
        {
            value |= 0x80;
        }

        if (semantics?.EmergencyAlarmIndication == true)
        {
            value |= 0x20;
        }

        if (semantics?.EmergencyCallIndication == true)
        {
            value |= 0x01;
        }

        return value;
    }

    /// <summary>
    /// Digital: Always=0x00, ChannelFree=0x08, ColorCodeFree=0x10.
    /// Analog:  Always=0x00, ChannelFree=0x01, CTCSS/CDCSS=0x02.
    /// Evidence: baseline Ch2 +0x0D=0x30 = CCFree(0x10) + EmergAlarmAck(0x20) — NOT "Always=0x30".
    /// Note: EmergAlarmAck scatter bit (b5=0x20) is OR'd at the call site, not here.
    /// </summary>
    private static byte BuildAdmitCriteria(Channel channel)
    {
        return channel switch
        {
            DigitalChannel digital => digital.AdmitCriteria switch
            {
                AdmitCriteria.ChannelFree => 0x08,    // independently verified
                AdmitCriteria.ColorCodeFree => 0x10,   // independently verified
                _ => 0x00,                             // independently verified
            },
            AnalogChannel analog => analog.AdmitCriteria switch
            {
                AdmitCriteria.ChannelFree => 0x01,     // independently verified
                AdmitCriteria.ColorCodeFree => 0x02,   // independently verified
                _ => 0x00,                             // Always Allow
            },
            _ => 0x00,
        };
    }

    /// <summary>
    /// TS1=0x00, TS2=0x10. DCM is handled separately via +0x09 b0.
    /// Previously misidentified as TS1=0x20 because baseline Ch2 had EmergCallInd scatter bit at b5.
    /// </summary>
    private static byte EncodeTimeSlot(int timeSlot)
        => timeSlot switch
        {
            <= 1 => 0x00,
            2 => 0x10,
            _ => 0x30,
        };

    private static int? FindRxGroupIndex(IReadOnlyList<RxGroup> rxGroups, string? rxGroupName)
    {
        if (string.IsNullOrWhiteSpace(rxGroupName))
        {
            return null;
        }

        for (var index = 0; index < rxGroups.Count; index++)
        {
            if (string.Equals(rxGroups[index].Name, rxGroupName, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return null;
    }

    private static int? FindScanListIndex(IReadOnlyList<ScanList> scanLists, string channelName)
    {
        for (var index = 0; index < scanLists.Count; index++)
        {
            if (scanLists[index].ChannelNames.Contains(channelName, StringComparer.OrdinalIgnoreCase))
            {
                return index + 1;
            }
        }

        return null;
    }

    private static bool IsHighPower(PowerLevel power)
        => power == PowerLevel.High || power == PowerLevel.Medium;

    private static void WriteTone(Span<byte> record, int offset, ToneValue tone)
    {
        if (tone.Kind == ToneKind.None)
        {
            record[offset] = 0xFF;
            record[offset + 1] = 0xFF;
            return;
        }

        switch (tone.Kind)
        {
            case ToneKind.Ctcss:
            {
                var value = decimal.Parse(tone.RawValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                var digits = ((int)Math.Round(value * 10m, MidpointRounding.AwayFromZero)).ToString("D4", CultureInfo.InvariantCulture);
                record[offset] = byte.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                record[offset + 1] = byte.Parse(digits[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                break;
            }
            case ToneKind.DcsNormal:
            case ToneKind.DcsInverted:
            {
                // Low byte:  BCD of last two digits (digits[1..2])
                // High byte: Normal   ? 0x80 | int(digits[0])  (D023N: 0x80|0 = 0x80)
                //            Inverted ? 0xC0 | (numeric & 0x0F) (D023I: 0xC0|7 = 0xC7; D723I: 0xC0|7 = 0xC7)
                // Evidence: baseline+ctcss_cdcss_decode_encode.data capture confirmed both values.
                var numericValue = int.Parse(tone.RawValue.TrimStart('D', 'd').TrimEnd('N', 'n', 'I', 'i'), CultureInfo.InvariantCulture);
                var digits = numericValue.ToString("D3", CultureInfo.InvariantCulture);
                record[offset] = byte.Parse($"{digits[1]}{digits[2]}", NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                record[offset + 1] = tone.Kind == ToneKind.DcsInverted
                    ? (byte)(0xC0 | (numericValue & 0x0F))
                    : byte.Parse($"8{digits[0]}", NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                break;
            }
            default:
                throw new InvalidOperationException($"Native DM1702 tone encoding for {tone.Kind} is not implemented.");
        }
    }

    private static void WriteObservedFrequency(Span<byte> destination, int offset, double frequencyMHz)
    {
        // OEM encoding: freq_hz / 10 as 8-digit packed BCD, stored little-endian (LSB pair in lowest address).
        // 400.000 MHz ? 40000000 ? digits="40000000" ? [0x00, 0x00, 0x00, 0x40]
        // 162.400 MHz ? 16240000 ? digits="16240000" ? [0x00, 0x00, 0x24, 0x16]
        // 162.425 MHz ? 16242500 ? digits="16242500" ? [0x00, 0x25, 0x24, 0x16]
        var digits = ((int)Math.Round(frequencyMHz * 100000d, MidpointRounding.AwayFromZero)).ToString("D8", CultureInfo.InvariantCulture);
        destination[offset]     = byte.Parse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture); // LSB pair
        destination[offset + 1] = byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        destination[offset + 2] = byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        destination[offset + 3] = byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture); // MSB pair
    }
}
