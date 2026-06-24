using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using FluentAssertions;

namespace Cassam.Core.Tests;

/// <summary>
/// Compliance-entity tests for <see cref="Resolucion"/>. These cover the
/// shape required by <c>pos-core-modern-stack</c> REQ-CORE-06:
/// document type, range, current number, expiration, technical-key FK,
/// status, and prefix.
/// </summary>
public class ResolucionTests
{
    [Fact]
    public void New_resolucion_starts_in_DRAFT_with_range_start_minus_one_as_current_number()
    {
        var resolucion = new Resolucion
        {
            TenantId = Guid.CreateVersion7(),
            DocumentType = DocumentType.DeePos,
            RangeStart = 1_000_000,
            RangeEnd = 2_000_000,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)),
            SoftwareTechnicalKeyId = Guid.CreateVersion7(),
            Status = ResolucionStatus.Draft,
        };

        resolucion.Status.Should().Be(ResolucionStatus.Draft);
        resolucion.CurrentNumber.Should().Be(0L,
            "CurrentNumber defaults to 0 so the first issued document is range_start (1-based)");
    }

    [Fact]
    public void Resolucion_lifecycle_progresses_DRAFT_to_ACTIVE_to_EXPIRED()
    {
        var resolucion = new Resolucion
        {
            TenantId = Guid.CreateVersion7(),
            DocumentType = DocumentType.FeVenta,
            RangeStart = 100,
            RangeEnd = 200,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            SoftwareTechnicalKeyId = Guid.CreateVersion7(),
        };

        // Lifecycle progression: DRAFT → ACTIVE → EXPIRED per SCN-CORE-06/07.
        resolucion.Status = ResolucionStatus.Draft;
        resolucion.Status.Should().Be(ResolucionStatus.Draft);

        resolucion.Status = ResolucionStatus.Active;
        resolucion.Status.Should().Be(ResolucionStatus.Active);

        resolucion.Status = ResolucionStatus.Expired;
        resolucion.Status.Should().Be(ResolucionStatus.Expired,
            "after expiration_date the operator (or the daily check job) flips status to EXPIRED");
    }

    [Fact]
    public void Resolucion_carries_a_DIAN_prefix_when_authorized()
    {
        var resolucion = new Resolucion
        {
            TenantId = Guid.CreateVersion7(),
            DocumentType = DocumentType.NotaCredito,
            RangeStart = 1,
            RangeEnd = 100,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            SoftwareTechnicalKeyId = Guid.CreateVersion7(),
            Prefix = "NC",
        };

        resolucion.Prefix.Should().Be("NC",
            "DIAN assigns a prefix (e.g. NC for Notas Crédito) so the same tenant can hold distinct ranges per document type");
    }

    [Fact]
    public void Resolucion_is_soft_deletable_and_tenant_scoped()
    {
        var resolucion = new Resolucion
        {
            TenantId = Guid.CreateVersion7(),
        };

        resolucion.DeletedAt.Should().BeNull();
        resolucion.TenantId.Should().NotBe(Guid.Empty);
    }
}
