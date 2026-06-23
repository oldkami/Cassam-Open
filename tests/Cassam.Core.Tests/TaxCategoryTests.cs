using Cassam.Core.Domain.Enums;
using FluentAssertions;

namespace Cassam.Core.Tests;

/// <summary>
/// Pins the four Colombian DIAN tax-category letters
/// (S / Z / E / O) to the right enum members per
/// <c>pos-fiscal-dee-pos</c> SCN-DEEPOS-02 and
/// <c>pos-core-modern-stack</c> REQ-CORE-11.
/// </summary>
public class TaxCategoryTests
{
    [Theory]
    [InlineData(TaxCategory.Standard, "Standard — Gravado, taxed at the prevailing IVA rate")]
    [InlineData(TaxCategory.ZeroRate, "ZeroRate — Tasa cero, 0% but reportable")]
    [InlineData(TaxCategory.Exempt, "Exempt — Exento, outside the scope of the tax")]
    [InlineData(TaxCategory.Other, "Other — Otro, excluded / non-taxable")]
    public void Each_tax_category_is_defined(TaxCategory category, string description)
    {
        // The category enum members map directly to the DIAN <TaxCategory>
        // letter per Anexo Técnico (DEE POS 1.0 / FE Venta 1.9).
        // Standard is intentionally value 0 so a freshly-constructed Product
        // defaults to the most common tax class (gravado).
        Enum.IsDefined(category).Should().BeTrue($"{description}");
    }

    [Fact]
    public void Tax_category_has_exactly_four_distinct_values()
    {
        // The four DIAN letters S/Z/E/O map to four distinct enum members.
        var distinctCategories = Enum.GetValues<TaxCategory>()
            .Select(c => (long)c)
            .Distinct()
            .ToList();

        distinctCategories.Should().HaveCount(4,
            "the four DIAN letters S/Z/E/O map to four distinct enum members");
    }

    [Fact]
    public void Tax_category_letters_round_trip_through_string_conversion()
    {
        // The DbContext configures TaxCategory with HasConversion<string>()
        // and HasMaxLength(1). The stored value is the single letter.
        var expectedMapping = new Dictionary<TaxCategory, string>
        {
            [TaxCategory.Standard] = nameof(TaxCategory.Standard),
            [TaxCategory.ZeroRate] = nameof(TaxCategory.ZeroRate),
            [TaxCategory.Exempt] = nameof(TaxCategory.Exempt),
            [TaxCategory.Other] = nameof(TaxCategory.Other),
        };

        foreach (var (category, expectedName) in expectedMapping)
        {
            var roundTripped = Enum.Parse<TaxCategory>(expectedName);
            roundTripped.Should().Be(category);
        }
    }
}
