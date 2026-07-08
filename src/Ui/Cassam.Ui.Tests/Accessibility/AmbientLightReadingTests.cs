using System;
using Cassam.Ui.Hardware.Common.Accessibility;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Accessibility;

/// <summary>
/// Unit tests for <see cref="AmbientLightReading"/>.
/// PR 10 (T2.12). Verifies the high-contrast threshold
/// + the "unavailable" sentinel value.
/// </summary>
public class AmbientLightReadingTests
{
    [Fact]
    public void IsAvailable_is_false_for_NaN()
    {
        var r = AmbientLightReading.Unavailable(DateTimeOffset.UtcNow);
        r.IsAvailable.Should().BeFalse();
        r.IsBrightEnoughForHighContrast.Should().BeFalse();
    }

    [Fact]
    public void IsAvailable_is_true_for_normal_value()
    {
        var r = new AmbientLightReading(500, DateTimeOffset.UtcNow);
        r.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Threshold_below_25_000_lux_does_not_trigger_high_contrast()
    {
        var r = new AmbientLightReading(24999, DateTimeOffset.UtcNow);
        r.IsBrightEnoughForHighContrast.Should().BeFalse();
    }

    [Fact]
    public void Threshold_at_25_000_lux_triggers_high_contrast()
    {
        var r = new AmbientLightReading(WcagThresholds.HighContrastAutoLux, DateTimeOffset.UtcNow);
        r.IsBrightEnoughForHighContrast.Should().BeTrue();
    }

    [Fact]
    public void Threshold_above_25_000_lux_triggers_high_contrast()
    {
        var r = new AmbientLightReading(50000, DateTimeOffset.UtcNow);
        r.IsBrightEnoughForHighContrast.Should().BeTrue();
    }

    [Fact]
    public void NaN_value_does_not_trigger_high_contrast_even_when_comparing()
    {
        var r = AmbientLightReading.Unavailable(DateTimeOffset.UtcNow);
        // NaN comparisons return false so this stays false even
        // if the threshold logic flips.
        r.IsBrightEnoughForHighContrast.Should().BeFalse();
    }
}
