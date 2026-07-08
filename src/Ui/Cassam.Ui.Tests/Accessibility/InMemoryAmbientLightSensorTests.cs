using System;
using Cassam.Ui.Hardware.Common.Accessibility;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Accessibility;

/// <summary>
/// Unit tests for <see cref="InMemoryAmbientLightSensor"/>.
/// PR 10 (T2.12). Verifies that publish + event flow work
/// for the manager VM's accessibility auto-theme logic.
/// </summary>
public class InMemoryAmbientLightSensorTests
{
    [Fact]
    public void Sensor_starts_with_zero_lux_by_default()
    {
        var sensor = new InMemoryAmbientLightSensor();
        sensor.GetCurrentReading().Lux.Should().Be(0);
    }

    [Fact]
    public void Sensor_starts_with_supplied_initial_value()
    {
        var sensor = new InMemoryAmbientLightSensor(new AmbientLightReading(800, DateTimeOffset.UtcNow));
        sensor.GetCurrentReading().Lux.Should().Be(800);
    }

    [Fact]
    public void Publish_updates_current_reading_and_raises_event()
    {
        var sensor = new InMemoryAmbientLightSensor();
        var received = 0;
        sensor.ReadingChanged += (_, _) => received++;

        var reading = new AmbientLightReading(22000, DateTimeOffset.UtcNow);
        sensor.Publish(reading);

        sensor.GetCurrentReading().Lux.Should().Be(22000);
        received.Should().Be(1);
    }

    [Fact]
    public void Publish_to_supplied_unavailable_reading_still_emits_event()
    {
        var sensor = new InMemoryAmbientLightSensor();
        var lastReading = (AmbientLightReading?)null;
        sensor.ReadingChanged += (_, r) => lastReading = r;

        var unavailable = AmbientLightReading.Unavailable(DateTimeOffset.UtcNow);
        sensor.Publish(unavailable);

        lastReading.Should().Be(unavailable);
    }

    [Fact]
    public void IsSupported_is_true_on_in_memory_implementation()
    {
        var sensor = new InMemoryAmbientLightSensor();
        sensor.IsSupported.Should().BeTrue();
    }

    [Fact]
    public void Default_initial_value_with_zero_lux_is_overridden()
    {
        // Constructor picks 500 lux when caller passes an
        // unavailable reading — verify that fallback works
        // as documented.
        var sentinel = AmbientLightReading.Unavailable(DateTimeOffset.UtcNow);
        var sensor = new InMemoryAmbientLightSensor(sentinel);
        sensor.GetCurrentReading().IsAvailable.Should().BeTrue();
    }
}
