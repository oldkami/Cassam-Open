using Uno.UI.Hosting;

namespace Cassam.Ui;

/// <summary>
/// Desktop bootstrap for Windows / macOS / Linux (X11) / Linux (Framebuffer).
/// A single TFM (<c>net10.0-desktop</c>) produces three runnable artifacts;
/// the matching <c>UseXxx()</c> call below selects the rendering host at runtime.
/// See <see href="https://aka.platform.uno/singleproject-features"/>.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
