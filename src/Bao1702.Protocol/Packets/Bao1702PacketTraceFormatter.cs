using System.Text;
using Bao1702.Transport.Diagnostics;

namespace Bao1702.Protocol.Packets;

/// <summary>
/// Formats <see cref="Bao1702Packet"/> instances as human-readable diagnostic trace strings.
/// </summary>
public static class Bao1702PacketTraceFormatter
{
    public static string Format(Bao1702Packet packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var command = Bao1702CommandCatalog.Get(packet.CommandId);
        var builder = new StringBuilder();
        builder.Append(command.Name);
        builder.Append(" (0x");
        builder.Append(packet.CommandId.ToString("X2"));
        builder.Append(")");
        builder.Append(" Flags=0x");
        builder.Append(packet.Flags.ToString("X2"));
        builder.Append(" Address=0x");
        builder.Append(packet.Address.ToString("X4"));
        builder.Append(" PayloadLength=");
        builder.Append(packet.PayloadLength);
        builder.Append(" Knowledge=");
        builder.Append(command.KnowledgeLevel);

        if (packet.Payload.Length > 0)
        {
            builder.AppendLine();
            builder.Append(HexDump.Format(packet.Payload.AsSpan()));
        }

        return builder.ToString();
    }
}
