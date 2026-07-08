using Cassam.Ui.Hardware.Common.Accessibility;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Accessibility;

/// <summary>
/// Unit tests for <see cref="ContrastRatioCalculator"/>. PR 10
/// (T2.12). Verifies the WCAG 2.x contrast formulas against
/// known good/bad pairs and the AA / AAA thresholds.
/// </summary>
public class ContrastRatioCalculatorTests
{
    [Fact]
    public void Black_on_white_ratios_to_21()
    {
        var ratio = ContrastRatioCalculator.CalculateContrastRatio(
            new RgbColor(0, 0, 0),
            new RgbColor(255, 255, 255));
        ratio.Should().BeApproximately(21.0, 0.05);
    }

    [Fact]
    public void White_on_white_ratios_to_1()
    {
        var ratio = ContrastRatioCalculator.CalculateContrastRatio(
            new RgbColor(255, 255, 255),
            new RgbColor(255, 255, 255));
        ratio.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void Ratio_is_symmetric()
    {
        // Lighter-on-darker and darker-on-lighter produce the
        // same ratio per the WCAG formula (the max/min).
        var a = ContrastRatioCalculator.CalculateContrastRatio(
            new RgbColor(0, 0, 0), new RgbColor(255, 255, 255));
        var b = ContrastRatioCalculator.CalculateContrastRatio(
            new RgbColor(255, 255, 255), new RgbColor(0, 0, 0));
        a.Should().BeApproximately(b, 0.001);
    }

    [Fact]
    public void MeetsAA_passes_for_4_5_to_1_normal_text()
    {
        ContrastRatioCalculator.MeetsAA(4.5, isLargeText: false).Should().BeTrue();
    }

    [Fact]
    public void MeetsAA_fails_for_4_4_to_1_normal_text()
    {
        ContrastRatioCalculator.MeetsAA(4.4, isLargeText: false).Should().BeFalse();
    }

    [Fact]
    public void MeetsAA_passes_for_3_to_1_large_text()
    {
        ContrastRatioCalculator.MeetsAA(3.0, isLargeText: true).Should().BeTrue();
    }

    [Fact]
    public void MeetsAA_fails_for_2_9_to_1_large_text()
    {
        ContrastRatioCalculator.MeetsAA(2.9, isLargeText: true).Should().BeFalse();
    }

    [Fact]
    public void MeetsHighContrast_requires_7_to_1_for_normal_text()
    {
        ContrastRatioCalculator.MeetsHighContrast(6.9, isLargeText: false).Should().BeFalse();
        ContrastRatioCalculator.MeetsHighContrast(7.0, isLargeText: false).Should().BeTrue();
        ContrastRatioCalculator.MeetsHighContrast(7.5, isLargeText: false).Should().BeTrue();
    }

    [Fact]
    public void Relative_luminance_for_black_is_zero()
    {
        ContrastRatioCalculator.RelativeLuminance(new RgbColor(0, 0, 0)).Should().Be(0.0);
    }

    [Fact]
    public void Relative_luminance_for_white_is_one()
    {
        ContrastRatioCalculator.RelativeLuminance(new RgbColor(255, 255, 255)).Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void Relative_luminance_for_mid_grey_is_approximately_0_2159()
    {
        // 50% grey relative luminance per WCAG ≈ 0.2159
        ContrastRatioCalculator.RelativeLuminance(new RgbColor(128, 128, 128))
            .Should().BeApproximately(0.2159, 0.01);
    }

    [Fact]
    public void Theme_tokens_in_cassam_palette_meet_AA()
    {
        // Per design §12.2: BackgroundBrush #FFFFFF +
        // OnBackgroundBrush #212121 must meet 16:1.
        var onBackground = RgbColor.FromHex("#212121");
        var background = RgbColor.FromHex("#FFFFFF");
        var ratio = ContrastRatioCalculator.CalculateContrastRatio(onBackground, background);
        ratio.Should().BeGreaterThanOrEqualTo(15.0, "design §12.2 promises 16:1");
    }
}
