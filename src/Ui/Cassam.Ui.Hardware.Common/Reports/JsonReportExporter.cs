using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Reports;

/// <summary>
/// JSON exporter (System.Text.Json). Writes a single JSON
/// object with a "rows" array + a top-level "asOf" timestamp.
/// Naming policy = camelCase so the file is human-readable
/// in any text editor; numeric values are emitted as JSON
/// numbers (no quotes).
/// </summary>
/// <remarks>
/// The exporter uses explicit DTOs with <see cref="JsonPropertyNameAttribute"/>
/// rather than anonymous types + naming policy. The two
/// approaches produce identical output, but the explicit
/// attributes are easier to reason about (the policy
/// re-casing of multi-segment names can mutate
/// properties named like <c>rowCount</c>).
/// </remarks>
public static class JsonReportExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task WriteDailySalesAsync(
        IReadOnlyList<DailySalesRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        var envelope = new DailySalesEnvelope(System.DateTimeOffset.UtcNow, rows.Count, rows);
        return JsonSerializer.SerializeAsync(output, envelope, Options, ct);
    }

    public static Task WriteInventoryAsync(
        IReadOnlyList<InventoryRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        var envelope = new InventoryEnvelope(System.DateTimeOffset.UtcNow, rows.Count, rows);
        return JsonSerializer.SerializeAsync(output, envelope, Options, ct);
    }

    public static Task WriteCashSessionAsync(
        IReadOnlyList<CashSessionRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        var envelope = new CashSessionEnvelope(System.DateTimeOffset.UtcNow, rows.Count, rows);
        return JsonSerializer.SerializeAsync(output, envelope, Options, ct);
    }

    private sealed record DailySalesEnvelope(
        [property: JsonPropertyName("asOf")] System.DateTimeOffset AsOf,
        [property: JsonPropertyName("rowCount")] int RowCount,
        [property: JsonPropertyName("rows")] IReadOnlyList<DailySalesRow> Rows);

    private sealed record InventoryEnvelope(
        [property: JsonPropertyName("asOf")] System.DateTimeOffset AsOf,
        [property: JsonPropertyName("rowCount")] int RowCount,
        [property: JsonPropertyName("rows")] IReadOnlyList<InventoryRow> Rows);

    private sealed record CashSessionEnvelope(
        [property: JsonPropertyName("asOf")] System.DateTimeOffset AsOf,
        [property: JsonPropertyName("rowCount")] int RowCount,
        [property: JsonPropertyName("rows")] IReadOnlyList<CashSessionRow> Rows);
}
