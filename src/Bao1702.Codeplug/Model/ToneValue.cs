using System.Globalization;

namespace Bao1702.Codeplug.Model;

/// <summary>Discriminator for CTCSS, DCS, or no sub-audible tone.</summary>
public enum ToneKind
{
    None,
    Ctcss,
    DcsNormal,
    DcsInverted,
}

public sealed record ToneValue(ToneKind Kind, string RawValue)
{
    public static ToneValue None { get; } = new(ToneKind.None, string.Empty);

    public static ToneValue Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return None;
        }

        value = value.Trim();
        if (decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
        {
            return new ToneValue(ToneKind.Ctcss, value);
        }

        if (value.StartsWith("D", StringComparison.OrdinalIgnoreCase) && value.Length >= 5)
        {
            var suffix = value[^1];
            return suffix switch
            {
                'N' or 'n' => new ToneValue(ToneKind.DcsNormal, value.ToUpperInvariant()),
                'I' or 'i' => new ToneValue(ToneKind.DcsInverted, value.ToUpperInvariant()),
                _ => throw new FormatException($"Unsupported DCS suffix in tone '{value}'."),
            };
        }

        throw new FormatException($"Tone value '{value}' is not a supported CTCSS or DCS encoding.");
    }

    public override string ToString() => RawValue;
}
