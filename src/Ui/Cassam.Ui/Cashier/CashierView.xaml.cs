namespace Cassam.Ui.Cashier;

/// <summary>
/// Code-behind for <see cref="CashierView"/>. Intentionally thin:
/// the View is a pure render of <see cref="Hardware.Common.Cashier.CashierViewModel"/>;
/// the only logic that lives here is the ViewModel resolution
/// through DI so the XAML <c>{x:Bind ViewModel.X}</c> expressions
/// resolve to the singleton registered in <c>App.xaml.cs</c>.
/// </summary>
public sealed partial class CashierView : Page
{
    /// <summary>The cashier view-model, resolved through DI.</summary>
    public Hardware.Common.Cashier.CashierViewModel ViewModel { get; }

    public CashierView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<Hardware.Common.Cashier.CashierViewModel>();
        this.DataContext = ViewModel;
    }
}
