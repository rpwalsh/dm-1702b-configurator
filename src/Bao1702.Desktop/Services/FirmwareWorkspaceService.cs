using System.IO;
using Bao1702.Firmware.Analysis;
using Bao1702.Protocol.Model;

namespace Bao1702.Desktop.Services;

/// <summary>
/// Result of a firmware image analysis, including compatibility assessment and memory map.
/// </summary>
public sealed record FirmwareWorkspaceAnalysis(
    string SourceName,
    FirmwareAnalysisResult Analysis,
    string MapText,
    FirmwareCompatibilityResult Compatibility);

public sealed class FirmwareWorkspaceService
{
    public string FirmwareWorkspaceRoot { get; } = Path.Combine(AppContext.BaseDirectory, "firmware");

    public FirmwareWorkspaceAnalysis AnalyzeBytes(string sourceName, ReadOnlyMemory<byte> bytes, RadioIdentity? identity = null)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name is required.", nameof(sourceName));
        }

        var analysis = FirmwareImageParser.Analyze(bytes.Span);
        var mapText = FirmwareMapDumper.DumpText(analysis.Image);
        var compatibility = identity is null
            ? new FirmwareCompatibilityResult(false, "No active radio identity supplied. Firmware image kept in read-only analysis mode.", ["A connected, identified target is required for compatibility evaluation."])
            : FirmwareCompatibilityValidator.Validate(identity, bytes.Span);

        return new FirmwareWorkspaceAnalysis(sourceName, analysis, mapText, compatibility);
    }

    public FirmwareWorkspaceAnalysis AnalyzeFile(string imagePath, RadioIdentity? identity = null)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("Firmware image path is required.", nameof(imagePath));
        }

        return AnalyzeBytes(Path.GetFileName(imagePath), File.ReadAllBytes(imagePath), identity);
    }
}
