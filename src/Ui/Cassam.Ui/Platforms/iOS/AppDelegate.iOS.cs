using Foundation;
using UIKit;

namespace Cassam.Ui;

/// <summary>
/// iOS / Mac Catalyst entry point. Uno uses this single file to bridge
/// the iOS UIApplicationDelegate lifecycle into the XAML host. The macOS
/// head is Mac Catalyst, which compiles the same iOS host to run on macOS.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : Microsoft.UI.Xaml.XamlApplicationDelegate
{
    static AppDelegate()
    {
        App.InitializeLogging();
    }

    public AppDelegate()
        : base(() => new App())
    {
    }
}
