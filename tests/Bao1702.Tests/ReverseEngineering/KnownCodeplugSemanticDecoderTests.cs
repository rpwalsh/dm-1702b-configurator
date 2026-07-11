using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class KnownCodeplugSemanticDecoderTests
{
    [TestMethod]
    public void DecodeContactsAndLists_ParsesBracketedAndCallPatterns()
    {
        var layout = new InferredCodeplugLayout(
            [],
            [
                new InferredChannelRecord(0, "Sys 1", 0x3000, 0x3000, 20, null, null, null, "synthetic", CodeplugConfidence.Inferred),
                new InferredChannelRecord(1, "Privacy 1", 0x3014, 0x3000, 20, null, null, null, "synthetic", CodeplugConfidence.Inferred),
            ],
            [
                new InferredContactRecord(0, "Call 100", 0x2000, 0x2000, 24, CodeplugConfidence.Inferred),
            ],
            [
                new InferredNamedListRecord(0, "3129 [TGRP004]", 0x1000, 0x1000, 24, CodeplugConfidence.Inferred),
            ]);

        var contacts = KnownCodeplugSemanticDecoder.DecodeContacts(layout);
        var lists = KnownCodeplugSemanticDecoder.DecodeNamedLists(layout);
        var profiles = KnownCodeplugSemanticDecoder.DecodeProfiles(layout);

        Assert.IsTrue(contacts.Any(static contact => contact.Name == "Call 100" && contact.CallId == 100));
        Assert.IsTrue(contacts.Any(static contact => contact.Name == "3129 [TGRP004]" && contact.CallId == 3129));
        Assert.IsTrue(lists.Any(static list => list.Name == "3129 [TGRP004]" && list.Label == "TGRP004"));
        Assert.IsTrue(profiles.Any(static profile => profile.ProfileType == "SystemProfile"));
        Assert.IsTrue(profiles.Any(static profile => profile.ProfileType == "PrivacyProfile"));
    }
}
