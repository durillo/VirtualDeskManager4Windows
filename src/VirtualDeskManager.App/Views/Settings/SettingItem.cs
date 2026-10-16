using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace VirtualDeskManager.App.Views.Settings;

/// <summary>
/// Un ajuste de la ventana de ajustes: su título, una línea que dice qué hace, el detalle de la
/// «i» y la explicación larga que sale con F1. El buscador y la ayuda los encuentran recorriendo
/// las páginas, así que todo ajuste que tenga que poder buscarse va dentro de uno.
/// </summary>
/// <remarks>
/// Es la pieza del estilo de ajustes fijado el 2026-10-09 (docs/ajustes/rediseno.html): un ajuste
/// nuevo se añade como una de estas filas, con su descripción, su detalle y su ayuda.
/// </remarks>
public abstract class SettingItem : ContentControl
{
    /// <summary>El título del ajuste.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingItem), new PropertyMetadata(string.Empty));

    /// <summary>Una línea, siempre a la vista, con lo que hace.</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingItem), new PropertyMetadata(string.Empty));

    /// <summary>El detalle que sale al pasar por la «i». Sin él, no hay «i».</summary>
    public static readonly DependencyProperty TipProperty = DependencyProperty.Register(
        nameof(Tip), typeof(string), typeof(SettingItem), new PropertyMetadata(string.Empty));

    /// <summary>La explicación larga, sólo para F1.</summary>
    public static readonly DependencyProperty HelpProperty = DependencyProperty.Register(
        nameof(Help), typeof(string), typeof(SettingItem), new PropertyMetadata(string.Empty));

    /// <summary>Encendida un momento cuando el buscador lleva hasta aquí, para que se vea cuál es.</summary>
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(SettingItem), new PropertyMetadata(false));

    private DispatcherTimer? _highlight;

    static SettingItem()
    {
        // Con foco, F1 sabe de qué ajuste se pide ayuda aunque el ratón esté en otra parte.
        FocusableProperty.OverrideMetadata(typeof(SettingItem), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(SettingItem), new FrameworkPropertyMetadata(false));
    }

    /// <inheritdoc cref="HeaderProperty"/>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <inheritdoc cref="DescriptionProperty"/>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <inheritdoc cref="TipProperty"/>
    public string Tip
    {
        get => (string)GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    /// <inheritdoc cref="HelpProperty"/>
    public string Help
    {
        get => (string)GetValue(HelpProperty);
        set => SetValue(HelpProperty, value);
    }

    /// <inheritdoc cref="IsHighlightedProperty"/>
    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    /// <summary>Lo resalta un segundo y medio, para que se vea adónde ha llevado el buscador.</summary>
    public void Flash()
    {
        _highlight?.Stop();
        IsHighlighted = true;

        _highlight = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _highlight.Tick += (_, _) =>
        {
            _highlight?.Stop();
            IsHighlighted = false;
        };
        _highlight.Start();
    }

    /// <summary>Todo el texto del ajuste junto, para el buscador.</summary>
    public string SearchText => string.Join(" ", Header, Description, Tip, Help);
}

/// <summary>Un ajuste en una línea: el texto a la izquierda y su control a la derecha.</summary>
public sealed class SettingRow : SettingItem
{
    static SettingRow() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(typeof(SettingRow)));
}

/// <summary>
/// Un ajuste que necesita todo el ancho (una lista, una tabla, varios botones): el texto arriba y
/// el contenido debajo.
/// </summary>
public sealed class SettingCard : SettingItem
{
    static SettingCard() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingCard), new FrameworkPropertyMetadata(typeof(SettingCard)));
}

/// <summary>
/// Un grupo de ajustes con su título. El buscador y la ayuda usan el título para decir dónde está
/// cada ajuste («Ventanas y apariencia › Botón en la barra de título»).
/// </summary>
public sealed class SettingGroup : HeaderedContentControl
{
    static SettingGroup() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingGroup), new FrameworkPropertyMetadata(typeof(SettingGroup)));
}

/// <summary>Las órdenes que mandan las páginas a la ventana: ir a otra sección.</summary>
public static class SettingsCommands
{
    /// <summary>Lleva a la sección cuya clave va de parámetro (por ejemplo «hotkeys»).</summary>
    public static readonly RoutedUICommand GoToSection = new("GoToSection", nameof(GoToSection), typeof(SettingsCommands));
}
