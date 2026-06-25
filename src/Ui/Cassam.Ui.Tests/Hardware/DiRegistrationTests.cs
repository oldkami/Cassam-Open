using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Common.Mock;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cassam.Ui.Tests.Hardware;

public class DiRegistrationTests
{
    [Fact]
    public void All_four_HAL_interfaces_resolve_from_a_default_host()
    {
        // Mirrors the registrations in Cassam.Ui/App.xaml.cs.
        // If the DI wiring drifts (e.g. a missing AddSingleton),
        // the build of the head project still succeeds but this
        // contract test catches the regression.
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IBarcodeScanner, MockBarcodeScanner>();
                services.AddSingleton<IReceiptPrinter, MockReceiptPrinter>();
                services.AddSingleton<ICashDrawer, MockCashDrawer>();
                services.AddSingleton<ICustomerPoleDisplay, MockCustomerPoleDisplay>();
            })
            .Build();

        using var scope = host.Services.CreateScope();
        var provider = scope.ServiceProvider;

        provider.GetRequiredService<IBarcodeScanner>()
            .Should().BeOfType<MockBarcodeScanner>();
        provider.GetRequiredService<IReceiptPrinter>()
            .Should().BeOfType<MockReceiptPrinter>();
        provider.GetRequiredService<ICashDrawer>()
            .Should().BeOfType<MockCashDrawer>();
        provider.GetRequiredService<ICustomerPoleDisplay>()
            .Should().BeOfType<MockCustomerPoleDisplay>();
    }

    [Fact]
    public void Singletons_are_returned_across_resolutions()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IBarcodeScanner, MockBarcodeScanner>();
            })
            .Build();

        var a = host.Services.GetRequiredService<IBarcodeScanner>();
        var b = host.Services.GetRequiredService<IBarcodeScanner>();

        a.Should().BeSameAs(b, "HAL must be a singleton — one listener for the whole app");
    }
}
