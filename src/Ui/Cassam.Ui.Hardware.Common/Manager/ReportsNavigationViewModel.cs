using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Identifies which report sub-tab the manager shell currently
/// renders. PR 10 (T2.09.d) ships only the navigation shell —
/// the actual report content lands in PR 11 (T2.11).
/// </summary>
public enum ReportSubTab
{
    /// <summary>Daily sales — replaces RFacturaCarta.rpt + RFacturaMediaCarta.rpt.</summary>
    DailySales = 0,

    /// <summary>Inventory status — replaces RInventarioCardex.</summary>
    Inventory = 1,

    /// <summary>Cash session / cuadre de caja summary.</summary>
    CashClose = 2,
}

/// <summary>
/// Manager-flow reports navigation shell. PR 10 ships only the
/// route selection + "[Reporte se renderiza en PR 11]" placeholder
/// per the prompt's "DO NOT IMPLEMENT T2.11" guard. The actual
/// rendering work lives in T2.11 + the
/// <c>QuestPdfReportRenderer</c> (design §11).
/// </summary>
public partial class ReportsNavigationViewModel : ObservableObject
{
    /// <summary>The currently selected report sub-tab.</summary>
    [ObservableProperty]
    private ReportSubTab _selectedTab = ReportSubTab.DailySales;

    [RelayCommand]
    public void Select(ReportSubTab tab) => SelectedTab = tab;
}