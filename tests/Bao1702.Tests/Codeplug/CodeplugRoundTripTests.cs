using Bao1702.Codeplug.Binary;
using Bao1702.Codeplug.Csv;
using Bao1702.Codeplug.Model;

namespace Bao1702.Tests.Codeplug;

[TestClass]
public sealed class CodeplugRoundTripTests
{
    [TestMethod]
    public void BinarySerializer_RoundTripsCodeplugModel()
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, "Local", 145_500_000, 145_500_000, PowerLevel.High, ChannelBandwidth.Wide, AdmitCriteria.Always, ["Simplex"], ToneValue.Parse("67.0"), ToneValue.Parse("67.0"), CodeplugConfidence.Inferred),
                new DigitalChannel(2, "TG91", 439_987_500, 430_987_500, PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.ColorCodeFree, ["Worldwide"], 1, 1, "TG91 Worldwide", "World Wide", CodeplugConfidence.Inferred),
            ],
            Zones =
            [
                new Zone("Simplex", ["Local"], CodeplugConfidence.Inferred),
                new Zone("Worldwide", ["TG91"], CodeplugConfidence.Inferred),
            ],
            Contacts = [new Contact("TG91 Worldwide", 91, ContactType.Group, CodeplugConfidence.Inferred)],
        };

        var bytes = CodeplugBinarySerializer.Serialize(image);
        var parsed = CodeplugBinarySerializer.Deserialize(bytes);

        Assert.AreEqual(2, parsed.Channels.Count);
        Assert.AreEqual(2, parsed.Zones.Count);
        Assert.AreEqual("TG91 Worldwide", parsed.Contacts[0].Name);
    }

    [TestMethod]
    public void CsvImport_ParsesAnalogAndDigitalChannels()
    {
        const string csv = "Name,ChannelType,RxFrequencyMHz,TxFrequencyMHz,Power,Bandwidth,AdmitCriteria,ZoneNames,RxTone,TxTone,ColorCode,TimeSlot,ContactName,RxGroupName\nLocal,Analog,145.5000,145.5000,High,Wide,Always,Simplex,67.0,67.0,,,,\nTG91,Digital,439.9875,430.9875,High,Narrow,ColorCodeFree,Worldwide,,,1,1,TG91 Worldwide,World Wide";

        var result = new ChannelCsvImporter().Import(csv);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Value!.Count);
        Assert.IsInstanceOfType<DigitalChannel>(result.Value[1]);
    }
}
