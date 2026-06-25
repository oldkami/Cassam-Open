namespace Cassam.Ui;

/// <summary>
/// Code-behind for <c>MainPage.xaml</c>. Intentionally empty: the
/// <c>MainPage.xaml</c> is a shared single-XAML bootstrap that renders
/// identically across all 5 native heads (SCN-UI-01). Logic that
/// varies by head is wrapped in ViewModels and resolved through DI,
/// not pushed into the page code-behind.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
    }
}
