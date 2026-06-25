namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Device descriptor for a discovered thermal printer. The station
/// configuration (T2.09) stores a list of these — one per
/// <c>user_settings.station_id</c> row — so the cashier flow can
/// pick the right printer without re-enumeration on every sale.
/// </summary>
public sealed record PrinterDevice(
    string Id,
    string Name,
    ConnectionType Connection,
    string Path);
