using System.Text;
using Bao1702.Transport.Diagnostics;

namespace Bao1702.Protocol.Stock;

/// <summary>
/// Well-known packet constants and builders for the stock CPS protocol (PSEARCH, PASSSTA,
/// SYSINFO, read/write commands, and G/V query windows).
/// </summary>
public static class StockCpsPackets
{
    public static ReadOnlyMemory<byte> Psearch { get; } = "PSEARCH"u8.ToArray();

    public static ReadOnlyMemory<byte> PassSta { get; } = "PASSSTA"u8.ToArray();

    public static ReadOnlyMemory<byte> SysInfo { get; } = "SYSINFO"u8.ToArray();

    public static ReadOnlyMemory<byte> Ack { get; } = new byte[] { 0x06 };

    public static ReadOnlyMemory<byte> QueryWindow10 { get; } = new byte[] { 0x56, 0x00, 0x10, 0x0A, 0x0D };

    public static ReadOnlyMemory<byte> QueryWindow20 { get; } = new byte[] { 0x56, 0x00, 0x20, 0x0A, 0x0D };

    /// <summary>
    /// V0000 query — note the OEM capture shows <c>56 00 00 00 0A</c>, NOT the <c>56 00 00 0A 0D</c>
    /// pattern used by V0010/V0020.  The response is [3, 8] with ACK, not [3] without ACK.
    /// </summary>
    public static ReadOnlyMemory<byte> QueryWindow00 { get; } = new byte[] { 0x56, 0x00, 0x00, 0x00, 0x0A };

    /// <summary>
    /// Transition command sent after the handshake completes and before read/write operations.
    /// OEM sends <c>FF FF FF FF {upperSectorByte}</c>.  No response is expected.
    /// The <paramref name="upperSectorByte"/> is extracted from the V0000 continuation response.
    /// </summary>
    public static byte[] BuildEnterTransferMode(byte upperSectorByte)
        => [0xFF, 0xFF, 0xFF, 0xFF, upperSectorByte];

    /// <summary>
    /// After the transfer-mode command the OEM echoes the bootstrap identity string
    /// (e.g. "DMR1702") back to the radio.  The radio responds with a single ACK (0x06).
    /// </summary>
    public static byte[] BuildIdentityEcho(string bootstrapIdentity)
        => System.Text.Encoding.ASCII.GetBytes(bootstrapIdentity);

    /// <summary>
    /// Start-read command (0x02) sent after the identity echo.
    /// The radio responds with 8 bytes (all 0xFF in observed captures).
    /// </summary>
    public static ReadOnlyMemory<byte> StartRead { get; } = new byte[] { 0x02 };

    public static byte[] BuildGCommand(byte windowOffset)
        => [0x47, 0x00, 0x00, windowOffset, 0x40];

    public static byte[] BuildRCommand(byte selectorLow, byte selectorMid, byte selectorHigh, byte length)
        => [0x52, selectorLow, selectorMid, selectorHigh, length];

    /// <summary>
    /// Builds a W-write command: 0x57 header, 3 address selector bytes, length byte, then data.
    /// OEM write protocol: host sends W-command, radio responds with single 0x06 ACK.
    /// Observed command structure: [0x57, lo, mid, hi, 0x40, ...64 data bytes...]
    /// </summary>
    public static byte[] BuildWWriteCommand(byte selectorLow, byte selectorMid, byte selectorHigh, byte length, ReadOnlySpan<byte> pageData)
    {
        if (pageData.Length != length)
        {
            throw new ArgumentException($"Page data must be exactly {length} bytes, got {pageData.Length}.", nameof(pageData));
        }

        var command = new byte[5 + pageData.Length];
        command[0] = 0x57;  // 'W'
        command[1] = selectorLow;
        command[2] = selectorMid;
        command[3] = selectorHigh;
        command[4] = length;
        pageData.CopyTo(command.AsSpan(5));
        return command;
    }

    public static string Format(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return "<empty>";
        }

        var isAscii = true;
        foreach (var value in payload)
        {
            if (value is < 0x20 or > 0x7E)
            {
                isAscii = false;
                break;
            }
        }

        if (isAscii)
        {
            return Encoding.ASCII.GetString(payload);
        }

        return HexDump.Format(payload);
    }
}
