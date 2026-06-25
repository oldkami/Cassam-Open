using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace Cassam.Ui.Droid;

/// <summary>
/// Android <see cref="Activity"/> hosting the Uno XAML content. The
/// <c>MainLauncher = true</c> attribute registers it as the launcher icon;
/// <c>ConfigurationChanges</c> is set to all-config so XAML handles rotation
/// internally without an Activity recreate.
/// </summary>
[Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden
)]
public class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        base.OnCreate(savedInstanceState);
    }
}
