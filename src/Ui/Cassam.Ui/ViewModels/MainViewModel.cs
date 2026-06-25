using CommunityToolkit.Mvvm.ComponentModel;

namespace Cassam.Ui.ViewModels;

/// <summary>
/// Application-root view model. Bound to <see cref="MainPage"/> in
/// PR 7 (T2.02) when the cashier flow is built. The bootstrap VM
/// surfaces a single status line so the page is non-empty in
/// <c>SameMainPageAcrossHeadsTests</c> and the headless smoke
/// integration tests can assert a known control is present
/// (SCN-UI-01).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _status = "Cassam POS — ready";
}
