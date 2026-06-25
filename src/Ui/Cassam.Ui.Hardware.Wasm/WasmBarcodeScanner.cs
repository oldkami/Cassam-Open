#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif
using System;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Wasm;

/// <summary>
/// Web (WASM) implementation of <see cref="IBarcodeScanner"/>.
///
/// <para>
/// Default decoder: the browser's <c>BarcodeDetector</c> Web API.
/// Chromium browsers (Edge, Chrome, Brave, Opera) ship it natively;
/// no JS bundle is needed for the decode path. Fallback: ZXing-wasm
/// (~120 KB gzipped) for Firefox / Safari / any browser without
/// <c>BarcodeDetector</c>.
/// </para>
///
/// <para>
/// Stream lifecycle:
/// <list type="number">
///   <item><c>StartAsync</c> requests <c>navigator.mediaDevices.getUserMedia</c>
///         with the rear-facing camera constraint. The browser
///         surfaces the permission prompt on first use.</item>
///   <item>The decoded barcode string arrives via the JS event
///         bridge (the WASM head wires <c>onBarcodeDetected(code)</c>
///         to this instance's event).</item>
///   <item><c>StopAsync</c> releases the media stream tracks and
///         the JS-side decoder loop.</item>
/// </list>
/// </para>
///
/// <para>
/// Permission failure handling (R-UI-10): when the operator denies
/// camera permission or the browser revokes it mid-session, the UI
/// shows "Cámara no disponible — ingrese el código manualmente" and
/// falls back to the manual entry search box (Phase 4a follow-up).
/// </para>
///
/// <para>
/// Test helper: <see cref="SimulateScan"/> pushes a code through
/// the same event stream the JS bridge would. The Playwright +
/// axe-core WASM test (T2.07 follow-up) uses
/// <c>page.evaluate(...)</c> to invoke the JS bridge directly so
/// the scan path is exercised without a real camera.
/// </para>
/// </summary>
public sealed class WasmBarcodeScanner : IBarcodeScanner
{
    /// <inheritdoc />
    public event EventHandler<string>? BarcodeRead;

    private bool _running;

    /// <summary>Codes the scanner has emitted so far (test helper).</summary>
    public int ScannedCount { get; private set; }

#if __WASM__
    /// <summary>
    /// JS bridge: ask the browser for camera permission + start the
    /// native <c>BarcodeDetector</c> decode loop. Returns the JS
    /// handle of the active media stream so <see cref="StopAsync"/>
    /// can release it. Returns <c>null</c> when the user denies
    /// permission or the browser does not implement the API.
    /// </summary>
    [JSImport("cassam.scanner.start")]
    private static partial Task<string?> StartScannerAsync();

    /// <summary>
    /// JS bridge: stop the active scanner and release the camera
    /// stream. No-op when the scanner is not running.
    /// </summary>
    [JSImport("cassam.scanner.stop")]
    private static partial Task StopScannerAsync();

    /// <summary>
    /// JS bridge: probe whether <c>BarcodeDetector</c> is available
    /// in the current browser. Returns <c>true</c> for Chromium,
    /// <c>false</c> for Firefox / Safari (which fall back to ZXing).
    /// </summary>
    [JSImport("cassam.scanner.hasBarcodeDetector")]
    private static partial bool HasBarcodeDetector();
#else
    // Non-WASM targets (headless tests, Linux dev hosts): the JS
    // bridge is unreachable. Tests use SimulateScan() to push codes
    // through the same event stream.
    private static Task<string?> StartScannerAsync() => Task.FromResult<string?>(null);
    private static Task StopScannerAsync() => Task.CompletedTask;
    private static bool HasBarcodeDetector() => false;
#endif

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _running = true;

        // Production path on a WASM build — ask the browser for
        // camera permission + start the decoder. Failure (denied /
        // revoked / unsupported browser) is surfaced to the UI
        // through the scanner's silence — the cashier sees a
        // "Cámara no disponible" banner and falls back to manual
        // entry. The detailed UX lands in PR 9 / T2.07 follow-up.
        _ = StartScannerAsync();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct)
    {
        if (!_running) return Task.CompletedTask;
        _running = false;

        _ = StopScannerAsync();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test helper + JS bridge entry point. Pushes a code through
    /// the same event stream a real <c>BarcodeDetector</c> decode
    /// would. No-op when the scanner has not been started.
    /// </summary>
    /// <remarks>
    /// On a WASM build the Uno platform's JS bridge calls this
    /// method directly from the <c>onBarcodeDetected(code)</c>
    /// JS callback. On headless test runs the test code calls it
    /// directly to drive the surface.
    /// </remarks>
    public void SimulateScan(string code)
    {
        if (!_running) return;
        ScannedCount++;
        BarcodeRead?.Invoke(this, code);
    }
}
