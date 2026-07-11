using Bao1702.Codeplug.Model;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class RecoveredNativeModelTests
{
    [TestMethod]
    public void RecoveredNativeChannel_SupportsGpsSystemField()
    {
        var channel = new RecoveredNativeChannel(
            72,
            "TEST-CH-073",
            443.4,
            448.4,
            ChannelKind.Digital,
            PowerLevel.High,
            ChannelBandwidth.Narrow,
            null,
            false,
            5,
            1,
            "907 [TGRP001]",
            "[TGRP001]",
            "APRS-GPS",
            0x4318,
            0x3D92,
            CodeplugConfidence.Inferred);

        Assert.AreEqual("APRS-GPS", channel.GpsSystemName);
    }
}
