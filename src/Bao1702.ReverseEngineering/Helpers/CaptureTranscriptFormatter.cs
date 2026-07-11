namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Renders a <see cref="CaptureTranscript"/> into a normalized, human-readable text format.
/// </summary>
public static class CaptureTranscriptFormatter
{
    public static string Normalize(CaptureTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var lines = new List<string>();
        foreach (var note in transcript.Notes)
        {
            lines.Add(note);
        }

        if (transcript.Notes.Count > 0 && transcript.Records.Count > 0)
        {
            lines.Add(string.Empty);
        }

        for (var index = 0; index < transcript.Records.Count; index++)
        {
            var record = transcript.Records[index];
            lines.Add(GetDirectionMarker(record.Direction));
            foreach (var hexLine in FormatHexLines(record.Bytes))
            {
                lines.Add(hexLine);
            }

            if (index + 1 < transcript.Records.Count)
            {
                lines.Add(string.Empty);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetDirectionMarker(CaptureDirection direction)
        => direction switch
        {
            CaptureDirection.HostToDevice => ">",
            CaptureDirection.DeviceToHost => "<",
            _ => "?",
        };

    private static IEnumerable<string> FormatHexLines(byte[] bytes)
    {
        const int bytesPerLine = 16;
        for (var offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            var slice = bytes.Skip(offset).Take(bytesPerLine).ToArray();
            yield return $"{offset:X4}  {string.Join(' ', slice.Select(static value => value.ToString("X2")))}";
        }
    }
}
