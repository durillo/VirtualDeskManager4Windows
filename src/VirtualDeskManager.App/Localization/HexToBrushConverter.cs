using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App.Localization;

/// <summary>
/// Convierte un <c>#RRGGBB</c> en una brocha, para las muestras de color de los ajustes.
/// </summary>
/// <remarks>
/// Un conversor y no una propiedad de tipo brocha en el modelo de vista porque las muestras salen
/// dentro de una plantilla, una por color de la lista, y cada una tiene que pintarse con el suyo.
/// Un color que no se entienda se pinta gris en vez de reventar la plantilla: mientras el usuario
/// escribe en la caja de al lado, el texto pasa por estados a medias como <c>#8</c>.
/// </remarks>
public sealed class HexToBrushConverter : IValueConverter
{
    /// <summary>Del texto <c>#RRGGBB</c> a una brocha congelada. Lo que no se entienda sale gris.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        RgbColour colour = RgbColour.Parse(value as string);

        SolidColorBrush brush = new(Color.FromRgb(colour.R, colour.G, colour.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>De una brocha de color sólido a su <c>#RRGGBB</c>; cualquier otra cosa da <c>#808080</c>.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SolidColorBrush brush
            ? new RgbColour(brush.Color.R, brush.Color.G, brush.Color.B).Text
            : "#808080";
}
