namespace Cassam.Ui.Hardware.Android;

/// <summary>
/// Detected Android OEM. Drives the per-OEM pairing helper
/// selection in <see cref="IOemPairingHelperFactory"/> because
/// each vendor ships a different BT UX:
///
/// <list type="bullet">
///   <item><b>Samsung One UI</b>: scans SPP / HID accept box is
///         always visible; pair prompt is the standard Material
///         bottom-sheet. No special handling.</item>
///   <item><b>Xiaomi MIUI</b>: <b>blocks</b> BT scans unless the
///         app has been granted the "Nearby devices" runtime
///         permission AND MIUI's "Autostart" permission. Without
///         both, <c>ScanForDevicesAsync</c> returns zero devices.
///         The cashier flow's first-scan wizard guides the operator
///         through MIUI's permission screens.</item>
///   <item><b>Motorola (near-stock Android)</b>: identical to AOSP
///         behaviour. No special handling.</item>
/// </list>
///
/// Detection: Android exposes <c>android.os.Build.MANUFACTURER</c>
/// at runtime. We map the manufacturer string to one of these
/// enum values and feed it into the helper factory.
/// </summary>
public enum AndroidOem
{
    /// <summary>Unrecognised manufacturer — fall back to AOSP behaviour.</summary>
    Unknown = 0,

    /// <summary>Samsung — One UI 5 / 6 / 7.</summary>
    Samsung = 1,

    /// <summary>Xiaomi / Redmi / POCO — MIUI / HyperOS.</summary>
    Xiaomi = 2,

    /// <summary>Motorola — near-stock Android.</summary>
    Motorola = 3,
}

/// <summary>
/// Per-OEM tweaks the cashier flow's first-scan wizard surfaces.
/// The interface is intentionally minimal — the actual permission
/// dialog / settings-screen navigation lives behind
/// <c>CrossBluetoothLE.Current.Adapter</c> + Android's
/// <c>Settings</c> intents, which the cashier flow's UI layer
/// (PR 9 / T2.09 manager wizard) wires up at navigation time.
/// </summary>
public interface IOemPairingHelper
{
    /// <summary>The OEM this helper targets.</summary>
    AndroidOem Oem { get; }

    /// <summary>
    /// True when the OEM's BT stack will return zero scan results
    /// without explicit permission grants (Xiaomi MIUI is the
    /// canonical case). The cashier flow surfaces a one-time
    /// "permita acceso en MIUI" banner before scanning.
    /// </summary>
    bool RequiresExplicitPermissionFlow { get; }

    /// <summary>
    /// Default scan timeout in milliseconds. Samsung's BT stack
    /// is fast (5 s is enough); Xiaomi + Motorola benefit from
    /// a longer window (15 s) because the permission grant
    /// happens during the scan.
    /// </summary>
    int RecommendedScanTimeoutMs { get; }

    /// <summary>
    /// Reconnect interval for sleeping BT devices. Samsung + Motorola
    /// sleep after 30 min idle; Xiaomi after 5 min. The cashier flow
    /// schedules a reconnect probe at this interval.
    /// </summary>
    TimeSpan ReconnectProbeInterval { get; }

    /// <summary>
    /// Human-readable hint shown in the cashier flow's first-scan
    /// wizard. Localized in Phase 4 (es-CO: "Asegúrese de que el
    /// escáner esté en modo emparejamiento").
    /// </summary>
    string PairingHint { get; }
}

/// <summary>
/// Samsung One UI 5/6/7. Standard Material BT prompt.
/// <c>RequiresExplicitPermissionFlow = false</c> because Samsung's
/// BT stack returns scan results even without the Autostart
/// permission.
/// </summary>
public sealed class SamsungPairingHelper : IOemPairingHelper
{
    /// <inheritdoc />
    public AndroidOem Oem => AndroidOem.Samsung;

    /// <inheritdoc />
    public bool RequiresExplicitPermissionFlow => false;

    /// <inheritdoc />
    public int RecommendedScanTimeoutMs => 5_000;

    /// <inheritdoc />
    public TimeSpan ReconnectProbeInterval => TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public string PairingHint =>
        "Samsung: standard Android BT pairing — no extra permission needed.";
}

/// <summary>
/// Xiaomi / Redmi / POCO with MIUI / HyperOS. The vendor's BT
/// stack blocks scans unless the app is granted the
/// "Nearby devices" runtime permission AND MIUI's "Autostart"
/// permission. Without both, the scan returns zero devices and
/// the operator is left confused.
/// </summary>
public sealed class XiaomiPairingHelper : IOemPairingHelper
{
    /// <inheritdoc />
    public AndroidOem Oem => AndroidOem.Xiaomi;

    /// <inheritdoc />
    public bool RequiresExplicitPermissionFlow => true;

    /// <inheritdoc />
    public int RecommendedScanTimeoutMs => 15_000;

    /// <inheritdoc />
    public TimeSpan ReconnectProbeInterval => TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public string PairingHint =>
        "Xiaomi/MIUI: grant Nearby devices + Autostart permissions before pairing.";
}

/// <summary>
/// Motorola (near-stock Android). Behaves like AOSP — no special
/// handling required.
/// </summary>
public sealed class MotorolaPairingHelper : IOemPairingHelper
{
    /// <inheritdoc />
    public AndroidOem Oem => AndroidOem.Motorola;

    /// <inheritdoc />
    public bool RequiresExplicitPermissionFlow => false;

    /// <inheritdoc />
    public int RecommendedScanTimeoutMs => 8_000;

    /// <inheritdoc />
    public TimeSpan ReconnectProbeInterval => TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public string PairingHint =>
        "Motorola: standard Android BT pairing — no extra permission needed.";
}

/// <summary>
/// AOSP fallback for unrecognised manufacturers. Same behaviour
/// as Motorola.
/// </summary>
public sealed class DefaultOemPairingHelper : IOemPairingHelper
{
    /// <inheritdoc />
    public AndroidOem Oem => AndroidOem.Unknown;

    /// <inheritdoc />
    public bool RequiresExplicitPermissionFlow => false;

    /// <inheritdoc />
    public int RecommendedScanTimeoutMs => 8_000;

    /// <inheritdoc />
    public TimeSpan ReconnectProbeInterval => TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public string PairingHint =>
        "Standard Android BT pairing.";
}

/// <summary>
/// Picks the right <see cref="IOemPairingHelper"/> based on the
/// running device's manufacturer. The factory is wired into the
/// Android HAL via DI; tests inject a specific helper to assert
/// the behaviour without touching real hardware.
/// </summary>
public static class IOemPairingHelperFactory
{
    /// <summary>
    /// Detect the running OEM. On non-Android hosts (the Windows
    /// dev host, CI runners) returns <see cref="AndroidOem.Unknown"/>
    /// and the factory returns the <see cref="DefaultOemPairingHelper"/>.
    /// </summary>
    public static AndroidOem DetectOem()
    {
        if (!System.OperatingSystem.IsAndroid())
        {
            return AndroidOem.Unknown;
        }

#if ANDROID
        // On a real Android build, read android.os.Build.MANUFACTURER.
        // The string is uppercased per Android convention. The fully-
        // qualified Android.OS type avoids clashing with our root
        // namespace Cassam.Ui.Hardware.Android.
        var manufacturer = global::Android.OS.Build.Manufacturer?.ToUpperInvariant() ?? string.Empty;
        if (manufacturer.Contains("SAMSUNG", System.StringComparison.OrdinalIgnoreCase)) return AndroidOem.Samsung;
        if (manufacturer.Contains("XIAOMI", System.StringComparison.OrdinalIgnoreCase) ||
            manufacturer.Contains("REDMI", System.StringComparison.OrdinalIgnoreCase) ||
            manufacturer.Contains("POCO", System.StringComparison.OrdinalIgnoreCase)) return AndroidOem.Xiaomi;
        if (manufacturer.Contains("MOTOROLA", System.StringComparison.OrdinalIgnoreCase)) return AndroidOem.Motorola;
#endif
        return AndroidOem.Unknown;
    }

    /// <summary>
    /// Pick the right helper for <paramref name="oem"/>. The
    /// <see cref="AndroidOem.Unknown"/> fallback covers the dev
    /// host + CI runners; the cashier flow never sees an Unknown
    /// in production.
    /// </summary>
    public static IOemPairingHelper GetHelper(AndroidOem oem) => oem switch
    {
        AndroidOem.Samsung => new SamsungPairingHelper(),
        AndroidOem.Xiaomi => new XiaomiPairingHelper(),
        AndroidOem.Motorola => new MotorolaPairingHelper(),
        _ => new DefaultOemPairingHelper(),
    };

    /// <summary>
    /// Auto-detect + return the matching helper. Convenience
    /// wrapper for the production code path.
    /// </summary>
    public static IOemPairingHelper Resolve() => GetHelper(DetectOem());
}
