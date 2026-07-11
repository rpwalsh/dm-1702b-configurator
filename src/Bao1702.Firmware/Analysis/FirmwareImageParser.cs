using System.Security.Cryptography;
using System.Text;

namespace Bao1702.Firmware.Analysis;

/// <summary>
/// Parses raw firmware image bytes into a structured <see cref="FirmwareImage"/>
/// with header, segments, and analysis metadata.
/// </summary>
public static class FirmwareImageParser
{
    public static FirmwareImage Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16)
        {
            throw new InvalidDataException("Firmware image is too short.");
        }

        var signature = Encoding.ASCII.GetString(bytes[..4]);
        var declaredLength = BitConverter.ToInt32(bytes.Slice(4, 4));
        var checksum = BitConverter.ToUInt16(bytes.Slice(8, 2));
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Confidence"] = "Unknown",
            ["Parser"] = "Provisional header parser",
        };

        var raw = bytes.ToArray();
        var segments = new List<FirmwareSegment>
        {
            new("header", 0, Math.Min(16, raw.Length), raw[..Math.Min(16, raw.Length)]),
        };

        if (raw.Length > 16)
        {
            segments.Add(new FirmwareSegment("payload", 16, raw.Length - 16, raw[16..]));
        }

        return new FirmwareImage(new FirmwareHeader(signature, declaredLength, checksum, metadata), segments, raw);
    }

    public static FirmwareAnalysisResult Analyze(ReadOnlySpan<byte> bytes)
    {
        var image = Parse(bytes);
        var checksums = FirmwareChecksumService.Analyze(bytes, image.Header);
        var strings = FirmwareStringExtractor.ExtractValues(bytes, minimumLength: 4);
        var warnings = new List<string>();
        if (image.Header.DeclaredLength != 0 && image.Header.DeclaredLength != bytes.Length)
        {
            warnings.Add($"Declared length {image.Header.DeclaredLength} does not match actual length {bytes.Length}.");
        }

        if (image.Header.Signature.Any(ch => char.IsControl(ch)))
        {
            warnings.Add("Header signature contains non-printable characters.");
        }

        return new FirmwareAnalysisResult(image, checksums, strings, warnings);
    }

    public static FirmwareDiffResult Diff(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var differences = new List<FirmwareDiffEntry>();
        var maxLength = Math.Max(left.Length, right.Length);
        for (var index = 0; index < maxLength; index++)
        {
            var leftByte = index < left.Length ? left[index] : (byte)0x00;
            var rightByte = index < right.Length ? right[index] : (byte)0x00;
            if (leftByte != rightByte)
            {
                differences.Add(new FirmwareDiffEntry(index, leftByte, rightByte));
            }
        }

        return new FirmwareDiffResult(differences.Count, differences);
    }

    public static IReadOnlyList<string> ExtractAsciiStrings(ReadOnlySpan<byte> bytes, int minimumLength)
    {
        var buffer = bytes.ToArray();
        var results = new List<string>();
        var current = new StringBuilder();
        foreach (var value in buffer)
        {
            if (value is >= 32 and <= 126)
            {
                current.Append((char)value);
                continue;
            }

            if (current.Length >= minimumLength)
            {
                results.Add(current.ToString());
            }

            current.Clear();
        }

        if (current.Length >= minimumLength)
        {
            results.Add(current.ToString());
        }

        return results;
    }
}
