using System.Text;

namespace Bao1702.ReverseEngineering.Helpers;

internal static class Dm1702NativeTextEncoding
{
    private static readonly Encoding NativeEncoding = CreateNativeEncoding();

    public static string Read(ReadOnlySpan<byte> data)
    {
        var end = data.Length;
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == 0x00 || data[i] == 0xFF)
            {
                end = i;
                break;
            }
        }

        return end == 0 ? string.Empty : NativeEncoding.GetString(data[..end]).TrimEnd();
    }

    public static void Write(Span<byte> destination, string value)
    {
        destination.Fill(0x00);
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var written = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            Span<char> runeChars = stackalloc char[2];
            var charCount = rune.EncodeToUtf16(runeChars);
            var chars = runeChars[..charCount];
            var byteCount = NativeEncoding.GetByteCount(chars);
            if (written + byteCount > destination.Length)
            {
                break;
            }

            written += NativeEncoding.GetBytes(chars, destination[written..]);
        }
    }

    private static Encoding CreateNativeEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GB2312");
    }
}
