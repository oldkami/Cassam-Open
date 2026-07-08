namespace Cassam.Ui.Hardware.Common.Accessibility;

/// <summary>
/// Decides the manager-view theme based on the ambient-light
/// reading + operator override. Lives in
/// <c>Cassam.Ui.Hardware.Common</c> so tests can validate the
/// decision logic without instantiating any Uno UI types.
/// </summary>
/// <remarks>
/// <para>
/// Mapping (design §12.1):
/// </para>
/// <list type="bullet">
///   <item>Operator picks "Auto" + sensor > 25 000 lux → HighContrast</item>
///   <item>Operator picks "Auto" + sensor ≤ 25 000 lux → Light</item>
///   <item>Operator picks "Auto" + sensor unavailable → Light (fallback, R-UI-10)</item>
///   <item>Operator picks "Light" / "Dark" / "HighContrast" explicitly → use that</item>
/// </list>
/// </remarks>
public enum ThemeMode
{
    /// <summary>Light theme (default).</summary>
    Light = 0,

    /// <summary>Dark theme (evening / low-light).</summary>
    Dark = 1,

    /// <summary>High-contrast theme (sunlight / accessibility).</summary>
    HighContrast = 2,

    /// <summary>Follow the OS + ambient-light sensor + system Dark mode.</summary>
    Auto = 3,
}

/// <summary>
/// Pure-function helper: maps a <see cref="ThemeMode"/> +
/// reading to the theme that should actually be applied.
/// Separated from the VM so the audit test exercises the
/// decision logic without spinning up a host.
/// </summary>
public static class ThemeModeResolver
{
    public static ThemeMode Resolve(ThemeMode mode, AmbientLightReading reading, bool systemIsDark)
    {
        if (mode != ThemeMode.Auto)
        {
            return mode;
        }

        if (!reading.IsAvailable)
        {
            // Sensor missing: fall back to the OS hint so
            // Windows users get Dark mode at night, Light
            // mode at day, etc.
            return systemIsDark ? ThemeMode.Dark : ThemeMode.Light;
        }

        return reading.IsBrightEnoughForHighContrast
            ? ThemeMode.HighContrast
            : (systemIsDark ? ThemeMode.Dark : ThemeMode.Light);
    }
}
