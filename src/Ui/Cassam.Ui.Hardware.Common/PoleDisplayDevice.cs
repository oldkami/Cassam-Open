namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Device descriptor for a customer-facing pole display.
/// </summary>
public sealed record PoleDisplayDevice(
    string Id,
    string Name,
    ConnectionType Connection,
    string Path);
