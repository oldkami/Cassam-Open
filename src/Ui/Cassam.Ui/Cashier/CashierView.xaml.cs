using System;

namespace Cassam.Ui.Cashier;

/// <summary>
/// Code-behind for <see cref="CashierView"/>. Intentionally thin:
/// the View is a pure render of <see cref="Hardware.Common.Cashier.CashierViewModel"/>;
/// the only logic that lives here is the ViewModel resolution
/// through DI so the XAML <c>{x:Bind ViewModel.X}</c> expressions
/// resolve to the singleton registered in <c>App.xaml.cs</c>.
/// PR 10 (T2.09) added the "Modo manager" button click handler
/// that raises <see cref="OpenManagerRequested"/>.
/// </summary>
public sealed partial class CashierView : Page
{
    /// <summary>The cashier view-model, resolved through DI.</summary>
    public Hardware.Common.Cashier.CashierViewModel ViewModel { get; }

    /// <summary>
    /// Raised when the operator taps "Modo manager". The
    /// application root listener navigates the root frame to the
    /// manager shell.
    /// </summary>
    public event EventHandler? OpenManagerRequested;

    public CashierView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<Hardware.Common.Cashier.CashierViewModel>();
        this.DataContext = ViewModel;
    }

    private void OnOpenManagerClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => OpenManagerRequested?.Invoke(this, EventArgs.Empty);
}
