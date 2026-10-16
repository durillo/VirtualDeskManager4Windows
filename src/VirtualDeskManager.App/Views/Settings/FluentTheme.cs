using System.Windows;
using Microsoft.Win32;

namespace VirtualDeskManager.App.Views.Settings;

/// <summary>
/// Pone el aspecto de Windows 11 (Fluent) a una ventana, claro u oscuro según Windows.
/// </summary>
/// <remarks>
/// Con <c>ThemeMode.System</c> WPF reaplica el tema a la ventana cada vez que Windows avisa de un
/// cambio de tema o de ajustes, y en .NET 10 eso a veces revienta: «referencia cíclica al evaluar
/// la propiedad Style del elemento TextBox», dentro de <c>ThemeManager.OnSystemThemeChanged</c>
/// (visto el 2026-10-09 al reconectar el Escritorio remoto al bastión). Con <c>Light</c> o
/// <c>Dark</c> fijo, elegido al abrir la ventana mirando el de Windows, WPF no vuelve a tocarla:
/// si se cambia el modo de Windows con la ventana abierta, lo coge la próxima vez que se abra.
/// </remarks>
public static class FluentTheme
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>El tema de Windows para las aplicaciones en este momento: claro u oscuro.</summary>
    public static bool AppsUseLightTheme()
    {
        try
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(Personalize);
            return clave?.GetValue("AppsUseLightTheme") is not int valor || valor != 0;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }

    /// <summary>Le pone el tema Fluent fijo, claro u oscuro como Windows ahora mismo.</summary>
    public static void Apply(Window window)
    {
#pragma warning disable WPF0001 // Experimental sólo porque la API puede cambiar de nombre.
        window.ThemeMode = AppsUseLightTheme() ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
    }
}
