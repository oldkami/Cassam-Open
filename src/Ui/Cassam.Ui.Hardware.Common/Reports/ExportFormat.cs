namespace Cassam.Ui.Hardware.Common.Reports;

/// <summary>
/// Identifies the export format the manager picks from the
/// reports toolbar. PR 10 (T2.11) implements PDF (QuestPDF)
/// + Excel (mini-XLSX via OpenXML) + CSV (RFC 4180) + JSON
/// (System.Text.Json). The values are encoded in the export
/// command's <c>CommandParameter</c> as a string key.
/// </summary>
public enum ExportFormat
{
    Pdf = 0,
    Excel = 1,
    Csv = 2,
    Json = 3,
}
