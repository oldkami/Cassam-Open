using Cassam.Ui.Hardware.Common.Manager;

namespace Cassam.Ui.Manager;

/// <summary>
/// Code-behind for <see cref="DianStatusPanelView"/>. Resolves
/// the view-model through DI and starts/stops the 5-second
/// polling timer on Loaded/Unloaded so the panel only polls
/// while visible.
/// </summary>
public sealed partial class DianStatusPanelView : Page
{
    /// <summary>The DIAN status panel view-model (singleton, DI-resolved).</summary>
    public DianStatusPanelViewModel ViewModel { get; }

    public DianStatusPanelView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<DianStatusPanelViewModel>();
        this.DataContext = ViewModel;

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
        ViewModel.StartPolling();
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.StopPolling();
    }
}