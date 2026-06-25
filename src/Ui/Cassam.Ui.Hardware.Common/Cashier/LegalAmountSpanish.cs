using System.Globalization;

namespace Cassam.Ui.Hardware.Common.Cashier;

/// <summary>
/// Renders Colombian-Peso amounts as the es-CO legal receipt
/// "SON: ... PESOS M/CTE" line required by the DIAN Anexo Técnico
/// (design §10 / DD-09).
///
/// <para>
/// Why a hand-rolled helper instead of
/// <c>Humanizer.ToWords(amount, new CultureInfo("es-CO"))</c>:
/// <list type="bullet">
///   <item>Humanizer's es-CO number-to-words covers the integer
///         part but the DIAN receipt text needs an explicit "PESOS
///         M/CTE" suffix, a "CON [X] CENTAVOS" suffix when there
///         are cents, and uppercase no-accent rendering per the
///         Anexo.</item>
///   <item>The legal text must round the amount to two decimal
///         places before the words are computed — receipts are
///         <c>numeric(18,4)</c> in PostgreSQL but the printed
///         legal text is rounded to the cent.</item>
///   <item>Edge cases (zero, one peso, exact 100 / 1000 / 1M / 1B,
///         negative amounts) need explicit guards; Humanizer's
///         output is correct but verbose.</item>
/// </list>
/// </para>
///
/// <para>
/// Reference:
/// <list type="bullet">
///   <item>dd-MM-yyyy: <c>UN MILLON DOSCIENTOS TREINTA Y CUATRO MIL
///         QUINIENTOS SESENTA Y SIETE PESOS CON CINCUENTA CENTAVOS
///         M/CTE</c>.</item>
///   <item>No accents: the Anexo renders the legal text in ASCII
///         (no tildes). The output of this helper is therefore
///         unaccented.</item>
///   <item>Always uppercase.</item>
/// </list>
/// </para>
/// </summary>
public static class LegalAmountSpanish
{
    /// <summary>
    /// Render <paramref name="amount"/> as the DIAN "SON: ... PESOS
    /// M/CTE" legal receipt text. The amount is rounded to two
    /// decimal places (the receipt's cent precision). Negative
    /// amounts throw because the DIAN Anexo disallows them on
    /// receipts.
    /// </summary>
    /// <example>
    /// <code>
    /// LegalAmountSpanish.ToLegalText(1234567.50m);
    /// // =&gt; "SON: UN MILLON DOSCIENTOS TREINTA Y CUATRO MIL
    /// //     QUINIENTOS SESENTA Y SIETE PESOS CON CINCUENTA
    /// //     CENTAVOS M/CTE"
    /// </code>
    /// </example>
    public static string ToLegalText(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "DIAN receipts cannot carry a negative legal amount.");
        }

        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var pesos = (long)decimal.Truncate(rounded);
        var centavos = (int)Math.Round((rounded - pesos) * 100m, MidpointRounding.AwayFromZero);

        // Centavos overflow: rounding may bump us to the next peso
        // (e.g. 99.999 → 100.00). Fold the overflow back.
        if (centavos == 100)
        {
            pesos += 1;
            centavos = 0;
        }

        var pesosText = ToWords(pesos);
        var centavosClause = centavos switch
        {
            0 => string.Empty,
            1 => " CON UN CENTAVO",
            _ => $" CON {ToWords(centavos)} CENTAVOS",
        };

        return $"SON: {pesosText} PESOS{centavosClause} M/CTE";
    }

    // ---- Number-to-words (es-CO, uppercase, no accents) -------------

    private static readonly string[] Units =
    {
        "CERO", "UN", "DOS", "TRES", "CUATRO", "CINCO",
        "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ",
        "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE",
        "DIECISEIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE",
        "VEINTE", "VEINTIUN", "VEINTIDOS", "VEINTITRES", "VEINTICUATRO",
        "VEINTICINCO", "VEINTISEIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE",
    };

    private static readonly string[] Tens =
    {
        "CERO", "DIEZ", "VEINTE", "TREINTA", "CUARENTA",
        "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA",
    };

    private static readonly string[] Hundreds =
    {
        "CERO", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS",
        "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS",
    };

    /// <summary>
    /// Convert a non-negative long to its es-CO words (uppercase,
    /// no accents). Supports values up to <see cref="long.MaxValue"/>
    /// which is ~9.2 quintillions — far beyond any realistic POS
    /// receipt total.
    /// </summary>
    internal static string ToWords(long value)
    {
        if (value == 0) return "CERO";
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "value must be non-negative");
        if (value < 30) return Units[value];
        if (value < 100) return TwoDigitWords((int)value);
        if (value < 1_000) return ThreeDigitWords((int)value);
        if (value < 1_000_000) return ThousandWords((int)value);
        if (value < 1_000_000_000) return MillionWords(value);
        if (value < 1_000_000_000_000L) return BillionWords(value);
        return TrillionWords(value);
    }

    private static string TwoDigitWords(int v)
    {
        var t = v / 10;
        var u = v % 10;
        if (u == 0) return Tens[t];
        return $"{Tens[t]} Y {Units[u]}";
    }

    private static string ThreeDigitWords(int v)
    {
        var h = v / 100;
        var rem = v % 100;
        // Hundreds[0] is "CERO" — only emit when h > 0. A 0-hundreds
        // prefix with a non-zero remainder drops to "DIEZ" /
        // "VEINTIUN" without the spurious "CERO" the array lookup
        // would otherwise produce.
        if (h == 0)
        {
            if (rem == 0) return string.Empty;
            return rem < 30 ? Units[rem] : TwoDigitWords(rem);
        }
        var head = h == 1 && rem == 0 ? "CIEN" : Hundreds[h];
        if (rem == 0) return head;
        return rem < 30 ? $"{head} {Units[rem]}" : $"{head} {TwoDigitWords(rem)}";
    }

    private static string ThousandWords(int v)
    {
        var thousands = v / 1000;
        var rem = v % 1000;
        var head = thousands == 1 ? "MIL" : $"{ThreeDigitWords(thousands)} MIL";
        if (rem == 0) return head;
        return $"{head} {ThreeDigitWords(rem)}";
    }

    private static string MillionWords(long v)
    {
        var millions = v / 1_000_000L;
        var rem = v % 1_000_000L;
        var head = millions == 1
            ? "UN MILLON"
            : $"{ThreeDigitWords((int)Math.Min(millions, int.MaxValue))} MILLONES";
        if (rem == 0) return head;
        var remText = rem < 1000L
            ? ToWords(rem)
            : ThousandWords((int)rem);
        return $"{head} {remText}";
    }

    private static string BillionWords(long v)
    {
        var billions = v / 1_000_000_000L;
        var rem = v % 1_000_000_000L;
        var head = billions == 1
            ? "UN MIL MILLON"
            : $"{ToWords(billions)} MIL MILLONES";
        if (rem == 0) return head;
        return $"{head} {ToWords(rem)}";
    }

    private static string TrillionWords(long v)
    {
        // Colombian POS receipts do not reach trillions of pesos
        // in practice (the country's GDP is ~USD 350 B). The
        // helper still completes the conversion for completeness.
        var trillions = v / 1_000_000_000_000L;
        var rem = v % 1_000_000_000_000L;
        var head = trillions == 1
            ? "UN BILLON"
            : $"{ToWords(trillions)} BILLONES";
        if (rem == 0) return head;
        return $"{head} {ToWords(rem)}";
    }

    /// <summary>
    /// Format a COP amount as the customer-facing currency string.
    /// Uses <see cref="CultureInfo"/> es-CO so the
    /// thousands separator is a dot and the decimal separator is a
    /// comma (per DD-09 / SCN-UI-09). Prefixed with <c>$</c> and
    /// separated from the amount by a single space — the convention
    /// on Colombian fiscal receipts.
    /// </summary>
    public static string FormatCop(decimal amount)
    {
        var culture = CultureInfo.GetCultureInfo("es-CO");
        return $"$ {amount.ToString("N2", culture)}";
    }
}
