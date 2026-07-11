using Bao1702.Transport.Framing;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Byte order for a frame length field.</summary>
public enum LengthFieldEndianness
{
    LittleEndian,
    BigEndian,
}

public sealed record FramingHeuristicCandidate(
    byte SyncByte,
    LengthFieldEndianness LengthEndianness,
    int HeaderLength,
    int TrailerLength,
    int MatchingLengthCount,
    int MatchingChecksumCount,
    IReadOnlyList<int> MatchingRecordIndices)
{
    public string Summary =>
        $"sync=0x{SyncByte:X2} len={LengthEndianness} header={HeaderLength} trailer={TrailerLength} " +
        $"lengthMatches={MatchingLengthCount} checksumMatches={MatchingChecksumCount}";
}

public sealed record FramingHeuristicAnalysis(
    int RecordCount,
    IReadOnlyList<KeyValuePair<byte, int>> FirstByteHistogram,
    IReadOnlyList<FramingHeuristicCandidate> Candidates);

public static class FrameHeuristicAnalyzer
{
    public static FramingHeuristicAnalysis Analyze(CaptureTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var dataRecords = transcript.Records.Where(static record => record.Bytes.Length >= 4).ToArray();
        var histogram = dataRecords
            .GroupBy(static record => record.Bytes[0])
            .Select(static group => new KeyValuePair<byte, int>(group.Key, group.Count()))
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key)
            .ToArray();

        var candidates = new List<FramingHeuristicCandidate>();
        foreach (var syncCandidate in histogram.Take(8).Select(static pair => pair.Key))
        {
            candidates.Add(Evaluate(dataRecords, syncCandidate, LengthFieldEndianness.LittleEndian));
            candidates.Add(Evaluate(dataRecords, syncCandidate, LengthFieldEndianness.BigEndian));
        }

        return new FramingHeuristicAnalysis(
            transcript.Records.Count,
            histogram,
            candidates
                .OrderByDescending(static candidate => candidate.MatchingLengthCount)
                .ThenByDescending(static candidate => candidate.MatchingChecksumCount)
                .ThenBy(static candidate => candidate.SyncByte)
                .ThenBy(static candidate => candidate.LengthEndianness)
                .ToArray());
    }

    private static FramingHeuristicCandidate Evaluate(
        IReadOnlyList<CaptureRecord> dataRecords,
        byte syncByte,
        LengthFieldEndianness endianness)
    {
        var matchingIndices = new List<int>();
        var checksumMatches = 0;

        foreach (var record in dataRecords)
        {
            if (record.Bytes[0] != syncByte)
            {
                continue;
            }

            var length = ReadUInt16(record.Bytes, 1, endianness);
            const int headerLength = TransportFrameCodec.HeaderLength;
            const int trailerLength = TransportFrameCodec.TrailerLength;
            var expectedLength = headerLength + length + trailerLength;
            if (expectedLength != record.Bytes.Length)
            {
                continue;
            }

            matchingIndices.Add(record.Index);
            var expectedChecksum = Checksums.ComputeSum8(record.Bytes.AsSpan(1, record.Bytes.Length - 2));
            if (record.Bytes[^1] == expectedChecksum)
            {
                checksumMatches++;
            }
        }

        return new FramingHeuristicCandidate(
            syncByte,
            endianness,
            TransportFrameCodec.HeaderLength,
            TransportFrameCodec.TrailerLength,
            matchingIndices.Count,
            checksumMatches,
            matchingIndices.ToArray());
    }

    private static ushort ReadUInt16(byte[] bytes, int offset, LengthFieldEndianness endianness)
    {
        return endianness == LengthFieldEndianness.LittleEndian
            ? BitConverter.ToUInt16(bytes, offset)
            : (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
    }
}
