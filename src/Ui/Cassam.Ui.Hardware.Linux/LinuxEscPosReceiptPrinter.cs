using System.IO.Ports;
using System.Net.Sockets;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Linux;

/// <summary>
/// Linux implementation of <see cref="IReceiptPrinter"/>.
///
/// <para>
/// The transport surface is identical to Windows: serial over
/// <c>/dev/ttyUSB*</c> or native <c>/dev/ttyS*</c> at 9600 8N1, LAN
/// over raw TCP port 9100. ESC/POS bytes are emitted unchanged.
/// </para>
///
/// <para>
/// USB-vendor-class printers on Linux: not implemented in PR 7.
/// Linux desktop retail is overwhelmingly LAN-attached (Star
/// TSP100LAN, Epson TM-T20II-i) and the USB vendor-class path is
/// only relevant to embedded SBC stations. The libusb-1.0 P/Invoke
/// adapter (design §6.2 "Linux (GTK)" row) lands in a follow-up
/// PR alongside the barcode-scanner evdev work.
/// </para>
///
/// <para>
/// Per-station device paths are operator-configured in
/// <c>user_settings</c>; the implementation does NOT enumerate
/// /dev/tty* at runtime (operators occasionally unplug devices mid-
/// shift and we want the cashier flow to fail loud, not auto-pick
/// the wrong port).
/// </para>
/// </summary>
public sealed class LinuxEscPosReceiptPrinter : IReceiptPrinter
{
    // Standard RJ12 kick pulse on pin 2: ESC p 0 25 250.
    private static readonly byte[] KickPulseBytes =
    {
        0x1B, (byte)'p', 0x00, 25, 250,
    };

    // GS V 0 — full cut. Same caveat as Windows: some low-cost
    // printers ignore this; that is the device's problem.
    private static readonly byte[] FullCutBytes = { 0x1D, (byte)'V', 0x00 };

    /// <inheritdoc />
    public Task PrintAsync(PrinterDevice device, byte[] escposBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(escposBytes);
        return device.Connection switch
        {
            ConnectionType.Serial => Task.Run(() => WriteViaSerial(device.Path, escposBytes), ct),
            ConnectionType.Lan => Task.Run(() => WriteViaLan(device.Path, escposBytes), ct),
            ConnectionType.Usb => throw new NotSupportedException(
                "USB vendor-class printing on Linux requires libusb-1.0 (PR 8 follow-up). " +
                "Configure the printer as Serial (/dev/ttyUSB0) or Lan (host:9100) for now."),
            _ => throw new NotSupportedException(
                $"Printer connection '{device.Connection}' is not supported on Linux."),
        };
    }

    /// <inheritdoc />
    public Task CutPaperAsync(PrinterDevice device, CancellationToken ct) =>
        PrintAsync(device, FullCutBytes, ct);

    /// <inheritdoc />
    public Task KickCashDrawerAsync(PrinterDevice device, CancellationToken ct) =>
        PrintAsync(device, KickPulseBytes, ct);

    // ---- Serial (/dev/ttyUSB*, /dev/ttyS*) -----------------------------

    private static void WriteViaSerial(string devicePath, byte[] payload)
    {
        using var port = new SerialPort(devicePath, 9600, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            WriteTimeout = 5000,
        };
        port.Open();
        port.Write(payload, 0, payload.Length);
    }

    // ---- LAN (raw TCP, default port 9100) ------------------------------

    private static void WriteViaLan(string hostAndPort, byte[] payload)
    {
        var lastColon = hostAndPort.LastIndexOf(':');
        string host;
        int port = 9100;
        if (lastColon > 0 && int.TryParse(hostAndPort[(lastColon + 1)..], out var explicitPort))
        {
            host = hostAndPort[..lastColon];
            port = explicitPort;
        }
        else
        {
            host = hostAndPort;
        }

        using var client = new TcpClient();
        client.Connect(host, port);
        using var stream = client.GetStream();
        stream.Write(payload, 0, payload.Length);
    }
}
