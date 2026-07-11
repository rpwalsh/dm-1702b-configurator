using System.Text.RegularExpressions;
using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>A candidate contact record extracted by semantic analysis of the native image.</summary>
public sealed record StructuredContactCandidate(
    int Index,
    string Name,
    int? CallId,
    string? Label,
    string Evidence,
    CodeplugConfidence Confidence);

public sealed record StructuredNamedListCandidate(
    int Index,
    string Name,
    string? Label,
    string Evidence,
    CodeplugConfidence Confidence);

public sealed record StructuredProfileCandidate(
    int Index,
    string Name,
    string ProfileType,
    string Evidence,
    CodeplugConfidence Confidence);

public static partial class KnownCodeplugSemanticDecoder
{
    [GeneratedRegex(@"^(\d+)\s+\[(.+)\]$", RegexOptions.Compiled)]
    private static partial Regex NumberedBracketedNameRegex();

    [GeneratedRegex(@"^Call\s+(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CallNameRegex();

    [GeneratedRegex(@"^Sys\s+(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SystemProfileRegex();

    [GeneratedRegex(@"^Privacy\s+(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PrivacyProfileRegex();

    public static IReadOnlyList<StructuredContactCandidate> DecodeContacts(InferredCodeplugLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var structured = new List<StructuredContactCandidate>();
        foreach (var contact in layout.Contacts)
        {
            var match = CallNameRegex().Match(contact.Name);
            structured.Add(new StructuredContactCandidate(
                contact.Index,
                contact.Name,
                match.Success ? int.Parse(match.Groups[1].Value) : null,
                null,
                match.Success ? "Parsed sequential contact slot from 'Call N' naming." : "Contact slot inferred from repeated fixed-stride name table.",
                CodeplugConfidence.Inferred));
        }

        foreach (var named in layout.NamedLists)
        {
            var match = NumberedBracketedNameRegex().Match(named.Name);
            if (!match.Success)
            {
                continue;
            }

            structured.Add(new StructuredContactCandidate(
                named.Index,
                named.Name,
                int.Parse(match.Groups[1].Value),
                match.Groups[2].Value,
                "Parsed DMR-style contact or talkgroup name from '[label]' naming convention.",
                CodeplugConfidence.Inferred));
        }

        return structured
            .GroupBy(static candidate => candidate.Name, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static candidate => candidate.CallId ?? int.MaxValue)
            .ThenBy(static candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<StructuredNamedListCandidate> DecodeNamedLists(InferredCodeplugLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return layout.NamedLists
            .Select(named =>
            {
                var match = NumberedBracketedNameRegex().Match(named.Name);
                return new StructuredNamedListCandidate(
                    named.Index,
                    named.Name,
                    match.Success ? match.Groups[2].Value : null,
                    match.Success ? "Parsed bracketed label from repeated fixed-stride name table." : "Named list inferred from repeated fixed-stride bracketed-name table.",
                    CodeplugConfidence.Inferred);
            })
            .ToArray();
    }

    public static IReadOnlyList<StructuredProfileCandidate> DecodeProfiles(InferredCodeplugLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return layout.Channels
            .Select(channel =>
            {
                if (SystemProfileRegex().IsMatch(channel.Name))
                {
                    return new StructuredProfileCandidate(channel.Index, channel.Name, "SystemProfile", "Parsed system profile slot name from structured repeated table.", CodeplugConfidence.Inferred);
                }

                if (PrivacyProfileRegex().IsMatch(channel.Name))
                {
                    return new StructuredProfileCandidate(channel.Index, channel.Name, "PrivacyProfile", "Parsed privacy profile slot name from structured repeated table.", CodeplugConfidence.Inferred);
                }

                return null;
            })
            .Where(static profile => profile is not null)
            .Select(static profile => profile!)
            .ToArray();
    }
}
