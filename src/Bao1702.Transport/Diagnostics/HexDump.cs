using System.Text;

namespace Bao1702.Transport.Diagnostics;

/// <summary>
/// Formats raw byte data as a traditional hex-dump string with offset, hex bytes, and ASCII columns.
/// </summary>
public static class HexDump
{
    public static string Format(ReadOnlySpan<byte> data, int bytesPerLine = 16)
    {
        if (bytesPerLine <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerLine));
        }

        if (data.IsEmpty)
        {
            return "<empty>";
        }

        var builder = new StringBuilder();
        for (var offset = 0; offset < data.Length; offset += bytesPerLine)
        {
            var sliceLength = Math.Min(bytesPerLine, data.Length - offset);
            var slice = data.Slice(offset, sliceLength);
            builder.Append(offset.ToString("X4"));
            builder.Append(": ");

            for (var index = 0; index < bytesPerLine; index++)
            {
                if (index < slice.Length)
                {
                    builder.Append(slice[index].ToString("X2"));
                }
                else
                {
                    builder.Append("  ");
                }

                if (index < bytesPerLine - 1)
                {
                    builder.Append(' ');
                }
            }

            builder.Append("  |");
            foreach (var value in slice)
            {
                builder.Append(value is >= 32 and <= 126 ? (char)value : '.');
            }

            builder.Append('|');
            if (offset + sliceLength < data.Length)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }
}
