using System.Globalization;
using System.IO.Ports;
using System.Text;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Windows;

/// <summary>
/// Windows implementation of <see cref="ICustomerPoleDisplay"/> that
/// drives a serial VFD / LCD using the CD-7220 command set (the de
/// facto command language for Bixolon BCD-1000 / Epson DM-D110 /
/// Posiflex PD-2800 pole displays sold throughout Latin America).
///
/// <para>
/// Connection: serial (COMx or USB-serial) at 9600 8N1. The pole
/// display is rare on Web (WASM) and unsupported on Android by
/// design (REQ-UI-06) so we only ship the Windows variant in
/// PR 7. macOS / Linux would copy the same byte sequence verbatim —
/// only the device-path lookup differs.
/// </para>
///
/// <para>
/// Command sequence (CD-7220):
/// <list type="number">
///   <item>ESC @ — initialize the display (clears previous content).</item>
///   <item>Move cursor to column 0 row 0 (top line — the running total
///         always lives on the top line per DIAN UI conventions).</item>
///   <item>Print <c>{currency} {total}</c> with the customer's
///         locale (es-CO) so the thousands separator reads naturally
///         to the customer.</item>
/// </list>
/// </para>
/// </summary>
public sealed class WindowsSerialPoleDisplay : ICustomerPoleDisplay
{
    private const byte Esc = 0x1B;
    private const byte Cr = 0x0D;
    private const byte Lf = 0x0A;

    // ESC @  — initialize display, clear all character data.
    private static readonly byte[] InitializeBytes = { Esc, (byte)'@' };

    // ESC t 0 — select code table 0 (PC437 / USA — diacritics come
    // through the firmware's Latin-1 fallback; Spanish needs "ñ" and
    // accented vowels which CD-7220 covers via code table 2).
    // We send code table 2 (PC850 multilingual) so es-CO totals show
    // correctly when the customer's locale prints them.
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
        // Clearing == initializing (ESC @) on a CD-7220.
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
        // CD-7220 displays 20 columns × 2 rows. We put the running
        // total on the top row; truncation is the operator's problem
        // (totals beyond COP 99.999.999,99 fit but COP billions do
        // not — that case is rare in retail).
        var truncated = line.Length > 20 ? line[..20] : line;

        var bytes = new List<byte>(InitializeBytes.Length + SelectCodePage850.Length + truncated.Length + 2);
        bytes.AddRange(InitializeBytes);
        bytes.AddRange(SelectCodePage850);

        // Move cursor to row 0, column 0. CD-7220 uses 1-based row
        // numbers so we send ESC $ (set absolute column = 0).
        bytes.Add(Esc); bytes.Add((byte)'$'); bytes.Add(0); bytes.Add(0);

        bytes.AddRange(Encoding.Latin1.GetBytes(truncated));
        bytes.Add(Cr);
        bytes.Add(Lf);
        return bytes.ToArray();
    }
}
