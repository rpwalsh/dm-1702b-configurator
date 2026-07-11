using Bao1702.Codeplug.Csv;

namespace Bao1702.Tests.Codeplug;

[TestClass]
public sealed class OperatorTemplateTests
{
    [TestMethod]
    public void PublicOperatorTemplates_AreStructurallyValidAndExplicitlyScoped()
    {
        var templateDirectory = FindRepositoryPath("examples", "us-amateur");
        var templatePaths = Directory.GetFiles(templateDirectory, "*.csv");

        Assert.HasCount(5, templatePaths);

        foreach (var templatePath in templatePaths)
        {
            var result = new ChannelCsvImporter().Import(File.ReadAllText(templatePath));
            var errors = result.Issues.Where(issue => issue.Severity == Bao1702.Codeplug.Validation.ValidationSeverity.Error).ToArray();

            Assert.IsEmpty(errors, $"{Path.GetFileName(templatePath)} must import without structural errors.");
            Assert.IsNotNull(result.Value);
            Assert.IsNotEmpty(result.Value);
        }
    }

    [TestMethod]
    public void ReceiveOnlyWeatherTemplate_ImportsWithTransmitInhibit()
    {
        var path = Path.Combine(FindRepositoryPath("examples", "us-amateur"), "receive-only-weather.csv");
        var result = new ChannelCsvImporter().Import(File.ReadAllText(path));

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Value);
        Assert.IsTrue(result.Value.Single().ReceiveOnly);
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository path '{Path.Combine(segments)}'.");
    }
}
