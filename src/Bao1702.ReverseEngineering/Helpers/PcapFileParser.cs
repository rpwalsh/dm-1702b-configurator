namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Minimal classic libpcap reader used to load USBPcap capture files from the workspace.
/// This parser intentionally supports only the pcap flavor observed in this repo's captures.
/// </summary>
public static class PcapFileParser
{
    private const uint PcapMagicLittleEndianMicroseconds = 0xA1B2C3D4;
    private const uint PcapMagicLittleEndianNanoseconds = 0xA1B23C4D;
    private const int GlobalHeaderLength = 24;
    private const int PacketHeaderLength = 16;

    public static IReadOnlyList<byte[]> ParseRecords(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);

        if (fileBytes.Length < GlobalHeaderLength)
        {
            throw new InvalidDataException("PCAP file is shorter than the global header.");
        }

        var magic = BitConverter.ToUInt32(fileBytes, 0);
        if (magic is not (PcapMagicLittleEndianMicroseconds or PcapMagicLittleEndianNanoseconds))
        {
            throw new InvalidDataException($"Unsupported PCAP magic 0x{magic:X8}. Only little-endian classic pcap files are supported.");
        }

        var records = new List<byte[]>();
        var offset = GlobalHeaderLength;
        while (offset + PacketHeaderLength <= fileBytes.Length)
        {
            var includedLength = BitConverter.ToUInt32(fileBytes, offset + 8);
            offset += PacketHeaderLength;

            if (includedLength > int.MaxValue)
            {
                throw new InvalidDataException($"PCAP packet length {includedLength} exceeds supported limits.");
            }

            if (offset + includedLength > fileBytes.Length)
            {
                throw new InvalidDataException("PCAP packet payload extends beyond end of file.");
            }

            if (includedLength > 0)
            {
                var record = new byte[includedLength];
                Buffer.BlockCopy(fileBytes, offset, record, 0, (int)includedLength);
                records.Add(record);
            }

            offset += (int)includedLength;
        }

        return records;
    }
}
