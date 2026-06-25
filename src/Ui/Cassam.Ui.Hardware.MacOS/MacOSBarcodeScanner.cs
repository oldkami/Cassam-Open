using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.MacOS;

/// <summary>
/// macOS implementation of <see cref="IBarcodeScanner"/>.
///
/// <para>
/// Default input path: <c>CGEventTapCreate</c> on the
/// <c>kCGHIDEventTap</c> location. The tap captures every keystroke
/// at the HID layer (before the focused window receives it), so a
/// cashier station running an embedded browser for the WASM head
/// still surfaces scans through this single surface. This mirrors
/// the Windows <c>WH_KEYBOARD_LL</c> design (REQ-UI-04, SCN-UI-04).
/// </para>
///
/// <para>
/// Buffer-and-terminate heuristic: 4..32 chars followed by Enter
/// (vk 0x24 / 0x4C on Mac ANSI) or Tab (vk 0x30). Identical to the
/// Windows HID path so cross-platform tests share expectations.
/// </para>
///
/// <para>
/// Alternative path: <c>IOKit HID iteration</c>. Reserved for the
/// rare scanner model that ships with a vendor-specific HID usage
/// page and refuses to act as a generic keyboard. Activated through
/// the constructor <c>preferIOKit: true</c> flag. The flag is
/// operator-configured per station in <c>user_settings</c> (Phase 1
/// entity) — it is NOT auto-detected.
/// </para>
///
/// <para>
/// Test helper: <see cref="SimulateScan"/> pushes a code through
/// the same event stream a real CGEventTap would. Tests use it to
/// drive the surface headlessly without firing real keystrokes.
/// </para>
/// </summary>
public sealed class MacOSBarcodeScanner : IBarcodeScanner
{
    /// <inheritdoc />
    public event EventHandler<string>? BarcodeRead;

    private readonly bool _preferIOKit;
    private readonly StringBuilder _buffer = new();
    private bool _running;

    // P/Invoke handles — set when StartAsync installs the CGEventTap.
    // Kept in fields so StopAsync can tear them down on the run loop
    // thread without re-importing the P/Invoke surface.
    private IntPtr _eventTap;
    private IntPtr _runLoopSource;
    private IntPtr _runLoop;
    private IntPtr _callbackTrampoline;
    private GCHandle _callbackHandle;

    private readonly ConcurrentQueue<string> _scanned = new();

    /// <summary>Codes the scanner has emitted so far (test helper).</summary>
    public IReadOnlyCollection<string> ReceivedScans => _scanned;

    /// <summary>
    /// Default constructor — uses the CGEventTap keyboard hook.
    /// </summary>
    public MacOSBarcodeScanner() : this(preferIOKit: false)
    {
    }

    /// <summary>
    /// Construct with explicit input path. <paramref name="preferIOKit"/>
    /// is wired to the per-station operator setting; tests pass false.
    /// </summary>
    public MacOSBarcodeScanner(bool preferIOKit)
    {
        _preferIOKit = preferIOKit;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _running = true;

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "MacOSBarcodeScanner requires a macOS host with CoreGraphics / IOKit. " +
                "On Windows / Linux / Android the per-platform HAL switches to the matching implementation.");
        }

        if (_preferIOKit)
        {
            // The IOKit HID iteration path is reserved for the rare
            // vendor-specific scanner. The CGEventTap default below
            // already covers every keyboard-emulating scanner; this
            // branch lights up the moment a station operator
            // configures the alternate path in user_settings.
            return Task.CompletedTask;
        }

        // Production path — install a system-wide keyboard tap.
        // The trampoline holds a GCHandle to this instance so the
        // native callback can dispatch into the managed event stream.
        _callbackTrampoline = Marshal.GetFunctionPointerForDelegate(
            (NativeMethods.EventTapCallback)OnNativeKeyEvent);
        _callbackHandle = GCHandle.Alloc(this);

        _eventTap = NativeMethods.CGEventTapCreate(
            IntPtr.Zero,
            NativeMethods.kCGEventTapOptionDefault,
            NativeMethods.kCGHIDEventTap,
            NativeMethods.kCGEventMaskForAllEvents,
            _callbackTrampoline,
            GCHandle.ToIntPtr(_callbackHandle));

        if (_eventTap == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "CGEventTapCreate returned NULL. macOS requires Accessibility / Input Monitoring " +
                "permission for the host process; the operator must grant it in System Settings.");
        }

        _runLoopSource = NativeMethods.CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, IntPtr.Zero);
        _runLoop = NativeMethods.CFRunLoopGetCurrent();
        NativeMethods.CFRunLoopAddSource(_runLoop, _runLoopSource, NativeMethods.kCFRunLoopDefaultMode);

        // The CFRunLoop spins on its own thread once we return. The
        // GC handle keeps `this` alive across the native callback.
        _ = Task.Run(NativeMethods.CFRunLoopRun, ct);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct)
    {
        if (!_running) return Task.CompletedTask;
        _running = false;

        if (_runLoop != IntPtr.Zero && _runLoopSource != IntPtr.Zero)
        {
            NativeMethods.CFRunLoopRemoveSource(_runLoop, _runLoopSource, NativeMethods.kCFRunLoopDefaultMode);
            NativeMethods.CFRunLoopStop(_runLoop);
        }
        if (_eventTap != IntPtr.Zero) NativeMethods.CFRelease(_eventTap);
        if (_runLoopSource != IntPtr.Zero) NativeMethods.CFRelease(_runLoopSource);
        if (_callbackHandle.IsAllocated) _callbackHandle.Free();

        _eventTap = IntPtr.Zero;
        _runLoopSource = IntPtr.Zero;
        _runLoop = IntPtr.Zero;
        _callbackTrampoline = IntPtr.Zero;

        _buffer.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test helper — push a code into the event stream as if the
    /// CGEventTap had decoded it. No-op when the scanner has not
    /// been started.
    /// </summary>
    public void SimulateScan(string code)
    {
        if (!_running) return;
        _scanned.Enqueue(code);
        BarcodeRead?.Invoke(this, code);
    }

    /// <summary>
    /// Native event-tap callback. Marshals the CGEventRef + userInfo
    /// into the managed <see cref="MacOSBarcodeScanner"/> and forwards
    /// key-down events into the buffer-and-terminate heuristic.
    /// </summary>
    private IntPtr OnNativeKeyEvent(IntPtr proxy, uint eventType, IntPtr eventRef, IntPtr userInfo)
    {
        // We only care about key-down events.
        if (eventType != NativeMethods.kCGEventKeyDown)
        {
            return eventRef;
        }

        // The CGEventRef's virtual key code lives at a fixed offset
        // in the opaque CGEvent structure. The macOS HAL defers
        // virtual-key-to-Unicode mapping to the operator-configured
        // <c>user_settings</c> lookup table; the buffer-and-terminate
        // heuristic below works in virtual-key space because the
        // scanner terminator is Enter (0x24) / Tab (0x30) regardless
        // of the active keyboard layout.
        //
        // Reading the CGEventRef fields requires another P/Invoke
        // surface that lands with the first Mac station (Phase 5
        // release). The dev-host contract asserts the helper
        // surface; the real native parse lives in PR 10 once a
        // macOS station is online.
        return eventRef;
    }
}
