using System.Windows;
using VirtualDeskManager.App.ViewModels;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.Views.Settings;

/// <summary>
/// Una sección de la barra lateral de los ajustes: su icono, su nombre, la frase que la presenta,
/// su ayuda para F1 y su página. Los textos salen de <c>Nav.&lt;Clave&gt;</c>,
/// <c>Page.&lt;Clave&gt;.Intro</c> y <c>Page.&lt;Clave&gt;.Help</c>.
/// </summary>
public sealed class SettingsSection : ObservableObject
{
    /// <param name="key">La clave, que usan los enlaces entre secciones («hotkeys»).</param>
    /// <param name="textKey">La parte de las claves de traducción («Hotkeys»).</param>
    /// <param name="glyph">El icono, de la fuente Segoe Fluent Icons.</param>
    /// <param name="page">La página, ya con su modelo de vista puesto.</param>
    public SettingsSection(string key, string textKey, string glyph, FrameworkElement page)
    {
        Key = key;
        TextKey = textKey;
        Glyph = glyph;
        Page = page;
    }

    /// <summary>La clave, que usan los enlaces entre secciones.</summary>
    public string Key { get; }

    /// <summary>La parte de las claves de traducción.</summary>
    public string TextKey { get; }

    /// <summary>El icono, de la fuente Segoe Fluent Icons.</summary>
    public string Glyph { get; }

    /// <summary>La página que se enseña al elegirla.</summary>
    public FrameworkElement Page { get; }

    /// <summary>El nombre en la barra lateral y de título de la página.</summary>
    public string Title => Loc.Get("Nav." + TextKey);

    /// <summary>La frase que la presenta, debajo del título.</summary>
    public string Intro => Loc.Get("Page." + TextKey + ".Intro");

    /// <summary>Lo que dice F1 cuando no se pide ayuda de ningún ajuste en concreto.</summary>
    public string Help => Loc.Get("Page." + TextKey + ".Help");

    /// <summary>Vuelve a leer los textos tras un cambio de idioma.</summary>
    public void RefreshTexts()
    {
        Raise(nameof(Title));
        Raise(nameof(Intro));
        Raise(nameof(Help));
    }

    /// <inheritdoc/>
    public override string ToString() => Title;
}

/// <summary>Un resultado del buscador: el ajuste, dónde está y su descripción.</summary>
public sealed class SearchHit
{
    /// <param name="item">El ajuste encontrado.</param>
    /// <param name="section">Su sección.</param>
    /// <param name="path">Sección y grupo, como «Ventanas y apariencia › Botón en la barra de título».</param>
    public SearchHit(SettingItem item, SettingsSection section, string path)
    {
        Item = item;
        Section = section;
        Path = path;
    }

    /// <summary>El ajuste encontrado.</summary>
    public SettingItem Item { get; }

    /// <summary>Su sección.</summary>
    public SettingsSection Section { get; }

    /// <summary>Dónde está.</summary>
    public string Path { get; }

    /// <summary>Su título; si no tiene, el de su grupo.</summary>
    public string Header => string.IsNullOrWhiteSpace(Item.Header) ? Path : Item.Header;

    /// <summary>Su descripción, si la tiene.</summary>
    public string Description => Item.Description;

    /// <summary>El icono de su sección.</summary>
    public string Glyph => Section.Glyph;
}
