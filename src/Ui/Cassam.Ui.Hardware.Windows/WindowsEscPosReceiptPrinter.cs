using System.IO.Ports;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Windows;

/// <summary>
/// Windows implementation of <see cref="IReceiptPrinter"/> that
/// handles all three connection types the design calls out (USB
/// vendor-class, serial / USB-serial, LAN). The printer byte stream
/// is ESC/POS — the renderer (PR 9, T2.09) emits the bytes; the HAL
/// only ships them to the device and applies the standard RJ12
/// cash-drawer kick pulse.
///
/// <para>
/// Connection routing:
/// <list type="bullet">
///   <item><see cref="ConnectionType.Usb"/>  → <c>winspool.drv</c>
///         <c>OpenPrinterW / WritePrinter</c> raw bytes (no driver
///         install needed — most ESC/POS USB printers register as a
///         generic / text-only printer).</item>
///   <item><see cref="ConnectionType.Serial"/> → <c>SerialPort</c>
///         with 9600 8N1 (the de-facto ESC/POS default).</item>
///   <item><see cref="ConnectionType.Lan"/>   → raw <c>TcpClient</c>
///         at port 9100 (the industry-standard "RAW" port used by
///         every Star / Epson / Bixolon LAN printer).</item>
/// </list>
/// </para>
/// </summary>
public sealed class WindowsEscPosReceiptPrinter : IReceiptPrinter
{
    // Standard RJ12 kick pulse on pin 2: ESC p 0 25 250.
    // Sent through the printer port so the hardware pulse reaches the
    // drawer regardless of which transport the printer is on.
    private static readonly byte[] KickPulseBytes =
    {
        0x1B, (byte)'p', 0x00, 25, 250,
    };

    // GS V 0 — full cut. Some low-cost printers ignore this; that is
    // the device's problem, not the HAL's.
    private static readonly byte[] FullCutBytes = { 0x1D, (byte)'V', 0x00 };

    /// <inheritdoc />
    public Task PrintAsync(PrinterDevice device, byte[] escposBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(escposBytes);
        return device.Connection switch
        {
            ConnectionType.Usb => Task.Run(() => WriteViaWindowsSpool(device.Path, escposBytes), ct),
            ConnectionType.Serial => Task.Run(() => WriteViaSerial(device.Path, escposBytes), ct),
            ConnectionType.Lan => Task.Run(() => WriteViaLan(device.Path, escposBytes), ct),
            _ => throw new NotSupportedException(
                $"Printer connection '{device.Connection}' is not supported on Windows."),
        };
    }

    /// <inheritdoc />
    public Task CutPaperAsync(PrinterDevice device, CancellationToken ct) =>
        PrintAsync(device, FullCutBytes, ct);

    /// <inheritdoc />
    public Task KickCashDrawerAsync(PrinterDevice device, CancellationToken ct) =>
        PrintAsync(device, KickPulseBytes, ct);

    // ---- USB vendor-class (winspool.drv) ---------------------------------

    private static void WriteViaWindowsSpool(string printerName, byte[] payload)
    {
        if (!NativeMethods.OpenPrinterW(printerName, out var hPrinter, nint.Zero))
        {
            throw new InvalidOperationException(
                $"OpenPrinterW failed for '{printerName}'. Verify the printer is installed " +
                "as a Generic / Text Only device and reachable from this user session.");
        }

        try
        {
            var docInfo = new NativeMethods.DOCINFOW
            {
                DocName = "Cassam POS Receipt",
                OutputFile = null,
                DataType = "RAW",
            };

            var docInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.DOCINFOW>());
            try
            {
                Marshal.StructureToPtr(docInfo, docInfoPtr, false);
                if (NativeMethods.StartDocPrinterW(hPrinter, 1, docInfoPtr) == 0)
                {
                    throw new InvalidOperationException(
                        $"StartDocPrinterW failed for '{printerName}'.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(docInfoPtr);
            }

            try
            {
                // The RAW write happens as a single chunk because most
                // ESC/POS printers expect contiguous bytes for the
                // receipt body; chunking would split graphic / barcode
                // commands.
                var buffer = Marshal.AllocHGlobal(payload.Length);
                try
                {
                    Marshal.Copy(payload, 0, buffer, payload.Length);
                    if (!NativeMethods.WritePrinter(hPrinter, buffer, (uint)payload.Length, out _))
                    {
                        throw new InvalidOperationException(
                            $"WritePrinter failed for '{printerName}'.");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                if (!NativeMethods.EndDocPrinter(hPrinter))
                {
                    throw new InvalidOperationException("EndDocPrinter failed.");
                }
            }
            finally
            {
                NativeMethods.ClosePrinter(hPrinter);
            }
        }
        catch
        {
            // Best-effort: close on failure too, so the printer handle
            // does not leak if WritePrinter threw mid-job.
            NativeMethods.ClosePrinter(hPrinter);
            throw;
        }
    }

    // ---- Serial (USB-serial, native COMx) -------------------------------

    private static void WriteViaSerial(string portName, byte[] payload)
    {
        using var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            WriteTimeout = 5000,
        };
        port.Open();
        port.Write(payload, 0, payload.Length);
    }

    // ---- LAN (RAW TCP port 9100) ----------------------------------------

    private static void WriteViaLan(string hostAndPort, byte[] payload)
    {
        // Device.Path is expected to be either "host" (default port
        // 9100) or "host:port".
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
