using Bao1702.Protocol.Packets;
using Bao1702.Transport.Framing;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Analysis result for a single captured USB frame, including decode status and parsed packet.</summary>
public sealed record CaptureFrameAnalysis(
    int Index,
    CaptureDirection Direction,
    bool IsValidFrame,
    string Summary,
    string? DecodeError,
    Bao1702Packet? Packet,
    byte[] RawBytes,
    UsbPcapRecord? UsbPcapRecord = null);

public sealed record CaptureSessionAnalysis(
    int TotalFrames,
    int ValidFrames,
    int HostToDeviceFrames,
    int DeviceToHostFrames,
    IReadOnlyList<CaptureFrameAnalysis> Frames,
    IReadOnlyList<string> CommandNames);

public static class CaptureSessionAnalyzer
{
    public static CaptureSessionAnalysis AnalyzeHexLines(string captureText)
    {
        ArgumentNullException.ThrowIfNull(captureText);
        return AnalyzeFrames(CaptureImportHelper.ParseHexLines(captureText));
    }

    public static CaptureSessionAnalysis AnalyzeFrames(IReadOnlyList<byte[]> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var records = frames.Select((frame, index) => new CaptureRecord(index, CaptureDirection.Unknown, frame, [])).ToArray();
        return AnalyzeRecords(records);
    }

    public static CaptureSessionAnalysis AnalyzeTranscript(string transcriptText)
    {
        ArgumentNullException.ThrowIfNull(transcriptText);
        var transcript = CaptureTranscriptParser.Parse(transcriptText);
        return AnalyzeRecords(transcript.Records);
    }

    public static CaptureSessionAnalysis AnalyzeRecords(IReadOnlyList<CaptureRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var analyses = new List<CaptureFrameAnalysis>(records.Count);
        var commandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var frame = record.Bytes;
            UsbPcapRecord? usbPcapRecord = null;
            if (UsbPcapRecordParser.TryParse(frame, out var parsedUsbPcapRecord, out _))
            {
                usbPcapRecord = parsedUsbPcapRecord;
                frame = usbPcapRecord.Payload;
                record = record with { Direction = usbPcapRecord.Direction };
            }

            if (frame.Length == 0)
            {
                analyses.Add(new CaptureFrameAnalysis(index, record.Direction, false, $"Frame {index} contains no transport payload.", "USB transfer carried zero payload bytes.", null, records[index].Bytes, usbPcapRecord));
                continue;
            }

            if (!TransportFrameCodec.TryDecode(frame, out var payload, out var error))
            {
                analyses.Add(new CaptureFrameAnalysis(index, record.Direction, false, $"Frame {index} is not a valid transport frame.", error, null, records[index].Bytes, usbPcapRecord));
                continue;
            }

            try
            {
                var packet = Bao1702PacketSerializer.Deserialize(payload);
                var command = Bao1702CommandCatalog.Get(packet.CommandId);
                commandNames.Add(command.Name);
                analyses.Add(new CaptureFrameAnalysis(
                    index,
                    record.Direction,
                    true,
                    $"Frame {index}: {command.Name} address=0x{packet.Address:X4} payload={packet.PayloadLength} bytes",
                    null,
                    packet,
                    records[index].Bytes,
                    usbPcapRecord));
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            {
                analyses.Add(new CaptureFrameAnalysis(index, record.Direction, false, $"Frame {index} transport-decoded but protocol parsing failed.", ex.Message, null, records[index].Bytes, usbPcapRecord));
            }
        }

        return new CaptureSessionAnalysis(
            records.Count,
            analyses.Count(entry => entry.IsValidFrame),
            analyses.Count(entry => entry.Direction == CaptureDirection.HostToDevice),
            analyses.Count(entry => entry.Direction == CaptureDirection.DeviceToHost),
            analyses,
            commandNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
