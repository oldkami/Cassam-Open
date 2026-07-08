using System;
using Cassam.Ui.Hardware.Common.Accessibility;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Accessibility;

/// <summary>
/// Pin-tests for <see cref="WcagThresholds"/>. PR 10 (T2.12).
/// If a threshold changes, the test fails so the change is
/// visible at review time — a 4.5:1 bump from the design
/// should land as one PR, not silently.
/// </summary>
public class WcagThresholdsTests
{
    [Fact]
    public void Normal_text_ratio_is_4_5()
    {
        WcagThresholds.NormalTextRatio.Should().Be(4.5);
    }

    [Fact]
    public void Large_text_ratio_is_3()
    {
        WcagThresholds.LargeTextRatio.Should().Be(3.0);
    }

    [Fact]
    public void High_contrast_normal_ratio_is_7()
    {
        WcagThresholds.HighContrastNormalRatio.Should().Be(7.0);
    }

    [Fact]
    public void Touch_target_min_is_44_dp()
    {
        WcagThresholds.MinTouchTargetDp.Should().Be(44.0);
    }

    [Fact]
    public void Ambient_light_auto_threshold_is_25_000_lux()
    {
        WcagThresholds.HighContrastAutoLux.Should().Be(25000.0);
    }

    [Fact]
    public void Large_text_pt_is_18()
    {
        WcagThresholds.LargeTextPointSize.Should().Be(18.0);
    }

    [Fact]
    public void Large_text_bold_pt_is_14()
    {
        WcagThresholds.LargeTextBoldPointSize.Should().Be(14.0);
    }
}
