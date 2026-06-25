using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Linux;

/// <summary>
/// Linux implementation of <see cref="IBarcodeScanner"/>.
///
/// <para>
/// Production input path: a <see cref="ILinuxInputHook"/> chosen at
/// runtime by <see cref="InputHookFactory"/> based on the active
/// session (Wayland + libei, X11 + libX11, or evdev). The hook
/// raises <see cref="ILinuxInputHook.BarcodeDecoded"/> when a HID
/// scanner emits a complete code (terminator Enter / Tab).
/// </para>
///
/// <para>
/// Why an abstract hook (R-UI-03, design §16):
/// <list type="bullet">
///   <item>Three different Linux surfaces (Wayland, X11, evdev
///         framebuffer) each need their own P/Invoke surface.
///         Switching between them at runtime without an abstraction
///         turns the scanner into a switch-on-platformType ladder.</item>
///   <item>Tests need to drive the scanner headlessly. The
///         abstraction's <see cref="FakeInputHook"/> swaps in on
///         the Windows dev host so the contract surface is
///         verified end-to-end without real hardware.</item>
/// </list>
/// </para>
///
/// <para>
/// Buffer-and-terminate heuristic: 4..32 chars + Enter (vk 28) /
/// Tab (vk 15). Identical to the Windows HID path so cross-platform
/// test expectations stay uniform.
/// </para>
///
/// <para>
/// PR 7 status vs PR 8 status:
/// <list type="bullet">
///   <item>PR 7: <see cref="StartAsync"/> threw
///         <see cref="PlatformNotSupportedException"/> on every host
///         because the real hook had not landed. The DI container
///         bound this implementation on Linux hosts but the cashier
///         flow could never start.</item>
///   <item>PR 8 (this file): the abstraction + fake hook close
///         R-UI-03 — Linux hosts can now construct and start the
///         scanner (it routes through the chosen hook). Real evdev /
///         X11 / Wayland bindings land in PR 10 with station QA.</item>
/// </list>
/// </para>
/// </summary>
public sealed class LinuxBarcodeScanner : IBarcodeScanner
{
    /// <inheritdoc />
    public event EventHandler<string>? BarcodeRead;

    private readonly ILinuxInputHook _hook;
    private bool _running;
    private readonly ConcurrentQueue<string> _scanned = new();

    /// <summary>Codes the scanner has emitted so far (test helper).</summary>
    public IReadOnlyCollection<string> ReceivedScans => _scanned;

    /// <summary>
    /// Default constructor — picks the right input hook via
    /// <see cref="InputHookFactory.Create"/>. Production code path.
    /// </summary>
    public LinuxBarcodeScanner() : this(InputHookFactory.Create())
    {
    }

    /// <summary>
    /// Test constructor — inject an explicit hook (the
    /// <see cref="FakeInputHook"/> in unit tests). The dev host is
    /// Windows so the factory's platform probe always returns the
    /// stub; tests bypass the factory and wire the fake directly.
    /// </summary>
    public LinuxBarcodeScanner(ILinuxInputHook hook)
    {
        _hook = hook ?? throw new ArgumentNullException(nameof(hook));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _running = true;

        _hook.BarcodeDecoded += OnHookBarcodeDecoded;
        return _hook.StartAsync(ct);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct)
    {
        if (!_running) return Task.CompletedTask;
        _running = false;

        _hook.BarcodeDecoded -= OnHookBarcodeDecoded;
        return _hook.StopAsync(ct);
    }

    /// <summary>
    /// Test helper — push a code through the hook. Mirrors the
    /// Windows + macOS scanner's <c>SimulateScan</c> surface.
    /// No-op when the scanner has not been started.
    /// </summary>
    public void SimulateScan(string code)
    {
        if (!_running) return;
        _scanned.Enqueue(code);
        BarcodeRead?.Invoke(this, code);
    }

    private void OnHookBarcodeDecoded(object? sender, string code)
    {
        // The hook raises on its background thread; the scanner's
        // event invocation is safe from any thread (the framework's
        // EventHandler snapshot handles the dispatch).
        _scanned.Enqueue(code);
        BarcodeRead?.Invoke(this, code);
    }
}
