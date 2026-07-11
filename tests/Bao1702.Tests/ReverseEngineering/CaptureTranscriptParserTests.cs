using Bao1702.Protocol.Packets;
using Bao1702.ReverseEngineering.Helpers;
using Bao1702.Transport.Framing;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class CaptureTranscriptParserTests
{
    [TestMethod]
    public void Parse_WiresharkStyleHexDumpWithDirections_ProducesRecords()
    {
        var packet = new Bao1702Packet(Bao1702CommandIds.ReadRadioInfo, 0x00, 0, []);
        var frame = TransportFrameCodec.Encode(Bao1702PacketSerializer.Serialize(packet));

        var transcript = string.Join(Environment.NewLine,
            ">",
            $"0000  {string.Join(' ', frame.Select(static value => value.ToString("X2")))}",
            string.Empty,
            "<",
            $"0000  {string.Join(' ', frame.Select(static value => value.ToString("X2")))}");

        var parsed = CaptureTranscriptParser.Parse(transcript);

        Assert.AreEqual(2, parsed.Records.Count);
        Assert.AreEqual(CaptureDirection.HostToDevice, parsed.Records[0].Direction);
        Assert.AreEqual(CaptureDirection.DeviceToHost, parsed.Records[1].Direction);
        CollectionAssert.AreEqual(frame, parsed.Records[0].Bytes);
    }

    [TestMethod]
    public void Analyze_FindsCurrentTransportFrameCandidate()
    {
        var packet = new Bao1702Packet(Bao1702CommandIds.ReadRadioInfo, 0x00, 0, []);
        var frameA = TransportFrameCodec.Encode(Bao1702PacketSerializer.Serialize(packet));
        var frameB = TransportFrameCodec.Encode(Bao1702PacketSerializer.Serialize(packet with { Flags = 0x80 }));

        var transcript = new CaptureTranscript(
            [
                new CaptureRecord(0, CaptureDirection.HostToDevice, frameA, []),
                new CaptureRecord(1, CaptureDirection.DeviceToHost, frameB, []),
            ],
            []);

        var analysis = FrameHeuristicAnalyzer.Analyze(transcript);
        var topCandidate = analysis.Candidates.First();

        Assert.AreEqual(TransportFrameCodec.SyncByte, topCandidate.SyncByte);
        Assert.AreEqual(LengthFieldEndianness.LittleEndian, topCandidate.LengthEndianness);
        Assert.AreEqual(2, topCandidate.MatchingLengthCount);
        Assert.AreEqual(2, topCandidate.MatchingChecksumCount);
    }

    [TestMethod]
    public void Parse_TsharkStyleFieldsWith0xTokens_ProducesNormalizedTranscript()
    {
        var transcript = string.Join(Environment.NewLine,
            "Frame 1: 10 bytes on wire",
            "URB_BULK out",
            "Leftover Capture Data: 0xA5 0x06 0x00 0x01 0x00 0x00 0x00 0x00 0x00 0x07",
            string.Empty,
            "Frame 2: 10 bytes on wire",
            "URB_BULK in",
            "Leftover Capture Data: 0xA5 0x06 0x00 0x01 0x80 0x00 0x00 0x00 0x00 0x87");

        var parsed = CaptureTranscriptParser.Parse(transcript);
        var normalized = CaptureTranscriptFormatter.Normalize(parsed);

        Assert.AreEqual(2, parsed.Records.Count);
        Assert.AreEqual(CaptureDirection.HostToDevice, parsed.Records[0].Direction);
        Assert.AreEqual(CaptureDirection.DeviceToHost, parsed.Records[1].Direction);
        StringAssert.Contains(normalized, ">\r\n0000  A5 06 00 01 00 00 00 00 00 07");
        StringAssert.Contains(normalized, "<\r\n0000  A5 06 00 01 80 00 00 00 00 87");
    }
}
