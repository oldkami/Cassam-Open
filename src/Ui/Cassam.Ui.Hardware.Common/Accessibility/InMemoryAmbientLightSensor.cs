using System;
using System.Collections.Generic;
using System.Threading;

namespace Cassam.Ui.Hardware.Common.Accessibility;

/// <summary>
/// In-memory implementation of <see cref="IAmbientLightSensor"/>
/// used by the manager VM (which needs a sensor regardless of
/// platform) and the accessibility audit tests (which need
/// deterministic readings).
/// </summary>
public sealed class InMemoryAmbientLightSensor : IAmbientLightSensor
{
    private AmbientLightReading _current;

    /// <inheritdoc />
    public event EventHandler<AmbientLightReading>? ReadingChanged;

    /// <inheritdoc />
    public AmbientLightReading GetCurrentReading() => _current;

    /// <inheritdoc />
    public bool IsSupported => true;

    public InMemoryAmbientLightSensor(AmbientLightReading initial = default)
    {
        _current = initial.IsAvailable || initial.Timestamp == default
            ? initial
            : new AmbientLightReading(500, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Push a new reading (test + manager VM use). Raises
    /// <see cref="ReadingChanged"/> if the new value differs
    /// from the previous one.
    /// </summary>
    public void Publish(AmbientLightReading reading)
    {
        _current = reading;
        ReadingChanged?.Invoke(this, reading);
    }
}
