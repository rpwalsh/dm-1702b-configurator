namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>Direction of a captured USB transfer relative to the host.</summary>
public enum CaptureDirection
{
    Unknown,
    HostToDevice,
    DeviceToHost,
}

public sealed record CaptureRecord(
    int Index,
    CaptureDirection Direction,
    byte[] Bytes,
    IReadOnlyList<string> SourceLines);

public sealed record CaptureTranscript(
    IReadOnlyList<CaptureRecord> Records,
    IReadOnlyList<string> Notes);

/// <summary>
/// Parses lightweight USB capture transcripts exported from Wireshark/tshark text views or handwritten RE notes.
/// This parser does not claim to understand pcapng directly. It is intentionally conservative and accepts
/// grouped text records containing a direction marker plus hex byte tokens.
/// </summary>
public static class CaptureTranscriptParser
{
    private static readonly IReadOnlyDictionary<string, CaptureDirection> DirectionTokens =
        new Dictionary<string, CaptureDirection>(StringComparer.OrdinalIgnoreCase)
        {
            [">"] = CaptureDirection.HostToDevice,
            ["OUT"] = CaptureDirection.HostToDevice,
            ["BULKOUT"] = CaptureDirection.HostToDevice,
            ["BULK-OUT"] = CaptureDirection.HostToDevice,
            ["CONTROL-OUT"] = CaptureDirection.HostToDevice,
            ["CONTROL_OUT"] = CaptureDirection.HostToDevice,
            ["TX"] = CaptureDirection.HostToDevice,
            ["HOST>"] = CaptureDirection.HostToDevice,
            ["HOST->DEVICE"] = CaptureDirection.HostToDevice,
            ["URB_OUT"] = CaptureDirection.HostToDevice,
            ["URB_BULK_OUT"] = CaptureDirection.HostToDevice,
            ["URB_CONTROL_OUT"] = CaptureDirection.HostToDevice,
            ["<"] = CaptureDirection.DeviceToHost,
            ["IN"] = CaptureDirection.DeviceToHost,
            ["BULKIN"] = CaptureDirection.DeviceToHost,
            ["BULK-IN"] = CaptureDirection.DeviceToHost,
            ["CONTROL-IN"] = CaptureDirection.DeviceToHost,
            ["CONTROL_IN"] = CaptureDirection.DeviceToHost,
            ["RX"] = CaptureDirection.DeviceToHost,
            ["DEV<"] = CaptureDirection.DeviceToHost,
            ["DEVICE->HOST"] = CaptureDirection.DeviceToHost,
            ["URB_IN"] = CaptureDirection.DeviceToHost,
            ["URB_BULK_IN"] = CaptureDirection.DeviceToHost,
            ["URB_CONTROL_IN"] = CaptureDirection.DeviceToHost,
        };

    public static CaptureTranscript Parse(string transcriptText)
    {
        ArgumentNullException.ThrowIfNull(transcriptText);

        var records = new List<CaptureRecord>();
        var notes = new List<string>();
        var currentChunk = new List<string>();

        using var reader = new StringReader(transcriptText);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushChunk(currentChunk, records, notes);
                continue;
            }

            if (IsComment(line))
            {
                notes.Add(line.Trim());
                continue;
            }

            if (LooksLikeRecordBoundary(line, currentChunk))
            {
                FlushChunk(currentChunk, records, notes);
            }

            currentChunk.Add(line);
        }

        FlushChunk(currentChunk, records, notes);
        return new CaptureTranscript(records, notes);
    }

    private static void FlushChunk(List<string> chunk, List<CaptureRecord> records, List<string> notes)
    {
        if (chunk.Count == 0)
        {
            return;
        }

        var direction = ResolveDirection(chunk);
        var bytes = ExtractBytes(chunk);
        if (bytes.Count == 0)
        {
            notes.Add($"Ignored text chunk with no parseable bytes: {chunk[0].Trim()}");
            chunk.Clear();
            return;
        }

        records.Add(new CaptureRecord(records.Count, direction, bytes.ToArray(), chunk.ToArray()));
        chunk.Clear();
    }

    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("#", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith(";", StringComparison.Ordinal);
    }

    private static bool LooksLikeRecordBoundary(string line, IReadOnlyList<string> currentChunk)
    {
        if (currentChunk.Count == 0)
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("Frame ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Packet ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("No. ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Index ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("URB ", StringComparison.OrdinalIgnoreCase))
        {
            return currentChunk.Any(static candidate => candidate.Any(Uri.IsHexDigit));
        }

        var currentDirection = ResolveDirection(currentChunk);
        var nextDirection = ResolveDirection([line]);
        return currentDirection != CaptureDirection.Unknown
            && nextDirection != CaptureDirection.Unknown
            && currentDirection != nextDirection;
    }

    private static CaptureDirection ResolveDirection(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (var token in Tokenize(trimmed))
            {
                if (DirectionTokens.TryGetValue(token, out var direction))
                {
                    return direction;
                }
            }

            if (trimmed.Contains("host to device", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureDirection.HostToDevice;
            }

            if (trimmed.Contains("device to host", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureDirection.DeviceToHost;
            }

            if (trimmed.Contains("bulk out", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("control out", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureDirection.HostToDevice;
            }

            if (trimmed.Contains("bulk in", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("control in", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureDirection.DeviceToHost;
            }
        }

        return CaptureDirection.Unknown;
    }

    private static List<byte> ExtractBytes(IReadOnlyList<string> lines)
    {
        var bytes = new List<byte>();
        foreach (var line in lines)
        {
            if (LooksLikeMetadataLine(line))
            {
                continue;
            }

            var tokens = Tokenize(line)
                .Where(static token => token.Length > 0)
                .ToArray();
            if (tokens.Length == 0)
            {
                continue;
            }

            var startIndex = 0;
            if (LooksLikeOffsetToken(tokens[0]) && tokens.Skip(1).Any(IsByteToken))
            {
                startIndex = 1;
            }

            for (var index = startIndex; index < tokens.Length; index++)
            {
                var token = tokens[index];
                if (DirectionTokens.ContainsKey(token))
                {
                    continue;
                }

                if (TryParseByteToken(token, out var value))
                {
                    bytes.Add(value);
                }
            }
        }

        return bytes;
    }

    private static string[] Tokenize(string line)
    {
        return line.Replace("|", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace(":", " ", StringComparison.Ordinal)
            .Replace("[", " ", StringComparison.Ordinal)
            .Replace("]", " ", StringComparison.Ordinal)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsByteToken(string token)
        => token.Length == 2 && token.All(Uri.IsHexDigit);

    private static bool TryParseByteToken(string token, out byte value)
    {
        var normalized = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? token[2..]
            : token;

        if (IsByteToken(normalized))
        {
            value = Convert.ToByte(normalized, 16);
            return true;
        }

        value = 0;
        return false;
    }

    private static bool LooksLikeOffsetToken(string token)
        => (token.Length == 4 || token.Length == 8) && token.All(Uri.IsHexDigit);

    private static bool LooksLikeMetadataLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("Frame ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Packet ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("No. ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Index ", StringComparison.OrdinalIgnoreCase);
    }
}
