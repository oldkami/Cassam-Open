using System;

namespace Cassam.Ui;

/// <summary>
/// Code-behind for <c>MainPage.xaml</c>. PR 10 (T2.09) added the
/// "Modo manager" button click handler which raises the
/// <see cref="OpenManagerRequested"/> event. The App.xaml.cs
/// root listener forwards the request to the manager shell via
/// <c>Frame.Navigate</c>.
/// </summary>
public sealed partial class MainPage : Page
{
    /// <summary>
    /// Raised when the operator taps "Modo manager". The
    /// application root listener navigates the root frame to the
    /// manager shell.
    /// </summary>
    public event EventHandler? OpenManagerRequested;

    public MainPage()
    {
        this.InitializeComponent();
    }

    private void OnOpenManagerClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => OpenManagerRequested?.Invoke(this, EventArgs.Empty);
}
