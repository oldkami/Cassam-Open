using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Linux;

/// <summary>
/// Linux implementation of <see cref="IBarcodeScanner"/>.
///
/// <para>
/// Status: abstraction-only placeholder. The full evdev / X11 / Wayland
/// integration lands in a follow-up PR once a Linux retail station is
/// available for QA. Per design §16 R-UI-03 the dev host runs Windows,
/// so no Linux retail station is currently online for verification.
/// </para>
///
/// <para>
/// Why ship a placeholder instead of a no-op:
/// <list type="number">
///   <item>The DI container MUST bind an implementation for every
///         interface or the cashier flow breaks at runtime with a
///         missing-dependency exception.</item>
///   <item>Throwing <see cref="PlatformNotSupportedException"/> at
///         <see cref="StartAsync"/> is the cleanest contract — Linux
///         builds will fail loudly on dev hosts instead of silently
///         dropping scans.</item>
///   <item>The same exception is raised on Windows + macOS so
///         integration tests can detect misrouted DI without
///         platform gymnastics.</item>
/// </list>
/// </para>
///
/// <para>
/// Implementation roadmap (post-PR-7 follow-up):
/// <list type="bullet">
///   <item>X11: <c>XGrabKey</c> on the cashier workstation's root
///         window via P/Invoke against libX11 — no daemon needed.</item>
///   <item>Wayland: <c>libei</c> via P/Invoke (the protocol-level
///         "emulated input" channel added in wlroots 0.17).</item>
///   <item>Fallback: in-process focused <c>TextBox</c> listener
///         when neither X11 nor Wayland binding is available
///         (Wayland session without libei).</item>
/// </list>
/// </para>
/// </summary>
public sealed class LinuxBarcodeScanner : IBarcodeScanner
{
    /// <inheritdoc />
    public event EventHandler<string>? BarcodeRead;

    private bool _running;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "LinuxBarcodeScanner requires a Linux host with evdev / X11 / Wayland. " +
                "On Windows or macOS the per-platform HAL switches to the matching implementation.");
        }

        // Real evdev / libei / XGrabKey hook would install here.
        // For PR 7 we only ensure the contract compiles + DI binds.
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
    /// Test helper — push a code into the event stream as if the
    /// real Linux hook had decoded it. No-op when the scanner has
    /// not been started (mirrors the Windows mock surface).
    /// </summary>
    public void SimulateScan(string code)
    {
        if (!_running) return;
        BarcodeRead?.Invoke(this, code);
    }
}
