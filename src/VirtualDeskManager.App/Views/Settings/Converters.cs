using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VirtualDeskManager.App.Views.Settings;

/// <summary>Gira la flecha de un grupo plegado: hacia abajo cerrado, hacia arriba abierto.</summary>
public sealed class ChevronAngle : IValueConverter
{
    /// <summary>La única instancia: no guarda nada.</summary>
    public static readonly ChevronAngle Instance = new();

    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 180.0 : 0.0;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Esconde un texto que está vacío, para que no deje un hueco.</summary>
public sealed class CollapsedWhenEmpty : IValueConverter
{
    private readonly bool _inverse;

    private CollapsedWhenEmpty(bool inverse) => _inverse = inverse;

    /// <summary>Visible si hay texto.</summary>
    public static readonly CollapsedWhenEmpty Instance = new(false);

    /// <summary>Al revés: visible sólo si no hay texto (el «Buscar un ajuste» dentro de la caja vacía).</summary>
    public static readonly CollapsedWhenEmpty Inverse = new(true);

    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) != _inverse ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
