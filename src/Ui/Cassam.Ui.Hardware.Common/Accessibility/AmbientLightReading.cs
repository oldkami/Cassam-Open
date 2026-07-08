using System;

namespace Cassam.Ui.Hardware.Common.Accessibility;

/// <summary>
/// One ambient-light reading as captured by
/// <see cref="IAmbientLightSensor"/>. Stored as lux per the
/// Android sensor convention (values >0 in any environment;
/// values can exceed 100 000 lux in direct sunlight).
/// </summary>
/// <param name="Lux">Illuminance in lux (0..120 000 typical range).</param>
/// <param name="Timestamp">UTC wall-clock when the reading was sampled.</param>
public readonly record struct AmbientLightReading(double Lux, DateTimeOffset Timestamp)
{
    /// <summary>Sensor disconnected / unavailable — <see cref="Lux"/> is <c>double.NaN</c>.</summary>
    public static AmbientLightReading Unavailable(DateTimeOffset when)
        => new(double.NaN, when);

    /// <summary>True if the sensor returned a real numeric reading.</summary>
    public bool IsAvailable => !double.IsNaN(Lux);

    /// <summary>
    /// Returns <c>true</c> when the reading exceeds the
    /// ambient-light threshold for switching to a
    /// high-contrast theme (design §9.4: 25 000 lux).
    /// Treats unavailable readings as "brightness unknown"
    /// so the manager VM can defer to the user setting.
    /// </summary>
    public bool IsBrightEnoughForHighContrast
        => IsAvailable && Lux >= WcagThresholds.HighContrastAutoLux;
}
