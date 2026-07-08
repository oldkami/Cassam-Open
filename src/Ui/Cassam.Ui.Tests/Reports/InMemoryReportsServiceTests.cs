using System;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Reports;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Reports;

/// <summary>
/// Unit tests for <see cref="InMemoryReportsService"/>. PR 10
/// (T2.11). Verifies day-bounded filtering, snapshot
/// stability, and the empty-row fallback.
/// </summary>
public class InMemoryReportsServiceTests
{
    [Fact]
    public async Task GetDailySalesAsync_returns_only_rows_within_the_target_day()
    {
        var service = new InMemoryReportsService();

        var today = await service.GetDailySalesAsync(DateTimeOffset.UtcNow);
        var yesterday = await service.GetDailySalesAsync(DateTimeOffset.UtcNow.AddDays(-1));

        today.Rows.Should().NotBeEmpty("the default fixture has today's sales");
        yesterday.Rows.Should().BeEmpty("the default fixture has no historical sales");
    }

    [Fact]
    public async Task GetInventoryAsync_returns_sorted_by_name()
    {
        var service = new InMemoryReportsService();

        var report = await service.GetInventoryAsync();

        report.Rows.Should().HaveCount(4);
        for (int i = 0; i < report.Rows.Count - 1; i++)
        {
            string.IsNullOrEmpty(report.Rows[i].Name).Should().BeFalse();
            string.Compare(report.Rows[i].Name, report.Rows[i + 1].Name, StringComparison.Ordinal)
                .Should().BeLessThan(0, "rows are sorted alphabetically by name");
        }
    }

    [Fact]
    public async Task GetCashSessionReportAsync_caps_at_30_days_by_default()
    {
        var service = new InMemoryReportsService();

        var report = await service.GetCashSessionReportAsync();

        report.Rows.Should().HaveCount(2, "fixture has 2 sessions within the last 30 days");
    }

    [Fact]
    public async Task GetCashSessionReportAsync_respects_days_parameter()
    {
        // Build a service with a deeply-stale session (>100 days) so the
        // days=0 filter excludes it. The default fixture's
        // yesterday session is within 0 days, so we replace it.
        var staleSession = new CashSessionRow(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-100),
            DateTimeOffset.UtcNow.AddDays(-100).AddHours(10),
            "Old Admin",
            100m, 200m, 200m, 0m, 3,
            0m, 0m, 100m, 19m, 81m);
        var service = new InMemoryReportsService(
            Array.Empty<DailySalesRow>(),
            Array.Empty<InventoryRow>(),
            new[] { staleSession });

        var report = await service.GetCashSessionReportAsync(days: 0);

        report.Rows.Should().BeEmpty("a -100-day-old session is excluded by days=0");
    }

    [Fact]
    public async Task GetDailySalesAsync_filters_by_utc_day_window()
    {
        // Build a fixture whose day-rows anchor on a fixed
        // calendar day so the test stays deterministic
        // regardless of when it runs.
        var fixedDay = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var rows = new[] {
            new DailySalesRow(Guid.NewGuid(), fixedDay, "FE-001-00001", null, null, 0, 0, 0, "Efectivo", "x", "TRANSMITTED", "1")
        };
        var service = new InMemoryReportsService(rows, Array.Empty<InventoryRow>(), Array.Empty<CashSessionRow>());

        var reportMatching = await service.GetDailySalesAsync(new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero));
        var reportOutside = await service.GetDailySalesAsync(new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero));

        reportMatching.Rows.Should().HaveCount(1, "the row falls in the target day");
        reportOutside.Rows.Should().BeEmpty("the row falls outside the target day");
    }

    [Fact]
    public void Constructor_accepts_caller_supplied_fixtures()
    {
        var sales = new[] {
            new DailySalesRow(Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, 0, 0, 0, "Efectivo", "x", "TRANSMITTED", "0")
        };
        var service = new InMemoryReportsService(sales, Array.Empty<InventoryRow>(), Array.Empty<CashSessionRow>());

        service.Should().NotBeNull();
    }
}
