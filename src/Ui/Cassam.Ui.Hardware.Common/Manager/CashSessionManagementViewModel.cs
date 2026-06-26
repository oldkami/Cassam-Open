using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Manager-flow cash-session view-model (T2.09.c, REQ-CORE-10).
/// Drives the "Abrir caja" / "Cerrar caja" controls and the
/// history list of recent sessions.
///
/// <para>
/// The variance calculation lives in
/// <see cref="ICashSessionService.CloseSessionAsync"/>; the VM
/// just exposes the resulting summary to the View.
/// </para>
/// </summary>
public partial class CashSessionManagementViewModel : ObservableObject
{
    private readonly ICashSessionService _service;
    private readonly ITenantSessionState _session;

    /// <summary>The currently-open cash session, or null when no session is open.</summary>
    [ObservableProperty]
    private CashSessionSummary? _openSession;

    /// <summary>The recent-closed-session history list.</summary>
    public ObservableCollection<CashSessionSummary> RecentSessions { get; } = new();

    /// <summary>The opening amount the manager typed in (only used while opening).</summary>
    [ObservableProperty]
    private decimal _openingAmountInput;

    /// <summary>The closing amount the manager typed in (only used while closing).</summary>
    [ObservableProperty]
    private decimal _closingAmountInput;

    /// <summary>One-line status message shown above the open-session card.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True while the open dialog is visible.</summary>
    [ObservableProperty]
    private bool _isOpening;

    /// <summary>True while the close dialog is visible.</summary>
    [ObservableProperty]
    private bool _isClosing;

    public CashSessionManagementViewModel(
        ICashSessionService service,
        ITenantSessionState session)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(session);
        _service = service;
        _session = session;
    }

    /// <summary>Refresh the open-session snapshot + history list.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        OpenSession = await _service.GetOpenSessionAsync(ct);
        var recent = await _service.ListRecentAsync(take: 20, ct);
        RecentSessions.Clear();
        foreach (var row in recent)
        {
            RecentSessions.Add(row);
        }
    }

    /// <summary>Open the "Abrir caja" dialog.</summary>
    [RelayCommand]
    public void BeginOpen()
    {
        OpeningAmountInput = 0m;
        IsOpening = true;
    }

    /// <summary>Cancel the "Abrir caja" dialog.</summary>
    [RelayCommand]
    public void CancelOpen()
    {
        IsOpening = false;
    }

    /// <summary>Confirm the open-session action.</summary>
    [RelayCommand]
    public async Task ConfirmOpenAsync(CancellationToken ct = default)
    {
        try
        {
            var snapshot = await _session.GetSnapshotAsync(ct);
            await _service.OpenSessionAsync(
                openedByUserName: snapshot.UserDisplayName,
                openingAmount: OpeningAmountInput,
                ct);
            IsOpening = false;
            StatusMessage = "Sesión de caja abierta.";
            await RefreshAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    /// <summary>Open the "Cerrar caja" dialog.</summary>
    [RelayCommand]
    public void BeginClose()
    {
        ClosingAmountInput = 0m;
        IsClosing = true;
    }

    /// <summary>Cancel the "Cerrar caja" dialog.</summary>
    [RelayCommand]
    public void CancelClose()
    {
        IsClosing = false;
    }

    /// <summary>Confirm the close-session action and surface the variance.</summary>
    [RelayCommand]
    public async Task ConfirmCloseAsync(CancellationToken ct = default)
    {
        try
        {
            var closed = await _service.CloseSessionAsync(
                closingAmount: ClosingAmountInput,
                ct);
            IsClosing = false;

            var variance = closed.VarianceAmount ?? 0m;
            var culture = CultureInfo.GetCultureInfo("es-CO");
            var varianceText = variance switch
            {
                > 0m => $"Sobrante de {variance.ToString("N0", culture)} COP.",
                < 0m => $"Faltante de {Math.Abs(variance).ToString("N0", culture)} COP.",
                _ => "Cuadre exacto.",
            };
            StatusMessage = $"Sesión cerrada. {varianceText}";
            await RefreshAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            StatusMessage = ex.Message;
        }
    }
}