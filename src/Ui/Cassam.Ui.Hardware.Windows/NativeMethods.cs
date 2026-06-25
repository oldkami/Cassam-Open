using System.Runtime.InteropServices;

namespace Cassam.Ui.Hardware.Windows;

/// <summary>
/// P/Invoke surface for the Windows native APIs the HAL uses. Kept
/// in a single file so the Win32 dependency is auditable in one
/// place — every external method is wrapped in a thin C# method,
/// none of the call sites need <c>DllImport</c> noise.
///
/// <para>
/// What we call:
/// <list type="bullet">
///   <item><c>user32.dll!SetWindowsHookExW / CallNextHookEx /
///         UnhookWindowsHookEx</c> — global keyboard hook for HID
///         barcode scanner input. WH_KEYBOARD_LL does NOT require
///         a DLL injection (unlike WH_KEYBOARD), so the hook lives
///         entirely inside this managed process (REQ-UI-04).</item>
///   <item><c>user32.dll!GetAsyncKeyState</c> — key-down edge
///         detection for fast scanner bursts.</item>
///   <item><c>winspool.drv!OpenPrinterW / WritePrinter /
///         ClosePrinter</c> — raw byte stream to a USB-attached
///         vendor-class ESC/POS printer (the "USB printer" column
///         in design §6.2). Skipped when the device is on COMx or
///         LAN.</item>
/// </list>
/// </para>
/// </summary>
internal static class NativeMethods
{
    private const string User32 = "user32.dll";
    private const string Winspool = "winspool.drv";

    // ---- Hook handles -------------------------------------------------

    internal const int WH_KEYBOARD_LL = 13;

    internal delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct KBDLLHOOKSTRUCT
    {
        public readonly uint VkCode;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nint DwExtraInfo;
    }

    [DllImport(User32, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport(User32, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    /// <summary>
    /// Install a low-level keyboard hook. WH_KEYBOARD_LL fires on the
    /// thread that installed it (no DLL injection) — exactly what we
    /// want for a managed POS app where a managed UI thread is always
    /// alive.
    /// </summary>
    [DllImport(User32, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint SetWindowsHookExW(
        int idHook,
        LowLevelKeyboardProc lpfn,
        nint hMod,
        uint dwThreadId);

    // ---- Async key state (edge detection) -----------------------------

    [DllImport(User32, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern short GetAsyncKeyState(int vKey);

    // ---- Raw printer (vendor-class USB) -------------------------------

    [DllImport(Winspool, SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenPrinterW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenPrinterW(string pPrinterName, out nint phPrinter, nint pDefault);

    [DllImport(Winspool, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClosePrinter(nint hPrinter);

    [DllImport(Winspool, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WritePrinter(
        nint hPrinter,
        nint pBuf,
        uint cbBuf,
        out uint pcWritten);

    [DllImport(Winspool, SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "StartDocPrinterW")]
    internal static extern int StartDocPrinterW(nint hPrinter, int level, nint pDocInfo);

    [DllImport(Winspool, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndPagePrinter(nint hPrinter);

    [DllImport(Winspool, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndDocPrinter(nint hPrinter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DOCINFOW
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? DocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DataType;
    }

    // ---- Module handle (required by SetWindowsHookEx) -----------------

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandleW(string? lpModuleName);
}
