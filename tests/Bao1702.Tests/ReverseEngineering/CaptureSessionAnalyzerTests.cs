using Bao1702.Protocol.Packets;
using Bao1702.ReverseEngineering.Helpers;
using Bao1702.Transport.Framing;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class CaptureSessionAnalyzerTests
{
    [TestMethod]
    public void AnalyzeHexLines_DecodesKnownPacketFrame()
    {
        var packet = new Bao1702Packet(Bao1702CommandIds.ReadRadioInfo, 0x00, 0, []);
        var frame = TransportFrameCodec.Encode(Bao1702PacketSerializer.Serialize(packet));
        var hexLine = string.Join(' ', frame.Select(static value => value.ToString("X2")));

        var analysis = CaptureSessionAnalyzer.AnalyzeHexLines(hexLine);

        Assert.AreEqual(1, analysis.TotalFrames);
        Assert.AreEqual(1, analysis.ValidFrames);
        Assert.AreEqual(0, analysis.HostToDeviceFrames);
        Assert.AreEqual(0, analysis.DeviceToHostFrames);
        CollectionAssert.Contains(analysis.CommandNames.ToList(), "ReadRadioInfo");
    }

    [TestMethod]
    public void AnalyzeTranscript_PreservesDirectionCounts()
    {
        var readInfoPacket = new Bao1702Packet(Bao1702CommandIds.ReadRadioInfo, 0x00, 0, []);
        var requestFrame = TransportFrameCodec.Encode(Bao1702PacketSerializer.Serialize(readInfoPacket));

        var responsePayload = System.Text.Encoding.UTF8.GetBytes("DM-1702|Bao1702B|D1.00.01|B1.00.00|1702B1234");
        var responsePacket = new Bao1702Packet(Bao1702CommandIds.ReadRadioInfo, 0x80, 0, [.. responsePayload]);
        var responseFrame = TransportFrameCodec.Encode(Bao1702PacketSerializer.Serialize(responsePacket));

        var transcript = string.Join(Environment.NewLine,
            $"> {string.Join(' ', requestFrame.Select(static value => value.ToString("X2")))}",
            $"< {string.Join(' ', responseFrame.Select(static value => value.ToString("X2")))}");

        var analysis = CaptureSessionAnalyzer.AnalyzeTranscript(transcript);

        Assert.AreEqual(2, analysis.TotalFrames);
        Assert.AreEqual(2, analysis.ValidFrames);
        Assert.AreEqual(1, analysis.HostToDeviceFrames);
        Assert.AreEqual(1, analysis.DeviceToHostFrames);
    }
}
