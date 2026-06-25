using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace Cassam.Ui.Common.Converters;

/// <summary>
/// Formats a <see cref="decimal"/> as the es-CO COP currency string
/// (<c>$ 1.234.567,50</c>) for XAML binding. Inverse parsing is
/// also supported so two-way bindings (the cash-tendered input)
/// can round-trip user input through the same locale-aware code
/// path.
/// </summary>
public sealed class CurrencyConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal d)
        {
            return Cassam.Ui.Hardware.Common.Cashier.LegalAmountSpanish.FormatCop(d);
        }
        if (value is null) return string.Empty;
        return value.ToString() ?? string.Empty;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is string s)
        {
            // Strip the "$" prefix + whitespace + thousands separators
            // so the user can type "$ 50.000,50" or "50000.50" and
            // both parse cleanly.
            var trimmed = s.Replace("$", string.Empty, StringComparison.Ordinal)
                           .Replace(".", string.Empty, StringComparison.Ordinal)
                           .Replace(" ", string.Empty, StringComparison.Ordinal)
                           .Trim();
            return decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.GetCultureInfo("es-CO"), out var result)
                ? result
                : 0m;
        }
        return 0m;
    }
}

/// <summary>
/// Parses / formats a <see cref="decimal"/> for XAML two-way
/// bindings on plain TextBox controls. Inverse parser strips
/// whitespace + culture separators and falls back to zero on
/// unparseable input.
/// </summary>
public sealed class DecimalConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal d) return d.ToString(CultureInfo.GetCultureInfo("es-CO"));
        return string.Empty;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is string s &&
            decimal.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.GetCultureInfo("es-CO"), out var result))
        {
            return result;
        }
        return 0m;
    }
}
