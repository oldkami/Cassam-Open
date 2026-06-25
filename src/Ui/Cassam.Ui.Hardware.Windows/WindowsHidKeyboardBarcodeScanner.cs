using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Windows;

/// <summary>
/// Windows implementation of <see cref="IBarcodeScanner"/> that
/// listens to a global low-level keyboard hook (WH_KEYBOARD_LL) and
/// decodes HID-keyboard-mode barcode scanner output.
///
/// <para>
/// Why this works without a vendor driver:
/// Most retail barcode scanners ship with two modes — USB-HID
/// keyboard (no driver needed, scanner emulates a keyboard) and
/// USB-COM / USB-vendor (driver required). The HID mode is the
/// default in 95 % of retail deployments because it works on any
/// Windows / Linux / macOS station with zero install — see design
/// §6.2 "Windows" row + REQ-UI-04.
/// </para>
///
/// <para>
/// Heuristic for separating scanner input from cashier typing:
/// <list type="number">
///   <item>Buffer characters until a 5 ms gap, Enter, or Tab is
///         observed.</item>
///   <item>If the buffer length is 4..32 and the terminating key was
///         Enter, raise <see cref="BarcodeRead"/> with the buffer
///         contents. Otherwise drop the buffer (treat as cashier
///         typing into the search box).</item>
///   <item>Cap any single buffer at 64 chars to avoid pathological
///         memory growth if a key is held.</item>
/// </list>
/// </para>
///
/// <para>
/// Thread-safety:
/// <list type="bullet">
///   <item>The hook callback runs on the UI thread that installed
///         it (the WPF / WinUI message pump).</item>
///   <item><see cref="SimulateScan"/> is exposed for tests so the
///         caller can push a code into the event stream without
///         poking the keyboard hook.</item>
/// </list>
/// </para>
/// </summary>
public sealed class WindowsHidKeyboardBarcodeScanner : IBarcodeScanner
{
    /// <inheritdoc />
    public event EventHandler<string>? BarcodeRead;

    private readonly ConcurrentQueue<string> _receivedScans = new();
    private bool _running;
    private nint _hookHandle;
    private NativeMethods.LowLevelKeyboardProc? _hookDelegate;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    // Edge-detection state for the heuristic above. Kept on the
    // instance because WH_KEYBOARD_LL is per-thread; on a single-UI
    // thread station this is exactly what we want.
    private readonly System.Text.StringBuilder _buffer = new();
    private DateTime _lastCharAt;

    /// <summary>Codes the scanner has emitted so far (test surface).</summary>
    public IReadOnlyCollection<string> ReceivedScans => _receivedScans.ToArray();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _running = true;

        // Install the hook on the current thread. The thread must run
        // a Windows message loop (the UI thread always does); we
        // deliberately avoid any background thread here because
        // WH_KEYBOARD_LL on a non-message-loop thread silently stops
        // firing after a few seconds.
        _hookDelegate = HookCallback;
        _hookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_KEYBOARD_LL,
            _hookDelegate,
            NativeMethods.GetModuleHandleW(null),
            0);

        if (_hookHandle == nint.Zero)
        {
            _running = false;
            throw new InvalidOperationException(
                "SetWindowsHookEx(WH_KEYBOARD_LL) failed — barcode scanner cannot start.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct)
    {
        if (!_running) return Task.CompletedTask;
        _running = false;

        if (_hookHandle != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }

        _hookDelegate = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test helper — push a code as if the WH_KEYBOARD_LL hook had
    /// decoded it. Bypasses the heuristic so unit tests can assert
    /// event ordering without firing fake keystrokes.
    /// </summary>
    public void SimulateScan(string code)
    {
        if (!_running) return;
        Emit(code);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        // Only act on key-down events (HC_ACTION = 0, WM_KEYDOWN = 0x0100).
        if (nCode >= 0 && wParam == 0x0100)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var now = DateTime.UtcNow;

            // Reset buffer after a 5 ms silence — separates two scans.
            if (_buffer.Length > 0 && (now - _lastCharAt).TotalMilliseconds > 5)
            {
                _buffer.Clear();
            }

            // Enter / Tab terminate the scan. Anything shorter than 4
            // characters is treated as cashier typing into the search
            // box (HIDs sometimes emit scanner codes of 4 — EAN-8 / UPC-E).
            if (hookStruct.VkCode == 0x0D || hookStruct.VkCode == 0x09)
            {
                if (_buffer.Length >= 4 && _buffer.Length <= 32)
                {
                    Emit(_buffer.ToString());
                }
                _buffer.Clear();
            }
            else
            {
                // Only printable ASCII. Non-printable keys (Shift, Ctrl,
                // function keys) are ignored — they don't appear in any
                // standard symbology output.
                var ch = VirtualKeyToChar(hookStruct.VkCode);
                if (ch is not null && _buffer.Length < 64)
                {
                    _buffer.Append(ch);
                    _lastCharAt = now;
                }
            }
        }

        return NativeMethods.CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    private void Emit(string code)
    {
        _receivedScans.Enqueue(code);
        BarcodeRead?.Invoke(this, code);
    }

    /// <summary>
    /// Translate a virtual-key code into its printable character.
    /// Returns <c>null</c> for non-printable keys so the scanner's
    /// heuristic ignores them.
    /// </summary>
    private static char? VirtualKeyToChar(uint vk)
    {
        // 0x30..0x39 = top-row digits 0..9.
        if (vk >= 0x30 && vk <= 0x39) return (char)('0' + (vk - 0x30));
        // 0x41..0x5A = letters A..Z (uppercase — EAN-13 / Code-128 are
        // uppercase by convention; shift state is not tracked).
        if (vk >= 0x41 && vk <= 0x5A) return (char)('A' + (vk - 0x41));
        return null;
    }
}
