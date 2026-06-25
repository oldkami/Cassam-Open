using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.MacOS;

/// <summary>
/// macOS implementation of <see cref="ICustomerPoleDisplay"/>.
///
/// <para>
/// Same CD-7220 command set as Windows + Linux. The pole display
/// is hardware-firmware-agnostic — only the OS-side file path
/// differs (<c>/dev/cu.usbserial</c> on macOS). Command bytes and
/// timing are identical, so we deliberately duplicate the
/// implementation rather than share a common library: the
/// per-platform project layout is part of the design's stable
/// seam (REQ-UI-04).
/// </para>
///
/// <para>
/// The macOS path is the rarest of the three: macOS desktop retail
/// is uncommon (operator stations in Colombia overwhelmingly run
/// Windows + Android). The HAL is shipped anyway because the
/// Skia Desktop head (`net10.0-desktop`) is the same artifact that
/// runs on macOS, and a missing HAL interface would break the DI
/// registration at startup.
/// </para>
/// </summary>
public sealed class MacOSSerialPoleDisplay : ICustomerPoleDisplay
{
    private const byte Esc = 0x1B;
    private const byte Cr = 0x0D;
    private const byte Lf = 0x0A;

    private static readonly byte[] InitializeBytes = { Esc, (byte)'@' };
    private static readonly byte[] SelectCodePage850 = { Esc, (byte)'t', 0x02 };

    /// <inheritdoc />
    public Task ShowTotalAsync(
        PoleDisplayDevice device, decimal total, string currency, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return Task.Run(() =>
        {
            using var port = new SerialPort(device.Path, 9600, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                WriteTimeout = 3000,
            };
            port.Open();

            var line = $"{currency} {total.ToString("N2", CultureInfo.GetCultureInfo("es-CO"))}";
            var payload = BuildPayload(line);
            port.Write(payload, 0, payload.Length);
        }, ct);
    }

    /// <inheritdoc />
    public Task ClearAsync(PoleDisplayDevice device, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var port = new SerialPort(device.Path, 9600, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                WriteTimeout = 3000,
            };
            port.Open();
            port.Write(InitializeBytes, 0, InitializeBytes.Length);
        }, ct);
    }

    private static byte[] BuildPayload(string line)
    {
        var truncated = line.Length > 20 ? line[..20] : line;

        var bytes = new List<byte>(InitializeBytes.Length + SelectCodePage850.Length + truncated.Length + 2);
        bytes.AddRange(InitializeBytes);
        bytes.AddRange(SelectCodePage850);
        bytes.Add(Esc); bytes.Add((byte)'Q'); bytes.Add(0); bytes.Add(0);
        bytes.AddRange(Encoding.Latin1.GetBytes(truncated));
        bytes.Add(Cr);
        bytes.Add(Lf);
        return bytes.ToArray();
    }
}
