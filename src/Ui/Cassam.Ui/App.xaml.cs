using System;
using System.Collections.Generic;
using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Common.Mock;
using Cassam.Ui.Hardware.Common.Stubs;
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
                // ---- Hardware abstraction layer (PR 6 — Mocks only) ----
                // Per-platform implementations land in PR 8 (T2.05..T2.07).
                // Phase 2 ships Mock implementations for testing without
                // physical hardware (SCN-UI-04..06 mock paths).
                services.AddSingleton<IBarcodeScanner, MockBarcodeScanner>();
                services.AddSingleton<IReceiptPrinter, MockReceiptPrinter>();
                services.AddSingleton<ICashDrawer, MockCashDrawer>();
                services.AddSingleton<ICustomerPoleDisplay, MockCustomerPoleDisplay>();

                // ---- DIAN / Sync stable seams (PR 6 — Stubs only) ----
                // Stub providers return safe defaults so the UI is
                // renderable end-to-end. Phase 4a (sync) and 4b (DIAN)
                // swap in real implementations behind the same interface
                // with no UI diff (R-UI-06 mitigation, design §7).
                services.AddSingleton<IDianStatusProvider, StubDianStatusProvider>();
                services.AddSingleton<ISyncStateProvider, StubSyncStateProvider>();

                // ---- Application root + entry VM ----
                services.AddSingleton<ViewModels.MainViewModel>();
            })
            .Build();
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
            // Single-page bootstrap. The cashier + manager flows land in
            // PR 7 (T2.02) and PR 9 (T2.03 / T2.09); this PR ships only
            // the shell + the byte-identical MainPage required by SCN-UI-01.
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
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
