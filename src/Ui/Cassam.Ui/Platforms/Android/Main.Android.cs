using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace Cassam.Ui.Droid;

/// <summary>
/// Android <see cref="Android.App.Application"/> entry point. The Uno head
/// uses <see cref="Microsoft.UI.Xaml.NativeApplication"/> to bridge the
/// Android lifecycle into the XAML host. This is the only file in the
/// Android head that is platform-specific; the rest of the codebase is
/// shared XAML under <c>net10.0-android</c>.
/// </summary>
[Application(
    Label = "@string/ApplicationName",
    Icon = "@mipmap/icon",
    LargeHeap = true,
    HardwareAccelerated = true,
    Theme = "@style/Theme.App.Starting"
)]
public class Application : Microsoft.UI.Xaml.NativeApplication
{
    static Application()
    {
        App.InitializeLogging();
    }

    public Application(IntPtr javaReference, JniHandleOwnership transfer)
        : base(() => new App(), javaReference, transfer)
    {
    }
}
