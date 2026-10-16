using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualDeskManager.App.Localization;

/// <summary>
/// Visible cuando el valor es falso: para los textos que sólo salen cuando algo no hay.
/// </summary>
/// <remarks>
/// El conversor de WPF sólo hace el sentido normal, y escribir la propiedad al revés en el modelo
/// de vista (un <c>NoHayAplicaciones</c> junto a <c>HasApplications</c>) sería duplicar estado
/// para ahorrarse diez líneas de aquí.
/// </remarks>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <summary>Oculto si el valor es <c>true</c>; visible en cualquier otro caso, también si no es un booleano.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>El camino de vuelta: <c>true</c> si no está visible.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility visibility && visibility != Visibility.Visible;
}
