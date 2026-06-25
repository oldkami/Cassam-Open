using Uno.UI.Hosting;

namespace Cassam.Ui;

/// <summary>
/// WebAssembly (browser) bootstrap. WASM is the back-office surface only
/// (no printer, no cash drawer, no pole display — see REQ-UI-06). The host
/// runs in the browser; the Skia renderer produces a Canvas/SVG presentation.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        App.InitializeLogging();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseWebAssembly()
            .Build();

        await host.RunAsync();
    }
}
