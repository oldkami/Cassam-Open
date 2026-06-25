using System;
using System.Collections.Generic;
using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Common.Cashier;
using Cassam.Ui.Hardware.Common.Mock;
using Cassam.Ui.Hardware.Common.Stubs;
using Cassam.Ui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cassam.Ui;

/// <summary>
/// Application root. Owns the singleton <see cref="IHost"/> that the
/// rest of the app resolves services from (HAL interfaces, ViewModels,
/// status providers — see design §2.4 / §7). The host is built once in
/// the constructor and survives all navigation.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        this.InitializeComponent();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Excludes noisy framework logs; keeps app logs visible.
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddFilter("Uno", LogLevel.Warning);
                logging.AddFilter("Windows", LogLevel.Warning);
                logging.AddFilter("Microsoft", LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                // ---- DIAN / Sync stable seams (PR 6 — Stubs only) ----
                // Stub providers return safe defaults so the UI is
                // renderable end-to-end. Phase 4a (sync) and 4b (DIAN)
                // swap in real implementations behind the same interface
                // with no UI diff (R-UI-06 mitigation, design §7).
                services.AddSingleton<IDianStatusProvider, StubDianStatusProvider>();
                services.AddSingleton<ISyncStateProvider, StubSyncStateProvider>();

                // ---- Application root + entry VMs ----
                services.AddSingleton<ViewModels.MainViewModel>();

                // ---- Cashier flow (PR 8, T2.08) ----
                // The cashier VM is the first end-user-facing screen.
                // Singleton because the scanner + printer + pole +
                // drawer HALs are singletons (PR 7 rationale) and
                // the VM owns the scanner-event subscription. A
                // transient VM would re-subscribe on every page
                // navigation, leaking events.
                services.AddSingleton<CashierViewModel>();
                services.AddSingleton<CashierView>();
                services.AddSingleton<IProductCatalog>(_ =>
                    new InMemoryProductCatalog(new Dictionary<string, ProductSearchResult>(StringComparer.OrdinalIgnoreCase)
                    {
                        // Minimal fixture so the cashier flow renders
                        // a non-empty cart on first launch. The
                        // production catalog (PR 9 / T2.09) replaces
                        // this with the EF-backed IProductCatalogService.
                        ["7701234567890"] = new ProductSearchResult(
                            Guid.Parse("00000000-0000-0000-0000-000000000001"),
                            "001-7701234",
                            "Arroz Diana 1kg",
                            3500m,
                            Core.Domain.Enums.TaxCategory.Standard),
                        ["7709876543210"] = new ProductSearchResult(
                            Guid.Parse("00000000-0000-0000-0000-000000000002"),
                            "002-7709876",
                            "Leche Alpina 1L",
                            4500m,
                            Core.Domain.Enums.TaxCategory.Exempt),
                    }));

                // ---- Per-platform HAL (PR 7 — design §6.2) ----
                // The branch selects the matching AddXxxHardware
                // extension at compile time. The fallback (no WINDOWS
                // / LINUX / ANDROID symbol) is the Mock surface so
                // the headless unit-test harness + the WASM head (PR 8)
                // still resolve every HAL interface.
                RegisterPerPlatformHardware(services);
            })
            .Build();
    }

    /// <summary>
    /// Register the per-platform HAL implementation that matches the
    /// current TFM. The Mock surface is registered LAST so it acts as
    /// the fallback for any platform that does not yet have a real
    /// implementation (WASM in PR 8, iOS, macOS before PR 8).
    /// </summary>
    private static void RegisterPerPlatformHardware(IServiceCollection services)
    {
#if WINDOWS
        // T2.03 — Windows HID + ESC/POS over winspool / SerialPort / TcpClient.
        global::Cassam.Ui.Hardware.Windows.WindowsHardwareModule.AddWindowsHardware(services);
#elif ANDROID
        // T2.05 — Android BT HID/SPP scanner + BT ESC/POS printer + BT cash drawer.
        global::Cassam.Ui.Hardware.Android.AndroidHardwareModule.AddAndroidHardware(services);
#elif LINUX
        // T2.04 — Linux Serial + LAN ESC/POS + serial pole display.
        // Barcode scanner is a placeholder until evdev / X11 / Wayland
        // hook lands in a follow-up PR (design §16 R-UI-03).
        global::Cassam.Ui.Hardware.Linux.LinuxHardwareModule.AddLinuxHardware(services);
#else
        // WASM (PR 8 / T2.07) + iOS (out of scope) + macOS (PR 8 / T2.06).
        // Camera-only barcode + no printer / drawer / pole (REQ-UI-06).
        services.AddSingleton<IBarcodeScanner, MockBarcodeScanner>();
        services.AddSingleton<IReceiptPrinter, MockReceiptPrinter>();
        services.AddSingleton<ICashDrawer, MockCashDrawer>();
        services.AddSingleton<ICustomerPoleDisplay, MockCustomerPoleDisplay>();
#endif
    }

    /// <summary>
    /// The DI container. Headless tests and view-model tests resolve
    /// services from this <see cref="IHost"/> without needing a live
    /// <see cref="Window"/>.
    /// </summary>
    public IHost Host => _host;

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window();
#if DEBUG
        MainWindow.UseStudio();
#endif

        // Place a frame on the window; navigate to the shared MainPage.
        // The frame is created here (rather than in XAML) so we can attach
        // the navigation-failed handler.
        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            MainWindow.Content = rootFrame;
        }

        if (rootFrame.Content == null)
        {
            // PR 8 (T2.08): the first end-user-facing screen is now
            // the cashier flow. The MainPage bootstrap survives as
            // the shell fallback but the navigation entry point
            // becomes CashierView. The frame transition is
            // post-construction so the DI container's async
            // InitialiseAsync (which wires the scanner event)
            // completes before the page renders.
            rootFrame.Navigate(typeof(Cassier.CashierView), args.Arguments);
        }

        MainWindow.Activate();
    }

    private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        => throw new InvalidOperationException(
            $"Failed to load {e.SourcePageType.FullName}: {e.Exception}");

    /// <summary>
    /// Configures global Uno Platform logging. Mirrors the template's
    /// recommended setup; intentionally a no-op for release builds
    /// to keep startup time low.
    /// </summary>
    public static void InitializeLogging()
    {
#if DEBUG
        var factory = LoggerFactory.Create(builder =>
        {
#if __WASM__
            builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#else
            builder.AddConsole();
#endif
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
