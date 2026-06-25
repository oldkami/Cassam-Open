namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Transport the HAL uses to describe a discovered peripheral. The
/// <see cref="Connection"/> field drives the per-platform code path
/// inside the matching implementation (PR 8, T2.05..T2.07). The
/// <see cref="Path"/> is OS-specific — a Windows device path, a Linux
/// <c>/dev/ttyUSB0</c> node, a macOS <c>/dev/cu.usbserial</c> node,
/// a Bluetooth MAC address, or a LAN URL.
/// </summary>
public enum ConnectionType
{
    /// <summary>USB-attached device (USB-HID, USB-CDC, USB vendor class).</summary>
    Usb,

    /// <summary>RS-232 serial port (COM1, /dev/ttyUSB0, /dev/cu.usbserial).</summary>
    Serial,

    /// <summary>TCP-attached device (raw socket, IPP, vendor protocol).</summary>
    Lan,

    /// <summary>Bluetooth — either classic SPP or BLE GATT.</summary>
    Bluetooth,

    /// <summary>Web (WASM) camera-only barcode (REQ-UI-06).</summary>
    Camera,
}
