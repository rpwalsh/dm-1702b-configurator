namespace Bao1702.Firmware.Analysis;

/// <summary>Parsed firmware file header: signature, declared length, checksum, and metadata.</summary>
public sealed record FirmwareHeader(string Signature, int DeclaredLength, ushort HeaderChecksum, IReadOnlyDictionary<string, string> Metadata);

public sealed record FirmwareSegment(string Name, int Offset, int Length, byte[] Data);

public sealed record FirmwareChecksum(string Algorithm, string Value, bool IsValid);

public sealed record FirmwareImage(FirmwareHeader Header, IReadOnlyList<FirmwareSegment> Segments, byte[] RawBytes);

public sealed record FirmwareAnalysisResult(FirmwareImage Image, IReadOnlyList<FirmwareChecksum> Checksums, IReadOnlyList<string> Strings, IReadOnlyList<string> Warnings);

public sealed record FirmwareStringEntry(int Offset, string Encoding, string Value);

public sealed record FirmwareMapEntry(string Name, int Offset, int Length, double Entropy, string Notes);

public sealed record FirmwareCompatibilityResult(bool IsCompatible, string Summary, IReadOnlyList<string> Reasons);

public sealed record FirmwareDiffEntry(int Offset, byte Left, byte Right);

public sealed record FirmwareDiffResult(int TotalDifferences, IReadOnlyList<FirmwareDiffEntry> Differences);
