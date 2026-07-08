namespace Cassam.Ui.Hardware.Common.Accessibility;

/// <summary>
/// WCAG 2.x contrast-ratio calculator. Pure functions —
/// no UI types so the static class is callable from any
/// platform, including the unit-test project.
///
/// <para>
/// Reference: https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio
/// </para>
///
/// <para>
/// The ratio formula is <c>(L1 + 0.05) / (L2 + 0.05)</c>
/// where L1 is the lighter colour's relative luminance and
/// L2 is the darker. Inputs are sRGB byte channels; the
/// helper below first linearises each channel per the WCAG
/// sRGB transfer function.
/// </para>
/// </summary>
public static class ContrastRatioCalculator
{
    /// <summary>
    /// Compute the WCAG 2.x contrast ratio between two
    /// colours. Returns a value ≥ 1.0 (a same-colour pair
    /// produces exactly 1.0); a pure black-on-white pair
    /// produces 21.
    /// </summary>
    public static double CalculateContrastRatio(RgbColor foreground, RgbColor background)
    {
        var l1 = RelativeLuminance(foreground);
        var l2 = RelativeLuminance(background);
        var lighter = System.Math.Max(l1, l2);
        var darker = System.Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Determine whether the pair meets WCAG 2.1 AA.
    /// <paramref name="isLargeText"/> follows the WCAG
    /// definition: ≥18 pt OR ≥14 pt bold.
    /// </summary>
    public static bool MeetsAA(double ratio, bool isLargeText)
        => isLargeText
            ? ratio >= WcagThresholds.LargeTextRatio
            : ratio >= WcagThresholds.NormalTextRatio;

    /// <summary>
    /// Determine whether the pair meets the high-contrast
    /// guarantee (≥7:1 for normal text per design §12.2).
    /// </summary>
    public static bool MeetsHighContrast(double ratio, bool isLargeText)
        => isLargeText
            ? ratio >= WcagThresholds.LargeTextRatio * 2.0  // 6:1 floor for large text in HC mode
            : ratio >= WcagThresholds.HighContrastNormalRatio;

    /// <summary>
    /// Compute the relative luminance per WCAG 2.x. The
    /// transfer function is piecewise:
    /// <list type="bullet">
    ///   <item>If channel ≤ 0.03928: <c>c / 12.92</c></item>
    ///   <item>Else: <c>((c + 0.055) / 1.055) ^ 2.4</c></item>
    /// </list>
    /// where <c>c</c> is the sRGB channel value normalised
    /// to 0..1.
    /// </summary>
    public static double RelativeLuminance(RgbColor color)
    {
        return 0.2126 * Linearise(color.R)
             + 0.7152 * Linearise(color.G)
             + 0.0722 * Linearise(color.B);
    }

    private static double Linearise(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928
            ? c / 12.92
            : System.Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
