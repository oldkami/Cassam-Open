namespace Cassam.Ui.Hardware.Common.Accessibility;

/// <summary>
/// A single 24-bit RGB color used by the contrast-ratio
/// calculator. Channels are stored as 0..255 bytes (sRGB
/// color space) — the contrast formula converts to linear
/// RGB before computing relative luminance per WCAG 2.x.
/// </summary>
/// <param name="R">Red channel (0..255).</param>
/// <param name="G">Green channel (0..255).</param>
/// <param name="B">Blue channel (0..255).</param>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Convert to a hex string in the form <c>#RRGGBB</c>.</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Parse a hex string like <c>#RRGGBB</c> or <c>RRGGBB</c> (uppercase or lowercase).</summary>
    public static RgbColor FromHex(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new System.ArgumentException("Value is null or empty.", nameof(value));
        if (value[0] == '#') value = value.Substring(1);
        if (value.Length != 6) throw new System.FormatException("Color hex must be exactly 6 characters.");
        var r = System.Convert.ToByte(value.Substring(0, 2), 16);
        var g = System.Convert.ToByte(value.Substring(2, 2), 16);
        var b = System.Convert.ToByte(value.Substring(4, 2), 16);
        return new RgbColor(r, g, b);
    }

    /// <summary>Format a colour string exactly as it appears in a XAML theme file so error messages match.</summary>
    public override string ToString() => ToHex();
}
