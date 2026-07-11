namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>A parsed USBPcap record containing transfer metadata and payload bytes.</summary>
public sealed record UsbPcapRecord(
    ushort HeaderLength,
    ushort FunctionCode,
    byte EndpointAddress,
    byte TransferType,
    uint DataLength,
    byte[] Payload,
    byte[] RawRecord)
{
    public CaptureDirection Direction => (EndpointAddress & 0x80) == 0x80
        ? CaptureDirection.DeviceToHost
        : CaptureDirection.HostToDevice;

    public bool HasPayload => Payload.Length > 0;
}

/// <summary>
/// Parses USBPcap pseudo-header records from normalized transcript bytes.
/// The field offsets are inferred from live captures and tshark correlation.
/// Confirmed so far for this workspace: endpoint address, transfer type, and data length
/// align with tshark field extraction for the captured Bao1702 usbprint traffic.
/// </summary>
public static class UsbPcapRecordParser
{
    public static bool TryParse(ReadOnlySpan<byte> rawRecord, out UsbPcapRecord? record, out string error)
    {
        record = null;
        error = string.Empty;

        if (rawRecord.Length < 0x1B)
        {
            error = "Record is shorter than the minimum observed USBPcap pseudo-header length.";
            return false;
        }

        var headerLength = BitConverter.ToUInt16(rawRecord[..2]);
        if (headerLength < 0x1B || headerLength > rawRecord.Length)
        {
            error = $"USBPcap header length {headerLength} is outside the record bounds {rawRecord.Length}.";
            return false;
        }

        if (rawRecord.Length < headerLength)
        {
            error = "Raw record is shorter than the declared USBPcap header length.";
            return false;
        }

        var endpointAddressOffset = headerLength - 6;
        var transferTypeOffset = headerLength - 5;
        var dataLengthOffset = headerLength - 4;
        if (dataLengthOffset + 4 > rawRecord.Length)
        {
            error = "USBPcap record does not contain a complete inferred data length field.";
            return false;
        }

        var endpointAddress = rawRecord[endpointAddressOffset];
        var transferType = rawRecord[transferTypeOffset];
        var dataLength = BitConverter.ToUInt32(rawRecord.Slice(dataLengthOffset, 4));
        var availablePayloadLength = rawRecord.Length - headerLength;
        if (dataLength > availablePayloadLength)
        {
            error = $"USBPcap declared payload length {dataLength} exceeds available bytes {availablePayloadLength}.";
            return false;
        }

        var functionCode = BitConverter.ToUInt16(rawRecord.Slice(14, 2));
        var payload = rawRecord.Slice((int)headerLength, (int)dataLength).ToArray();
        record = new UsbPcapRecord(headerLength, functionCode, endpointAddress, transferType, dataLength, payload, rawRecord.ToArray());
        return true;
    }
}
