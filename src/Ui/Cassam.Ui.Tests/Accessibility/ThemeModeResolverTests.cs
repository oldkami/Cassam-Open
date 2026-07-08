using System;
using Cassam.Ui.Hardware.Common.Accessibility;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Accessibility;

/// <summary>
/// Unit tests for <see cref="ThemeModeResolver"/>. PR 10
/// (T2.12). Covers the four-state resolution matrix:
/// explicit mode, auto + bright, auto + dim, auto + missing.
/// </summary>
public class ThemeModeResolverTests
{
    private static readonly AmbientLightReading DimRoom = new(500, DateTimeOffset.UtcNow);
    private static readonly AmbientLightReading Sunlight = new(WcagThresholds.HighContrastAutoLux, DateTimeOffset.UtcNow);

    [Fact]
    public void Explicit_Light_returns_Light()
    {
        ThemeModeResolver.Resolve(ThemeMode.Light, Sunlight, systemIsDark: false)
            .Should().Be(ThemeMode.Light);
        ThemeModeResolver.Resolve(ThemeMode.Light, Sunlight, systemIsDark: true)
            .Should().Be(ThemeMode.Light);
    }

    [Fact]
    public void Explicit_Dark_returns_Dark()
    {
        ThemeModeResolver.Resolve(ThemeMode.Dark, DimRoom, systemIsDark: false)
            .Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public void Explicit_HighContrast_returns_HighContrast()
    {
        ThemeModeResolver.Resolve(ThemeMode.HighContrast, DimRoom, systemIsDark: true)
            .Should().Be(ThemeMode.HighContrast);
    }

    [Fact]
    public void Auto_with_sunlight_returns_HighContrast()
    {
        ThemeModeResolver.Resolve(ThemeMode.Auto, Sunlight, systemIsDark: false)
            .Should().Be(ThemeMode.HighContrast);
        ThemeModeResolver.Resolve(ThemeMode.Auto, Sunlight, systemIsDark: true)
            .Should().Be(ThemeMode.HighContrast);
    }

    [Fact]
    public void Auto_with_dim_returns_light_when_system_is_light()
    {
        ThemeModeResolver.Resolve(ThemeMode.Auto, DimRoom, systemIsDark: false)
            .Should().Be(ThemeMode.Light);
    }

    [Fact]
    public void Auto_with_dim_returns_dark_when_system_is_dark()
    {
        ThemeModeResolver.Resolve(ThemeMode.Auto, DimRoom, systemIsDark: true)
            .Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public void Auto_with_unavailable_sensor_falls_back_to_system_hint()
    {
        var unavailable = AmbientLightReading.Unavailable(DateTimeOffset.UtcNow);
        ThemeModeResolver.Resolve(ThemeMode.Auto, unavailable, systemIsDark: false)
            .Should().Be(ThemeMode.Light);
        ThemeModeResolver.Resolve(ThemeMode.Auto, unavailable, systemIsDark: true)
            .Should().Be(ThemeMode.Dark);
    }
}
