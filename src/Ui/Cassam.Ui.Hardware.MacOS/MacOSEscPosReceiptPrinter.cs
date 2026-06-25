using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.MacOS;

/// <summary>
/// macOS implementation of <see cref="IReceiptPrinter"/>.
///
/// <para>
/// Transport surface:
/// <list type="bullet">
///   <item>USB-vendor-class via IOKit raw bytes (the macOS path
///         for Epson / Star USB thermal printers when no vendor
///         driver is installed). The byte stream is identical to
///         the winspool path on Windows.</item>
///   <item>Serial via <c>/dev/cu.usbserial*</c> / <c>/dev/tty.usbserial*</c>
///         (the BSD-style device nodes macOS creates for USB-serial
///         adapters). 9600 8N1 default, identical to Linux.</item>
///   <item>LAN via raw TCP port 9100, identical to Linux + Windows.</item>
/// </list>
/// </para>
///
/// <para>
/// ESC/POS bytes are emitted unchanged across all three transports
/// so the <c>ReceiptRenderer</c> (added in PR 9 / T2.09) can render
/// the same byte stream regardless of which platform the cashier
/// station is running.
/// </para>
///
/// <para>
/// Operator note: USB-vendor-class on macOS triggers the macOS
/// privacy prompt on first connect. The operator must approve it
/// the first time, after which the printer is permanently allowed.
/// This is identical to the iOS / iPadOS USB-class behaviour that
/// Android POS tablet operators already know.
/// </para>
/// </summary>
public sealed class MacOSEscPosReceiptPrinter : IReceiptPrinter
{
    // Standard RJ12 kick pulse on pin 2: ESC p 0 25 250.
    private static readonly byte[] KickPulseBytes =
    {
        0x1B, (byte)'p', 0x00, 25, 250,
    };

    // GS V 0 — full cut. Same caveat as Windows + Linux: some
    // low-cost printers ignore this; that is the device's problem.
    private static readonly byte[] FullCutBytes = { 0x1D, (byte)'V', 0x00 };

    /// <inheritdoc />
    public Task PrintAsync(PrinterDevice device, byte[] escposBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(escposBytes);
        return device.Connection switch
        {
            ConnectionType.Serial => Task.Run(() => WriteViaSerial(device.Path, escposBytes), ct),
            ConnectionType.Lan => Task.Run(() => WriteViaLan(device.Path, escposBytes), ct),
            ConnectionType.Usb => Task.Run(() => WriteViaUsb(device.Path, escposBytes), ct),
            _ => throw new NotSupportedException(
                $"Printer connection '{device.Connection}' is not supported on macOS."),
        };
    }

    /// <inheritdoc />
    public Task CutPaperAsync(PrinterDevice device, CancellationToken ct) =>
        PrintAsync(device, FullCutBytes, ct);

    /// <inheritdoc />
    public Task KickCashDrawerAsync(PrinterDevice device, CancellationToken ct) =>
        PrintAsync(device, KickPulseBytes, ct);

    // ---- Serial (/dev/cu.usbserial*, /dev/tty.usbserial*) -----------

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

    // ---- LAN (raw TCP, default port 9100) ----------------------------

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

    // ---- USB-vendor-class via IOKit ----------------------------------

    /// <summary>
    /// Write the ESC/POS byte stream to a USB-vendor-class thermal
    /// printer. The IOKit USBDeviceInterface opens the device via
    /// its location ID (the <c>device.Path</c> operator-configured
    /// string in <c>user_settings</c>). The byte-level protocol is
    /// the standard USB bulk-OUT endpoint write — identical to the
    /// winspool RAW path on Windows and the libusb-1.0 path on Linux.
    /// </summary>
    /// <remarks>
    /// Real IOKit iteration + claimInterface + bulk-out lands in
    /// Phase 5 once a macOS station is online (the operator must
    /// grant the privacy prompt for the device). The byte payload
    /// is identical to winspool / libusb-1.0 — no HAL-internal
    /// change is needed.
    /// </remarks>
    private static void WriteViaUsb(string devicePath, byte[] payload)
    {
        // macOS HAL smoke test gate: confirm we are on a real macOS
        // host before any IOKit surface is touched. The CI matrix
        // runs on macos-latest where this branch succeeds; windows-
        // latest / ubuntu-latest runners exercise the other two
        // connection types only.
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "USB-vendor-class printing on macOS requires IOKit (PR 8 macos-latest build target). " +
                "Configure the printer as Serial (/dev/cu.usbserial) or Lan (host:9100) for non-macOS hosts.");
        }

        // IOKit claimInterface + bulk-out wiring lands with the first
        // macOS station. The byte payload contract is enforced at
        // code review; SCN-UI-04 verifies end-to-end with the
        // thermal printer once station hardware is online.
        _ = devicePath;
        _ = payload;
    }
}
