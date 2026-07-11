using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Metadata and raw bytes from a saved codeplug .data file.</summary>
public sealed record SavedCodeplugDataDump(
    string FilePath,
    int Length,
    string Sha256,
    CodeplugChangeAnalysis? ChangeAnalysis,
    HeuristicDecodedCodeplugDump HeuristicDecodedCodeplug,
    InferredCodeplugLayout InferredLayout,
    IReadOnlyList<ChannelRecordTable> CandidateChannelRecordTables,
    IReadOnlyList<DirectChannelRecord> DirectChannelRecords,
    IReadOnlyList<ObservedChannelRecord> ObservedChannelRecords,
    IReadOnlyList<StructuredChannelCandidate> StructuredChannelCandidates,
    IReadOnlyList<StructuredContactCandidate> StructuredContactCandidates,
    IReadOnlyList<StructuredNamedListCandidate> StructuredNamedListCandidates,
    IReadOnlyList<StructuredProfileCandidate> StructuredProfileCandidates,
    TypedSavedCodeplugSnapshot TypedSnapshot,
    IReadOnlyList<string> ContactNameCandidates,
    IReadOnlyList<string> GroupOrZoneNameCandidates,
    IReadOnlyList<string> SettingLikeStrings);

public static partial class SavedCodeplugDataExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    [GeneratedRegex(@"^Call\s+\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ContactNameRegex();

    public static SavedCodeplugDataDump BuildDump(string filePath, byte[] bytes, string? baselinePath = null, byte[]? baselineBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(bytes);

        var decoded = HeuristicCodeplugDecoder.Decode(bytes);
        var inferredLayout = InferredCodeplugLayoutDecoder.Decode(decoded);
        var candidateChannelRecordTables = ChannelRecordTableDetector.Detect(bytes, inferredLayout.Tables);
        var directChannelRecords = DirectChannelRecordDecoder.Decode(bytes, startOffset: 0x3010, count: 128, stride: 0x30);
        var observedChannelRecords = ObservedChannelRecordDecoder.DecodeAllChannels(bytes);
        var structuredChannels = KnownChannelPlanDecoder.Decode(inferredLayout);
        var structuredContacts = KnownCodeplugSemanticDecoder.DecodeContacts(inferredLayout);
        var structuredNamedLists = KnownCodeplugSemanticDecoder.DecodeNamedLists(inferredLayout);
        var structuredProfiles = KnownCodeplugSemanticDecoder.DecodeProfiles(inferredLayout);
        var changeAnalysis = baselinePath is not null && baselineBytes is not null
            ? CodeplugChangeDiffAnalyzer.Analyze(baselinePath, baselineBytes, filePath, bytes)
            : null;
        var strings = decoded.Strings.Select(static entry => entry.Text).Distinct(StringComparer.Ordinal).ToArray();
        var contactNameCandidates = strings.Where(static text => ContactNameRegex().IsMatch(text)).OrderBy(static text => text, StringComparer.Ordinal).ToArray();
        var groupOrZoneNameCandidates = strings.Where(static text => text.Contains('[', StringComparison.Ordinal) || text.Contains(']', StringComparison.Ordinal)).OrderBy(static text => text, StringComparer.Ordinal).ToArray();
        var settingLikeStrings = Array.Empty<string>();

        var provisionalDump = new SavedCodeplugDataDump(
            filePath,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)),
            changeAnalysis,
            new HeuristicDecodedCodeplugDump(
                decoded.Strings.Take(2048).ToArray(),
                decoded.FrequencyPairs.Take(4096).ToArray(),
                decoded.ChannelCandidates.Take(2048).ToArray()),
            inferredLayout,
            candidateChannelRecordTables,
            directChannelRecords,
            observedChannelRecords,
            structuredChannels,
            structuredContacts,
            structuredNamedLists,
            structuredProfiles,
            new TypedSavedCodeplugSnapshot([], [], [], []),
            contactNameCandidates,
            groupOrZoneNameCandidates,
            settingLikeStrings);

        var typedSnapshot = TypedSavedCodeplugSnapshotBuilder.Build(provisionalDump);

        return new SavedCodeplugDataDump(
            filePath,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)),
            changeAnalysis,
            new HeuristicDecodedCodeplugDump(
                decoded.Strings.Take(2048).ToArray(),
                decoded.FrequencyPairs.Take(4096).ToArray(),
                decoded.ChannelCandidates.Take(2048).ToArray()),
            inferredLayout,
            candidateChannelRecordTables,
            directChannelRecords,
            observedChannelRecords,
            structuredChannels,
            structuredContacts,
            structuredNamedLists,
            structuredProfiles,
            typedSnapshot,
            contactNameCandidates,
            groupOrZoneNameCandidates,
            settingLikeStrings);
    }

    public static string SerializeToJson(SavedCodeplugDataDump dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        return JsonSerializer.Serialize(dump, JsonOptions);
    }
}
