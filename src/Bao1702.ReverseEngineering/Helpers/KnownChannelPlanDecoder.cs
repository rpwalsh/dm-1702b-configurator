using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

public sealed record StructuredChannelCandidate(
    int Index,
    string Name,
    double? RxFrequencyMHz,
    double? TxFrequencyMHz,
    string? Service,
    string Evidence,
    CodeplugConfidence Confidence);

/// <summary>
/// No public channel-plan database is used. Structured candidates must originate from fields
/// decoded from the supplied image; name-based frequency or service inference is prohibited.
/// </summary>
public static class KnownChannelPlanDecoder
{
    public static IReadOnlyList<StructuredChannelCandidate> Decode(InferredCodeplugLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return [];
    }
}
