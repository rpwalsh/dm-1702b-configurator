using Bao1702.Desktop.Services;
using Bao1702.Firmware.Analysis;

namespace Bao1702.Desktop.ViewModels;

/// <summary>
/// View model for the Firmware analysis tab.
/// Displays firmware image analysis, checksums, string extraction, and memory maps.
/// </summary>
public sealed class FirmwareAnalysisViewModel : ObservableObject
{
    private string _sourceName = "No firmware image analyzed yet.";
    private string _summary = "Firmware analysis is idle.";
    private string _compatibilitySummary = "No target compatibility analysis yet.";
    private string _checksums = "N/A";
    private string _stringPreview = "N/A";
    private string _mapPreview = "N/A";

    public string SourceName
    {
        get => _sourceName;
        set => SetProperty(ref _sourceName, value);
    }

    public string Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    public string CompatibilitySummary
    {
        get => _compatibilitySummary;
        set => SetProperty(ref _compatibilitySummary, value);
    }

    public string Checksums
    {
        get => _checksums;
        set => SetProperty(ref _checksums, value);
    }

    public string StringPreview
    {
        get => _stringPreview;
        set => SetProperty(ref _stringPreview, value);
    }

    public string MapPreview
    {
        get => _mapPreview;
        set => SetProperty(ref _mapPreview, value);
    }

    public void LoadAnalysis(FirmwareWorkspaceAnalysis workspaceAnalysis)
    {
        ArgumentNullException.ThrowIfNull(workspaceAnalysis);

        var analysis = workspaceAnalysis.Analysis;
        SourceName = workspaceAnalysis.SourceName;
        Summary = string.Join(Environment.NewLine,
            $"Signature: {analysis.Image.Header.Signature}",
            $"Declared length: {analysis.Image.Header.DeclaredLength}",
            $"Actual length: {analysis.Image.RawBytes.Length}",
            $"Warnings: {string.Join("; ", analysis.Warnings.DefaultIfEmpty("none"))}");
        CompatibilitySummary = string.Join(Environment.NewLine,
            workspaceAnalysis.Compatibility.Summary,
            string.Join(Environment.NewLine, workspaceAnalysis.Compatibility.Reasons));
        Checksums = string.Join(Environment.NewLine, analysis.Checksums.Select(FormatChecksum));
        StringPreview = string.Join(Environment.NewLine, analysis.Strings.Take(16).DefaultIfEmpty("<no printable strings>"));
        MapPreview = workspaceAnalysis.MapText;
    }

    private static string FormatChecksum(FirmwareChecksum checksum)
        => $"{checksum.Algorithm}: {checksum.Value} ({(checksum.IsValid ? "valid" : "mismatch")})";
}
