using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Reports;

/// <summary>
/// Common contract for "write report rows to a stream"
/// implementations. Each <see cref="ExportFormat"/> maps to
/// one implementation; the ViewModel resolves via the
/// registered <see cref="IReportExporterProvider"/> so the
/// managers toolbar exposes them uniformly.
///
/// <para>
/// Implementations MUST close the stream when finished
/// (dispose pattern). Implementations MUST NOT block on
/// network or DB I/O; the byte buffers are produced in
/// memory and flushed at the end.
/// </para>
///
/// <para>
/// Replaces the legacy Crystal Reports "Export to PDF/Excel"
/// menu: the new pipeline is "export to bytes → write to
/// stream", agnostic of where the stream ends up (file
/// dialog, email attachment, server-side render, etc.).
/// </para>
/// </summary>
public interface IReportExporter
{
    /// <summary>Format identifier.</summary>
    ExportFormat Format { get; }

    /// <summary>Common file extension (without dot) — e.g. "pdf", "csv".</summary>
    string FileExtension { get; }

    /// <summary>
    /// Render the <paramref name="rows"/> to <paramref name="output"/>.
    /// The exporter writes the bytes sequentially and flushes
    /// at the end; the caller owns the stream lifetime.
    /// </summary>
    Task ExportAsync(
        IReadOnlyList<DailySalesRow> rows,
        Stream output,
        CancellationToken ct = default);
}

/// <summary>
/// Variant for the inventory report. Implementations may
/// share helpers with the sales exporter (e.g. CSV writer)
/// but the row contracts differ — column sets diverge so
/// we type the inputs explicitly to avoid <c>object</c>/
/// boxing across the hot path.
/// </summary>
public interface IInventoryExporter
{
    ExportFormat Format { get; }
    string FileExtension { get; }

    Task ExportAsync(
        IReadOnlyList<InventoryRow> rows,
        Stream output,
        CancellationToken ct = default);
}

/// <summary>
/// Variant for the cash-session report. Same rationale as
/// <see cref="IInventoryExporter"/>.
/// </summary>
public interface ICashSessionExporter
{
    ExportFormat Format { get; }
    string FileExtension { get; }

    Task ExportAsync(
        IReadOnlyList<CashSessionRow> rows,
        Stream output,
        CancellationToken ct = default);
}

/// <summary>
/// Provides the list of available exporters so the manager
/// toolbar can enumerate them. Each registered exporter
/// implements all three row-set interfaces (or throws at
/// registration time); the provider groups them under a
/// single <see cref="ExportFormat"/> for the menu.
/// </summary>
public interface IReportExporterProvider
{
    /// <summary>Every <see cref="ExportFormat"/> the pipeline supports in this build.</summary>
    IReadOnlyList<ExportFormat> AvailableFormats { get; }
}
