namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Device descriptor for a directly-attached cash drawer. See
/// <see cref="ICashDrawer"/> — most drawers are kicked through the
/// printer, so this record is rarely used.
/// </summary>
public sealed record CashDrawerDevice(
    string Id,
    string Name,
    ConnectionType Connection,
    string Path);
