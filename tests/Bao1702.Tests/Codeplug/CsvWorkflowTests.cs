using Bao1702.Codeplug.Csv;
using Bao1702.Codeplug.Model;
using Bao1702.Codeplug.Validation;

namespace Bao1702.Tests.Codeplug;

[TestClass]
public sealed class CsvWorkflowTests
{
    [TestMethod]
    public void ChannelCsvImporter_ReportsPreciseRowAndColumnForInvalidData()
    {
        const string csv = "Name,ChannelType,RxFrequencyMHz,TxFrequencyMHz,Power,Bandwidth,AdmitCriteria,ZoneNames,RxTone,TxTone,ColorCode,TimeSlot,ContactName,RxGroupName\n,Analog,145.5000,145.5000,High,Wide,Always,Simplex,67.0,67.0,,,,";

        var result = new ChannelCsvImporter().Import(csv);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, result.Issues.Count);
        Assert.AreEqual(2, result.Issues[0].RowNumber);
        Assert.AreEqual("Name", result.Issues[0].ColumnName);
    }

    [TestMethod]
    public void CodeplugCsvService_RoundTripsOptionalSchemas()
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, "Local", 145_500_000, 145_500_000, PowerLevel.High, ChannelBandwidth.Wide, AdmitCriteria.Always, ["Simplex"], ToneValue.Parse("67.0"), ToneValue.Parse("67.0"), CodeplugConfidence.Inferred),
                new DigitalChannel(2, "TG91", 439_987_500, 430_987_500, PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.ColorCodeFree, ["Worldwide"], 1, 1, "TG91 Worldwide", "World Wide", CodeplugConfidence.Inferred),
            ],
            Contacts = [new Contact("TG91 Worldwide", 91, ContactType.Group, CodeplugConfidence.Inferred)],
            Zones =
            [
                new Zone("Simplex", ["Local"], CodeplugConfidence.Inferred),
                new Zone("Worldwide", ["TG91"], CodeplugConfidence.Inferred),
            ],
            GroupLists = [new GroupList("Local Group", ["TG91 Worldwide"], CodeplugConfidence.Inferred)],
            RxGroups = [new RxGroup("World Wide", ["TG91 Worldwide"], CodeplugConfidence.Inferred)],
            ScanLists = [new ScanList("Travel", ["Local", "TG91"], CodeplugConfidence.Inferred)],
        };

        var service = new CodeplugCsvService();
        var exported = service.Export(image);
        var imported = service.Import(exported);

        Assert.IsTrue(imported.Success, string.Join(Environment.NewLine, imported.Issues.Select(issue => issue.Message)));
        Assert.IsNotNull(imported.Value);
        Assert.AreEqual(1, imported.Value.GroupLists.Count);
        Assert.AreEqual("Local Group", imported.Value.GroupLists[0].Name);
        Assert.AreEqual(1, imported.Value.RxGroups.Count);
        Assert.AreEqual(1, imported.Value.ScanLists.Count);
        Assert.AreEqual("World Wide", imported.Value.RxGroups[0].Name);
        Assert.AreEqual("Travel", imported.Value.ScanLists[0].Name);
    }

    [TestMethod]
    public void GroupListCsvImporter_ParsesGroupListsWithMultipleContacts()
    {
        const string csv = "Name,ContactNames\nLocal Repeater Group,TG91 Worldwide;TG1 Local\nDX Group,TG91 Worldwide";

        var result = new GroupListCsvImporter().Import(csv);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Value!.Count);
        Assert.AreEqual("Local Repeater Group", result.Value[0].Name);
        Assert.AreEqual(2, result.Value[0].ContactNames.Count);
        Assert.AreEqual("TG91 Worldwide", result.Value[0].ContactNames[0]);
        Assert.AreEqual("TG1 Local", result.Value[0].ContactNames[1]);
        Assert.AreEqual(1, result.Value[1].ContactNames.Count);
    }

    [TestMethod]
    public void GroupListCsvExporter_ProducesValidCsv()
    {
        var groupLists = new List<GroupList>
        {
            new("Local Group", ["TG91 Worldwide", "TG1 Local"], CodeplugConfidence.Inferred),
            new("DX", ["TG91 Worldwide"], CodeplugConfidence.Inferred),
        };

        var csv = new GroupListCsvExporter().Export(groupLists);

        Assert.IsTrue(csv.StartsWith("Name,ContactNames", StringComparison.Ordinal));
        Assert.IsTrue(csv.Contains("Local Group", StringComparison.Ordinal));
        Assert.IsTrue(csv.Contains("TG91 Worldwide;TG1 Local", StringComparison.Ordinal));
        Assert.IsTrue(csv.Contains("DX", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CodeplugValidator_FlagsGroupListBrokenReferences()
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            Contacts = [new Contact("Real Contact", 1, ContactType.Group, CodeplugConfidence.Inferred)],
            GroupLists = [new GroupList("My Group", ["Real Contact", "Ghost Contact"], CodeplugConfidence.Inferred)],
        };

        var issues = CodeplugValidator.Validate(image);

        Assert.IsTrue(issues.Any(issue => issue.Message.Contains("unknown contact 'Ghost Contact'", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CodeplugValidator_FlagsDuplicateGroupListNames()
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            GroupLists =
            [
                new GroupList("Dupe", [], CodeplugConfidence.Inferred),
                new GroupList("Dupe", [], CodeplugConfidence.Inferred),
            ],
        };

        var issues = CodeplugValidator.Validate(image);

        Assert.IsTrue(issues.Any(issue => issue.Message.Contains("Group list name 'Dupe' is duplicated", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CodeplugValidator_FlagsBrokenReferences()
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new DigitalChannel(1, "TG91", 439_987_500, 430_987_500, PowerLevel.High, ChannelBandwidth.Narrow, AdmitCriteria.ColorCodeFree, ["Worldwide"], 1, 1, "Missing Contact", "Missing Group", CodeplugConfidence.Inferred),
            ],
            Zones = [new Zone("Worldwide", ["TG91", "Missing Channel"], CodeplugConfidence.Inferred)],
            ScanLists = [new ScanList("Travel", ["Missing Channel"], CodeplugConfidence.Inferred)],
            RxGroups = [new RxGroup("World Wide", ["Missing Contact"], CodeplugConfidence.Inferred)],
        };

        var issues = CodeplugValidator.Validate(image);

        Assert.IsTrue(issues.Any(issue => issue.Message.Contains("unknown channel 'Missing Channel'", StringComparison.Ordinal)));
        Assert.IsTrue(issues.Any(issue => issue.Message.Contains("unknown contact 'Missing Contact'", StringComparison.Ordinal)));
        Assert.IsTrue(issues.Any(issue => issue.Message.Contains("unknown RX group 'Missing Group'", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CodeplugCsvService_Import_AcceptsKeysWithoutCsvExtension()
    {
        // Simulates CsvWorkspaceViewModel.ImportFromFolder which strips the .csv extension
        // via Path.GetFileNameWithoutExtension.
        var csvFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["channels"] = "Name,ChannelType,RxFrequencyMHz,TxFrequencyMHz,Power,Bandwidth,AdmitCriteria,ZoneNames,RxTone,TxTone,ColorCode,TimeSlot,ContactName,RxGroupName\nTest,Analog,146.520,146.520,High,Wide,Always,,,,,,",
            ["contacts"] = "Name,CallId,ContactType\nTCT001,9998,Private",
        };

        var service = new CodeplugCsvService();
        var result = service.Import(csvFiles);

        Assert.IsNotNull(result.Value);
        Assert.AreEqual(1, result.Value.Channels.Count, "Channel should be imported when key lacks .csv extension");
        Assert.AreEqual("Test", result.Value.Channels[0].Name);
        Assert.AreEqual(1, result.Value.Contacts.Count, "Contact should be imported when key lacks .csv extension");
    }

    [TestMethod]
    public void CodeplugCsvService_Import_AcceptsKeysWithCsvExtension()
    {
        // Schema names include .csv; this path should still work.
        var csvFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["channels.csv"] = "Name,ChannelType,RxFrequencyMHz,TxFrequencyMHz,Power,Bandwidth,AdmitCriteria,ZoneNames,RxTone,TxTone,ColorCode,TimeSlot,ContactName,RxGroupName\nTest,Analog,146.520,146.520,High,Wide,Always,,,,,,",
        };

        var service = new CodeplugCsvService();
        var result = service.Import(csvFiles);

        Assert.IsNotNull(result.Value);
        Assert.AreEqual(1, result.Value.Channels.Count, "Channel should be imported when key has .csv extension");
    }

    [TestMethod]
    public void CodeplugCsvService_ExportToDirectory_CreatesFiles()
    {
        var image = CodeplugImage.CreateEmpty() with
        {
            Channels =
            [
                new AnalogChannel(1, "Test", 146_520_000, 146_520_000, PowerLevel.High, ChannelBandwidth.Wide, AdmitCriteria.Always, [], ToneValue.None, ToneValue.None, CodeplugConfidence.Inferred),
            ],
            Contacts = [new Contact("TCT001", 9998, ContactType.Private, CodeplugConfidence.Inferred)],
        };

        var dir = Path.Combine(Path.GetTempPath(), $"bao1702_csv_test_{Guid.NewGuid():N}");
        try
        {
            var service = new CodeplugCsvService();
            service.ExportToDirectory(image, dir);

            Assert.IsTrue(File.Exists(Path.Combine(dir, "channels.csv")), "channels.csv should exist");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "contacts.csv")), "contacts.csv should exist");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "zones.csv")), "zones.csv should exist");

            // Verify round-trip through ImportFromDirectory
            var result = service.ImportFromDirectory(dir);
            Assert.IsNotNull(result.Value);
            Assert.AreEqual(1, result.Value.Channels.Count);
            Assert.AreEqual("Test", result.Value.Channels[0].Name);
            Assert.AreEqual(1, result.Value.Contacts.Count);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
