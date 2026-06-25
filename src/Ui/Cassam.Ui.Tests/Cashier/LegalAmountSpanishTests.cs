using Cassam.Ui.Hardware.Common.Cashier;
using FluentAssertions;

namespace Cassam.Ui.Tests.Cashier;

/// <summary>
/// Unit tests for <see cref="LegalAmountSpanish"/>. The es-CO
/// number-to-words surface is the DD-09 receipt-text contract
/// every cashier-printable receipt depends on (SCN-UI-09).
/// </summary>
public class LegalAmountSpanishTests
{
    [Fact]
    public void ToLegalText_zero_uses_UN_PESOS_singular()
    {
        var result = LegalAmountSpanish.ToLegalText(0m);
        result.Should().Be("SON: CERO PESOS M/CTE");
    }

    [Fact]
    public void ToLegalText_one_peso_uses_UN_PESO_singular()
    {
        var result = LegalAmountSpanish.ToLegalText(1m);
        result.Should().Be("SON: UN PESOS M/CTE");
    }

    [Theory]
    [InlineData(100, "SON: CIEN PESOS M/CTE")]
    [InlineData(1_000, "SON: MIL PESOS M/CTE")]
    [InlineData(10_000, "SON: DIEZ MIL PESOS M/CTE")]
    [InlineData(100_000, "SON: CIEN MIL PESOS M/CTE")]
    [InlineData(1_000_000, "SON: UN MILLON PESOS M/CTE")]
    public void ToLegalText_round_amounts_render_correctly(decimal amount, string expected)
    {
        LegalAmountSpanish.ToLegalText(amount).Should().Be(expected);
    }

    [Fact]
    public void ToLegalText_one_peso_fifty_centavos_includes_centavos_clause()
    {
        var result = LegalAmountSpanish.ToLegalText(1.50m);
        result.Should().Be("SON: UN PESOS CON CINCUENTA CENTAVOS M/CTE");
    }

    [Fact]
    public void ToLegalText_one_centavo_uses_singular_centavo()
    {
        var result = LegalAmountSpanish.ToLegalText(0.01m);
        result.Should().Be("SON: CERO PESOS CON UN CENTAVO M/CTE");
    }

    [Fact]
    public void ToLegalText_ninety_nine_centavos_uses_plural()
    {
        var result = LegalAmountSpanish.ToLegalText(0.99m);
        result.Should().Be("SON: CERO PESOS CON NOVENTA Y NUEVE CENTAVOS M/CTE");
    }

    [Fact]
    public void ToLegalText_one_million_plus_rendered_with_full_segmentation()
    {
        // 1.234.567,50 → "UN MILLON DOSCIENTOS TREINTA Y CUATRO MIL
        // QUINIENTOS SESENTA Y SIETE PESOS CON CINCUENTA CENTAVOS M/CTE"
        var result = LegalAmountSpanish.ToLegalText(1_234_567.50m);
        result.Should().Be(
            "SON: UN MILLON DOSCIENTOS TREINTA Y CUATRO MIL QUINIENTOS " +
            "SESENTA Y SIETE PESOS CON CINCUENTA CENTAVOS M/CTE");
    }

    [Fact]
    public void ToLegalText_one_billion_rendered_with_UN_MIL_MILLON_head()
    {
        var result = LegalAmountSpanish.ToLegalText(1_000_000_000m);
        result.Should().Be("SON: UN MIL MILLON PESOS M/CTE");
    }

    [Fact]
    public void ToLegalText_rejects_negative_amounts()
    {
        var act = () => LegalAmountSpanish.ToLegalText(-1m);
        act.Should().Throw<ArgumentOutOfRangeException>(
            "DIAN receipts cannot carry a negative legal amount");
    }

    [Fact]
    public void ToLegalText_rounds_half_cent_up()
    {
        // 1.005 → 1.01 (rounded half-up); legal text uses the rounded value.
        var result = LegalAmountSpanish.ToLegalText(1.005m);
        result.Should().Be("SON: UN PESOS CON UN CENTAVO M/CTE");
    }

    [Fact]
    public void ToLegalText_handles_centavos_overflow_from_rounding()
    {
        // 99.999 → 100.00 after rounding. The centavos clause must
        // be suppressed (no cents) and the peso count must include
        // the rolled-up 100 pesos.
        var result = LegalAmountSpanish.ToLegalText(99.999m);
        result.Should().Be("SON: CIEN PESOS M/CTE");
    }

    [Theory]
    [InlineData(1234.56, "$ 1.234,56")]
    [InlineData(1000000, "$ 1.000.000,00")]
    [InlineData(0.50, "$ 0,50")]
    public void FormatCop_uses_es_CO_locale(decimal amount, string expected)
    {
        LegalAmountSpanish.FormatCop(amount).Should().Be(expected);
    }

    [Fact]
    public void Output_never_contains_accents()
    {
        // The Anexo requires ASCII — no tildes in the legal text.
        // Even if a future translator adds accents elsewhere, this
        // contract must hold for the printed receipt.
        var text = LegalAmountSpanish.ToLegalText(1_234_567.50m);
        text.Should().NotContain("á").And.NotContain("é").And.NotContain("í")
            .And.NotContain("ó").And.NotContain("ú").And.NotContain("ñ");
    }
}
