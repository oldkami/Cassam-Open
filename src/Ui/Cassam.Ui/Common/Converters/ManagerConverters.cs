using Cassam.Ui.Hardware.Common.Stubs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Cassam.Ui.Common.Converters;

/// <summary>
/// Maps <c>true</c> to <see cref="Visibility.Visible"/> and
/// <c>false</c> to <see cref="Visibility.Collapsed"/> for XAML
/// <c>Visibility</c> bindings on bool flags.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// Inverse of <see cref="BoolToVisibilityConverter"/>: maps
/// <c>true</c> to <see cref="Visibility.Collapsed"/> and
/// <c>false</c> to <see cref="Visibility.Visible"/>.
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v != Visibility.Visible;
}

/// <summary>
/// Maps a non-null object to <c>true</c> and a null to
/// <c>false</c>. Used to drive button enable state from "is
/// there an open session?" boolean tests.
/// </summary>
public sealed class NullToBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not null;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// Formats a non-empty string as <see cref="Visibility.Visible"/>
/// and an empty / null string as <see cref="Visibility.Collapsed"/>.
/// Used to hide an error message when there is no error.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// Formats a <see cref="DateTime"/> / <see cref="DateTimeOffset"/>
/// as <c>dd/MM/yyyy</c> per the DD-09 es-CO date format.
/// </summary>
public sealed class DateEsCoConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("es-CO");
        return value switch
        {
            DateTime dt => dt.ToString("dd/MM/yyyy", culture),
            DateTimeOffset dto => dto.ToString("dd/MM/yyyy", culture),
            _ => string.Empty,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="SyncMode"/> value to a UI color string per
/// DD-10 (green = Online, amber = LimitedConnectivity, red =
/// Offline).
/// </summary>
public sealed class SyncModeToColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        SyncMode.Online => "Green",
        SyncMode.LimitedConnectivity => "Orange",
        SyncMode.Offline => "Red",
        _ => "Gray",
    };

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}