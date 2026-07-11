namespace Bao1702.Protocol.Model;

/// <summary>Reports block-level progress for a codeplug read or write operation.</summary>
public sealed record ProtocolProgress(
    string OperationName,
    int CurrentBlock,
    int TotalBlocks,
    int BytesCompleted,
    int BytesTotal)
{
    public double PercentComplete => BytesTotal > 0
        ? Math.Clamp((double)BytesCompleted / BytesTotal * 100.0, 0.0, 100.0)
        : 0.0;

    public override string ToString()
        => $"{OperationName}: block {CurrentBlock}/{TotalBlocks} ({PercentComplete:F1}%)";
}
