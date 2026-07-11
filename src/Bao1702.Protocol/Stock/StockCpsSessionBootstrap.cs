namespace Bao1702.Protocol.Stock;

/// <summary>A single step in the stock CPS session startup handshake sequence.</summary>
public sealed record StockCpsStartupStep(string Name, ReadOnlyMemory<byte> Request, int[] ExpectedResponseLengths, bool RequiresAckRoundTripAfterResponses);

/// <summary>
/// Defines the ordered startup handshake steps (PSEARCH ? PASSSTA ? SYSINFO ? G/V queries)
/// required to establish a stock CPS session and read radio identity information.
/// Also provides the segment-based address maps for codeplug read and write operations,
/// derived from the captured OEM CPS session (rw-readback-usbpcap2-20260411-190633.pcap).
/// </summary>
public static class StockCpsSessionBootstrap
{
    /// <summary>
    /// The size of the OEM CPS native codeplug image (.data file) in bytes.
    /// All .data files produced by the OEM CPS are exactly this length.
    /// </summary>
    public const int NativeImageLength = 245_760; // 0x3C000
    public static IReadOnlyList<StockCpsStartupStep> ReadRadioInfoSteps { get; } =
    [
        new("PSEARCH", StockCpsPackets.Psearch, [8], false),
        new("PASSSTA", StockCpsPackets.PassSta, [3], false),
        new("SYSINFO", StockCpsPackets.SysInfo, [1], false),
        new("V0010", StockCpsPackets.QueryWindow10, [3, 10], true),
        new("V0020", StockCpsPackets.QueryWindow20, [3, 10], true),
        new("G0000", StockCpsPackets.BuildGCommand(0x00), [69], true),
        new("G0040", StockCpsPackets.BuildGCommand(0x40), [69], true),
        new("G0080", StockCpsPackets.BuildGCommand(0x80), [69], true),
        new("G00C0", StockCpsPackets.BuildGCommand(0xC0), [69], true),
        new("V0000", StockCpsPackets.QueryWindow00, [3, 8], true),
    ];

    /// <summary>Each R/W command transfers exactly 64 data bytes (0x40).</summary>
    public const int ObservedCodeplugPageSize = 64;

    /// <summary>
    /// Generates the 1-byte probe-read addresses the OEM CPS sends between the transition
    /// commands and the 64-byte block reads.  Each probe reads a single byte at address
    /// <c>0x??_??_FF</c> (lo = 0xFF), stepping through every 4 KiB page boundary
    /// from <c>0x001FFF</c> to <c>0x{upperSector:X2}8FFF</c>.
    /// The <paramref name="upperSector"/> value (typically 0x0C) comes from the V0000
    /// continuation response byte at index 6.
    /// </summary>
    public static IEnumerable<int> EnumerateProbeAddresses(byte upperSector)
    {
        // Start at 0x001FFF, increment by 0x1000 each step.
        // End at upperSector * 0x10000 + 0x8FFF.
        var start = 0x001FFF;
        var end = (upperSector << 16) | 0x8FFF;
        for (var addr = start; addr <= end; addr += 0x1000)
        {
            yield return addr;
        }
    }

    /// <summary>
    /// Contiguous address segments observed during the OEM CPS 64-byte read phase.
    /// The read has one extra segment (0x055000) that is not written back.
    /// Each tuple is (startAddress, blockCount). Addresses increment by 0x40 within a segment.
    /// Derived from: rw-readback-usbpcap2-20260411-190633.pcap, first 3648 R-commands with len=0x40.
    /// The last OEM wire-segment (0x01E000, 1984 blocks) maps to two distinct file regions
    /// with different wire?file deltas (0x2000 then 0x1000), so it is split here to maintain
    /// a 1:1 correspondence with <see cref="ReadSegmentFileMapping"/>.
    /// </summary>
    public static IReadOnlyList<(int StartAddress, int BlockCount)> ObservedReadSegments { get; } =
    [
        (0x055000,   64),   //   4,096 bytes
        (0x046000,   64),   //   4,096 bytes
        (0x007000,   64),   //   4,096 bytes
        (0x072000,  128),   //   8,192 bytes
        (0x009000,  256),   //  16,384 bytes
        (0x084000,   64),   //   4,096 bytes
        (0x00E000,  192),   //  12,288 bytes
        (0x06C000,   64),   //   4,096 bytes
        (0x048000,   64),   //   4,096 bytes
        (0x013000,  640),   //  40,960 bytes
        (0x07C000,   64),   //   4,096 bytes
        (0x01E000,  512),   //  32,768 bytes  (wire?file delta 0x2000)
        (0x026000, 1472),   //  94,208 bytes  (wire?file delta 0x1000)
    ];

    /// <summary>
    /// Contiguous address segments observed during the OEM CPS 64-byte write phase.
    /// Same as read segments minus the first read-only segment at 0x055000.
    /// The last OEM wire-segment (0x01E000, 1984 blocks) is split into two entries
    /// to match the file-mapping discontinuity — see <see cref="ObservedReadSegments"/>.
    /// Derived from: rw-readback-usbpcap2-20260411-190633.pcap, all 3584 W-commands.
    /// </summary>
    public static IReadOnlyList<(int StartAddress, int BlockCount)> ObservedWriteSegments { get; } =
    [
        (0x046000,   64),   //   4,096 bytes
        (0x007000,   64),   //   4,096 bytes
        (0x072000,  128),   //   8,192 bytes
        (0x009000,  256),   //  16,384 bytes
        (0x084000,   64),   //   4,096 bytes
        (0x00E000,  192),   //  12,288 bytes
        (0x06C000,   64),   //   4,096 bytes
        (0x048000,   64),   //   4,096 bytes
        (0x013000,  640),   //  40,960 bytes
        (0x07C000,   64),   //   4,096 bytes
        (0x01E000,  512),   //  32,768 bytes
        (0x026000, 1472),   //  94,208 bytes
    ];

    /// <summary>Total 64-byte blocks in a full read (3648 blocks = 233,472 bytes).</summary>
    public static int ObservedReadTotalBlocks { get; } = SumBlocks(ObservedReadSegments);

    /// <summary>Total 64-byte blocks in a full write (3584 blocks = 229,376 bytes).</summary>
    public static int ObservedWriteTotalBlocks { get; } = SumBlocks(ObservedWriteSegments);

    /// <summary>Total image size for a full read.</summary>
    public static int ObservedReadImageSize => ObservedReadTotalBlocks * ObservedCodeplugPageSize;

    /// <summary>Total image size for a full write.</summary>
    public static int ObservedWriteImageSize => ObservedWriteTotalBlocks * ObservedCodeplugPageSize;

    public static byte[] BuildRReadCommand(byte selectorLow, byte selectorMid, byte selectorHigh, byte length)
        => [0x52, selectorLow, selectorMid, selectorHigh, length];

    /// <summary>
    /// Encodes an absolute address into the 3-byte selector + length used by R/W commands.
    /// </summary>
    public static (byte SelectorLow, byte SelectorMid, byte SelectorHigh, byte Length) EncodeAddress(int address)
    {
        return (
            (byte)(address & 0xFF),
            (byte)((address >> 8) & 0xFF),
            (byte)((address >> 16) & 0xFF),
            0x40);
    }

    /// <summary>
    /// Enumerates all (address, imageOffset) pairs for a given segment list.
    /// </summary>
    public static IEnumerable<(int Address, int ImageOffset)> EnumerateBlocks(IReadOnlyList<(int StartAddress, int BlockCount)> segments)
    {
        var imageOffset = 0;
        foreach (var (startAddress, blockCount) in segments)
        {
            for (var i = 0; i < blockCount; i++)
            {
                yield return (startAddress + i * ObservedCodeplugPageSize, imageOffset);
                imageOffset += ObservedCodeplugPageSize;
            }
        }
    }

    /// <summary>
    /// Wire-address-to-file-offset mapping for read segments, empirically derived by correlating
    /// Each entry is (WireSegmentStart, NativeFileOffset, BlockCount). Within a segment the mapping
    /// is linear: file_offset = native_base + (wire_address - wire_start).
    /// </summary>
    public static IReadOnlyList<(int WireStart, int FileOffset, int BlockCount)> ReadSegmentFileMapping { get; } =
    [
        (0x055000, 0x002000,   64),  //   4,096 bytes — read-only segment (not written back)
        (0x046000, 0x003000,   64),  //   4,096 bytes
        (0x007000, 0x004000,   64),  //   4,096 bytes
        (0x072000, 0x005000,  128),  //   8,192 bytes
        (0x009000, 0x007000,  256),  //  16,384 bytes
        (0x084000, 0x00B000,   64),  //   4,096 bytes
        (0x00E000, 0x00C000,  192),  //  12,288 bytes
        (0x06C000, 0x00F000,   64),  //   4,096 bytes
        (0x048000, 0x010000,   64),  //   4,096 bytes
        (0x013000, 0x011000,  640),  //  40,960 bytes
        (0x07C000, 0x01B000,   64),  //   4,096 bytes
        (0x01E000, 0x01C000,  512),  //  32,768 bytes  (delta 0x2000)
        (0x026000, 0x025000, 1472),  //  94,208 bytes  (delta 0x1000)
    ];

    /// <summary>
    /// Wire-address-to-file-offset mapping for write segments (same as read minus the
    /// read-only segment at 0x055000).
    /// </summary>
    public static IReadOnlyList<(int WireStart, int FileOffset, int BlockCount)> WriteSegmentFileMapping { get; } =
        ReadSegmentFileMapping.Where(static s => s.WireStart != 0x055000).ToArray();

    /// <summary>
    /// Converts a packed read image (233,472 bytes, segments packed linearly in read order)
    /// into the 245,760-byte native .data format used by the OEM CPS.
    /// Unread regions (file offsets 0x0000–0x1FFF and 0x3B000–0x3BFFF) are filled with 0xFF.
    /// </summary>
    public static byte[] BuildNativeImage(byte[] packedReadImage)
    {
        ArgumentNullException.ThrowIfNull(packedReadImage);
        if (packedReadImage.Length != ObservedReadImageSize)
        {
            throw new ArgumentException(
                $"Packed read image must be exactly {ObservedReadImageSize:N0} bytes, but received {packedReadImage.Length:N0}.",
                nameof(packedReadImage));
        }

        var native = new byte[NativeImageLength];
        Array.Fill(native, (byte)0xFF);

        var packedOffset = 0;
        foreach (var (wireStart, fileOffset, blockCount) in ReadSegmentFileMapping)
        {
            var segmentBytes = blockCount * ObservedCodeplugPageSize;
            packedReadImage.AsSpan(packedOffset, segmentBytes).CopyTo(native.AsSpan(fileOffset, segmentBytes));
            packedOffset += segmentBytes;
        }

        return native;
    }

    /// <summary>
    /// Extracts a packed write image (229,376 bytes) from a 245,760-byte native .data image
    /// by reading segments at their mapped file offsets in write order.
    /// </summary>
    public static byte[] ExtractPackedWriteImage(byte[] nativeImage)
    {
        ArgumentNullException.ThrowIfNull(nativeImage);
        if (nativeImage.Length != NativeImageLength)
        {
            throw new ArgumentException(
                $"Native image must be exactly {NativeImageLength:N0} bytes, but received {nativeImage.Length:N0}.",
                nameof(nativeImage));
        }

        var packed = new byte[ObservedWriteImageSize];
        var packedOffset = 0;
        foreach (var (_, fileOffset, blockCount) in WriteSegmentFileMapping)
        {
            var segmentBytes = blockCount * ObservedCodeplugPageSize;
            nativeImage.AsSpan(fileOffset, segmentBytes).CopyTo(packed.AsSpan(packedOffset, segmentBytes));
            packedOffset += segmentBytes;
        }

        return packed;
    }

    private static int SumBlocks(IReadOnlyList<(int StartAddress, int BlockCount)> segments)
    {
        var total = 0;
        foreach (var (_, blockCount) in segments)
        {
            total += blockCount;
        }

        return total;
    }
}
