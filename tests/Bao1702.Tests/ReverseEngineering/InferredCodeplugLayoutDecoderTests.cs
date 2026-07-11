using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class InferredCodeplugLayoutDecoderTests
{
    [TestMethod]
    public void Decode_FindsChannelAndContactTablesFromRepeatedStrideStrings()
    {
        var strings = new[]
        {
            new DecodedStringEntry(0x1000, "TEST-CH-001", "ChannelLike"),
            new DecodedStringEntry(0x100B, "TEST-CH-002", "ChannelLike"),
            new DecodedStringEntry(0x1016, "TEST-CH-003", "ChannelLike"),
            new DecodedStringEntry(0x1021, "TEST-CH-004", "ChannelLike"),
            new DecodedStringEntry(0x2000, "Call 100", "ChannelLike"),
            new DecodedStringEntry(0x2010, "Call 101", "ChannelLike"),
            new DecodedStringEntry(0x2020, "Call 102", "ChannelLike"),
            new DecodedStringEntry(0x2030, "Call 103", "ChannelLike"),
        };

        var frequencies = new[]
        {
            new FrequencyPairCandidate(0x1040, 162.55000, 162.55000, FrequencyEncodingKind.BcdAsStored, Bao1702.Codeplug.Model.CodeplugConfidence.Inferred),
        };

        var decoded = new HeuristicDecodedCodeplug(strings, frequencies, []);
        var layout = InferredCodeplugLayoutDecoder.Decode(decoded);

        Assert.IsTrue(layout.Tables.Any(static table => table.Kind == InferredStringTableKind.ChannelName));
        Assert.IsTrue(layout.Tables.Any(static table => table.Kind == InferredStringTableKind.ContactName));
        Assert.IsTrue(layout.Channels.Any(static channel => channel.Name == "TEST-CH-001"));
        Assert.IsTrue(layout.Contacts.Any(static contact => contact.Name == "Call 100"));
        Assert.IsTrue(layout.Channels.Any(static channel => !string.IsNullOrWhiteSpace(channel.Evidence)));
    }
}
