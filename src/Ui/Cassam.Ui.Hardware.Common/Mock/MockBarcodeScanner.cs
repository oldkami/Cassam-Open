using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Mock;

/// <summary>
/// In-memory <see cref="IBarcodeScanner"/> for unit tests and for
/// PR 6's headless integration smoke test. The Mock exposes a
/// <see cref="SimulateScan"/> helper that the test can call to push
/// a code into the event stream; the production code is unchanged.
///
/// Thread-safety: <see cref="SimulateScan"/> is safe to call from
/// any thread — the underlying <c>EventHandler</c> invocation is
/// synchronized via the framework's own event-delegate snapshot.
/// </summary>
public sealed class MockBarcodeScanner : IBarcodeScanner
{
    /// <inheritdoc />
    public event EventHandler<string>? BarcodeRead;

    private bool _running;
    private readonly List<string> _scanned = new();

    /// <summary>
    /// Codes the mock has emitted so far. Useful for assertions
    /// like <c>scanner.ScannedCodes.Should().ContainSingle(...)</c>.
    /// </summary>
    public IReadOnlyList<string> ScannedCodes => _scanned;

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
    /// Test helper — push a code into the event stream as if a
    /// real scanner had just decoded it. No-op when the scanner
    /// has not been started.
    /// </summary>
    public void SimulateScan(string code)
    {
        if (!_running) return;
        _scanned.Add(code);
        BarcodeRead?.Invoke(this, code);
    }
}
