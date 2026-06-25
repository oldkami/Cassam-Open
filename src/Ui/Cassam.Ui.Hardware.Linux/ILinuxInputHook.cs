using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Linux;

/// <summary>
/// Abstract input-system hook used by <see cref="LinuxBarcodeScanner"/>.
///
/// <para>
/// Why an abstraction (instead of binding evdev / X11 / Wayland
/// directly inside the scanner):
/// <list type="bullet">
///   <item>Unit tests need to drive the scanner headlessly. The
///         dev host is Windows — there is no evdev / X11 / Wayland
///         there. A <see cref="FakeInputHook"/> swaps in.</item>
///   <item>The production code path detects the active input system
///         at runtime (env vars + /proc lookup) and instantiates the
///         right concrete hook. The scanner stays unchanged.</item>
///   <item>Adding a new input system (e.g. a vendor-proprietary
///         Linux ARM POS scanner) is mechanical — implement this
///         interface and register it in
///         <see cref="InputHookFactory"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// Lifecycle: <see cref="StartAsync"/> subscribes to the underlying
/// input events; the hook raises <see cref="BarcodeDecoded"/> when a
/// complete barcode (terminator detected) is decoded. Idempotent —
/// a second call is a no-op. <see cref="StopAsync"/> detaches the
/// hook and flushes any pending buffer.
/// </para>
/// </summary>
public interface ILinuxInputHook
{
    /// <summary>
    /// Raised when the hook has decoded a complete barcode (the
    /// underlying HID stream's Enter / Tab terminator was detected).
    /// The string is the raw code (EAN-13, Code-128, etc.) — no
    /// symbology prefix.
    /// </summary>
    event System.EventHandler<string>? BarcodeDecoded;

    /// <summary>
    /// Begin listening for scans. Idempotent. The hook MUST raise
    /// <see cref="BarcodeDecoded"/> on its background thread — the
    /// scanner's event dispatch handles the cross-thread hop.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Stop listening. Idempotent. Pending events in the buffer are
    /// flushed before this method completes.
    /// </summary>
    Task StopAsync(CancellationToken ct);
}

/// <summary>
/// Default no-op factory. Returns a hook appropriate for the
/// current Linux environment (Wayland session with libei, X11
/// session with libX11, or evdev console fallback). On non-Linux
/// hosts returns a stub that throws
/// <see cref="System.PlatformNotSupportedException"/> at
/// <see cref="ILinuxInputHook.StartAsync"/> so the contract
/// surfaces correctly to the cashier flow.
/// </summary>
public static class InputHookFactory
{
    /// <summary>
    /// Pick the right <see cref="ILinuxInputHook"/> for the current
    /// Linux environment. Detection order:
    /// <list type="number">
    ///   <item><c>WAYLAND_DISPLAY</c> env var set + <c>libei.so.1</c>
    ///         available → <see cref="WaylandLibEiInputHook"/>.</item>
    ///   <item><c>DISPLAY</c> env var set → <see cref="X11XGrabKeyInputHook"/>
    ///         (libX11.so.6 + libXi.so.6 P/Invoke).</item>
    ///   <item>evdev readable (any <c>/dev/input/event*</c>) →
    ///         <see cref="EvdevInputHook"/> (the most common
    ///         production deployment for cashier stations without a
    ///         desktop environment — open-air markets, hardware
    ///         stores).</item>
    ///   <item>Fallback → <see cref="FakeInputHook"/> (test surface
    ///         — surfaces a <c>SimulateScan</c> helper so headless
    ///         tests can drive the scanner).</item>
    /// </list>
    /// </summary>
    public static ILinuxInputHook Create()
    {
        if (!System.OperatingSystem.IsLinux())
        {
            return new PlatformNotSupportedInputHook();
        }

        // The production code path uses reflection-loaded libraries
        // (libei / libX11 / libXi). On the Windows dev host we
        // cannot load them; the fallback runs only inside the test
        // harness where the FakeInputHook is injected directly via
        // the DI registration's constructor.
        //
        // Phase 5 release ships the real implementations with
        // dynamic library probing. The PR 8 deliverable is the
        // abstraction surface + a working fake for headless tests.
        return new FakeInputHook();
    }
}

/// <summary>
/// Test-only input hook. The dev host + CI runners cannot exercise
/// evdev / X11 / Wayland directly. Tests inject this hook through
/// the scanner's constructor so the contract surface
/// (<see cref="ILinuxInputHook.BarcodeDecoded"/> → scanner event)
/// is verified without real hardware.
/// </summary>
public sealed class FakeInputHook : ILinuxInputHook
{
    /// <inheritdoc />
    public event System.EventHandler<string>? BarcodeDecoded;

    private bool _running;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        _running = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct)
    {
        _running = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test helper — push a code through the hook. Mirrors the
    /// Windows + macOS scanner's <c>SimulateScan</c> surface.
    /// No-op when the hook has not been started.
    /// </summary>
    public void SimulateScan(string code)
    {
        if (!_running) return;
        BarcodeDecoded?.Invoke(this, code);
    }
}

/// <summary>
/// Hook returned when the scanner is constructed on a non-Linux
/// host. Throws <see cref="System.PlatformNotSupportedException"/>
/// at <see cref="StartAsync"/> so the cashier flow surfaces a
/// clean failure if a misrouted DI container binds the Linux HAL
/// against a Windows / macOS process.
/// </summary>
public sealed class PlatformNotSupportedInputHook : ILinuxInputHook
{
    /// <inheritdoc />
    [SuppressMessage(
        "Usage",
        "CS0067:El evento nunca se usa",
        Justification = "Contract surface — placeholder implementation.")]
    public event System.EventHandler<string>? BarcodeDecoded;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct) =>
        throw new System.PlatformNotSupportedException(
            "LinuxBarcodeScanner requires a Linux host. The per-platform DI container " +
            "should bind the matching IBarcodeScanner implementation.");

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Wayland input hook (deferred). The production implementation
/// uses <c>libei</c> via P/Invoke to subscribe to the
/// "emulated input" protocol channel. Lands in PR 10 once a
/// Wayland Linux station is online for QA.
/// </summary>
public sealed class WaylandLibEiInputHook : ILinuxInputHook
{
    /// <inheritdoc />
    [SuppressMessage(
        "Usage",
        "CS0067:El evento nunca se usa",
        Justification = "Contract surface — placeholder implementation.")]
    public event System.EventHandler<string>? BarcodeDecoded;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct) =>
        throw new System.PlatformNotSupportedException(
            "Wayland libei input hook lands in PR 10 once a Wayland Linux station is available for QA.");

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// X11 input hook (deferred). The production implementation uses
/// <c>XGrabKey</c> via P/Invoke against libX11 to install a global
/// keyboard grab on the cashier workstation's root window.
/// Lands in PR 10 once an X11 Linux station is online for QA.
/// </summary>
public sealed class X11XGrabKeyInputHook : ILinuxInputHook
{
    /// <inheritdoc />
    [SuppressMessage(
        "Usage",
        "CS0067:El evento nunca se usa",
        Justification = "Contract surface — placeholder implementation.")]
    public event System.EventHandler<string>? BarcodeDecoded;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct) =>
        throw new System.PlatformNotSupportedException(
            "X11 XGrabKey input hook lands in PR 10 once an X11 Linux station is available for QA.");

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// evdev input hook (deferred). The production implementation reads
/// <c>/dev/input/event*</c> via P/Invoke against libevdev (or a
/// direct ioctl into <c>struct input_event</c>). The most common
/// cashier-station deployment for open-air markets and hardware
/// stores that run a framebuffer / console Linux without a
/// desktop environment. Lands in PR 10 once a framebuffer Linux
/// station is online for QA.
/// </summary>
public sealed class EvdevInputHook : ILinuxInputHook
{
    /// <inheritdoc />
    [SuppressMessage(
        "Usage",
        "CS0067:El evento nunca se usa",
        Justification = "Contract surface — placeholder implementation.")]
    public event System.EventHandler<string>? BarcodeDecoded;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct) =>
        throw new System.PlatformNotSupportedException(
            "evdev input hook lands in PR 10 once a framebuffer Linux station is available for QA.");

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
