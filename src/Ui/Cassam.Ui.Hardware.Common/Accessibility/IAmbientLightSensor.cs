using System;

namespace Cassam.Ui.Hardware.Common.Accessibility;

/// <summary>
/// Ambient-light sensor abstraction (design §9.4). The
/// per-platform implementation lives in
/// <c>Cassam.Ui.Hardware.Android</c> (Phase 2 PR 7) reading
/// <c>Sensor.TYPE_LIGHT</c>; Windows + macOS + WASM may
/// emit <see cref="AmbientLightReading.Unavailable"/>.
///
/// <para>
/// The interface intentionally exposes a single event + a
/// current-reading method — the manager VM subscribes once
/// and listens for changes, so per-platform differences
/// (Android continuous mode, Windows on-demand) collapse
/// into one API.
/// </para>
/// </summary>
public interface IAmbientLightSensor
{
    /// <summary>
    /// Raised when the sensor publishes a new reading.
    /// Listeners may receive an <see cref="AmbientLightReading.Unavailable"/>
    /// payload if the sensor disconnects mid-session (R-UI-10
    /// fallback — manager UI prompts the operator to choose
    /// a theme manually).
    /// </summary>
    event EventHandler<AmbientLightReading> ReadingChanged;

    /// <summary>Returns the latest reading or <see cref="AmbientLightReading.Unavailable"/>.</summary>
    AmbientLightReading GetCurrentReading();

    /// <summary>True when the sensor is present on the current platform + device.</summary>
    bool IsSupported { get; }
}
