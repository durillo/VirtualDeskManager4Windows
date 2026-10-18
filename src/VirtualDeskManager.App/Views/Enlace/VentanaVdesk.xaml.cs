using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using VirtualDeskManager.App.ViewModels;
using VirtualDeskManager.App.Views.Settings;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.App.Views.Enlace;

/// <summary>
/// La ventana principal de Virtual Desk Manager (ver el XAML). Clara de serie; la luna de la barra de herramientas la pasa a
/// oscura y el sol, de vuelta, y se recuerda. La barra de título es la de Windows: sólo se le
/// cambia el color para que vaya con el tema. Los ajustes van dentro (<see cref="PanelAjustes"/>),
/// con los mismos colores.
/// </summary>
public partial class VentanaVdesk : Window
{
    private const string Claro = "Views/Enlace/TemaMacClaro.xaml";
    private const string Oscuro = "Views/Enlace/TemaMacOscuro.xaml";

    private readonly VentanaVdeskViewModel _vm;
    private readonly Func<PanelAjustes>? _crearAjustes;
    private PanelAjustes? _ajustes;

    /// <summary>Crea la ventana con el tema guardado, o el que se pida (las capturas).</summary>
    /// <param name="viewModel">Lo que enseña.</param>
    /// <param name="oscuro">El tema; nulo, el guardado.</param>
    /// <param name="crearAjustes">Hace los ajustes la primera vez que se piden; sin él (las capturas) no hay ajustes.</param>
    public VentanaVdesk(VentanaVdeskViewModel viewModel, bool? oscuro = null, Func<PanelAjustes>? crearAjustes = null)
    {
        _crearAjustes = crearAjustes;
        Resources["NullToCollapsed"] = new VacioOculto();
        Resources["EmptyToCollapsed"] = new VacioOculto();
        Resources["EmptyToVisible"] = new VacioOculto { AlReves = true };
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Claro, UriKind.Relative) });

        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;

        // Eliminar un escritorio con ventanas se pregunta; la pregunta es de la ventana, no del modelo.
        viewModel.Escritorios.Confirmar = pregunta =>
            MessageBox.Show(this, pregunta, AppVersion.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            == MessageBoxResult.Yes;

        PonerTema(oscuro ?? viewModel.Oscuro);
        SourceInitialized += (_, _) => PonerBarraDeTitulo();

        // Automatizaciones y Distribuir ventanas son las páginas de los ajustes, puestas aquí.
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VentanaVdeskViewModel.Seccion))
            {
                PonerPagina();
            }
        };
        Loaded += (_, _) =>
        {
            PonerPagina();
            viewModel.Abierta();
        };
        Closed += (_, _) => viewModel.Cerrada();
    }

    private bool _oscuro;

    private bool EsOscuro => _oscuro;

    /// <summary>
    /// Pone los colores claros u oscuros. Las páginas de ajustes usan los controles de Fluent (cajas,
    /// botones, listas desplegables, tablas), así que el panel de ajustes lleva el tema Fluent del
    /// mismo modo y, encima, los colores de la ventana con las claves de Fluent (TemaMacClaro/Oscuro).
    /// Sólo el panel: la vista principal tiene sus propios estilos y no lo necesita.
    /// </summary>
    /// <remarks>
    /// No con <c>ThemeMode</c> en la ventana: lo pone en los recursos de la ventana, y los del XAML
    /// (InitializeComponent) los pisan, así que los controles salían con el aspecto clásico.
    /// </remarks>
    private void PonerTema(bool oscuro)
    {
        _oscuro = oscuro;
        Uri fuente = new(oscuro ? Oscuro : Claro, UriKind.Relative);

        ResourceDictionary? antes = Resources.MergedDictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("TemaMac") == true);
        if (antes is not null)
        {
            Resources.MergedDictionaries.Remove(antes);
        }

        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = fuente });

        HostAjustes.Resources.MergedDictionaries.Clear();
        HostAjustes.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{(oscuro ? "Dark" : "Light")}.xaml")
        });
        HostAjustes.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = fuente });

        // Lo mismo para las páginas de Automatizaciones y Distribuir ventanas.
        ScrollPaginas.Resources.MergedDictionaries.Clear();
        foreach (ResourceDictionary d in HostAjustes.Resources.MergedDictionaries)
        {
            ScrollPaginas.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = d.Source });
        }

        IconoTema.Text = oscuro ? "" : ""; // sol en oscuro, luna en claro
        PonerBarraDeTitulo();
    }

    // ------------------------------------------------------------------ ajustes

    /// <summary>Si se están viendo los ajustes.</summary>
    public bool EnAjustes => HostAjustes.Visibility == Visibility.Visible;

    /// <summary>Enseña los ajustes en esta misma ventana; con <paramref name="seccion"/>, en esa (por ejemplo «hotkeys»).</summary>
    public void MostrarAjustes(string? seccion)
    {
        if (_crearAjustes is null)
        {
            return;
        }

        if (_ajustes is null)
        {
            _ajustes = _crearAjustes();
            _ajustes.Volver += (_, _) => OcultarAjustes();
            HostAjustes.Content = _ajustes;
        }
        else if (!EnAjustes)
        {
            _ajustes.Refrescar();
        }

        HostAjustes.Visibility = Visibility.Visible;
        VistaVdesk.Visibility = Visibility.Hidden;
        Title = AppVersion.ProductName + " · " + Core.Localization.Loc.Get("Vdesk.Settings");

        if (seccion is not null)
        {
            _ajustes.GoTo(seccion);
        }
    }

    /// <summary>Vuelve de los ajustes a la vista principal.</summary>
    public void OcultarAjustes()
    {
        HostAjustes.Visibility = Visibility.Collapsed;
        VistaVdesk.Visibility = Visibility.Visible;
        Title = AppVersion.ProductName;
    }

    /// <summary>La barra de título de Windows, en oscuro o en claro como el resto. Sus botones no se tocan.</summary>
    private void PonerBarraDeTitulo()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0)
        {
            Dwmapi.TrySetDarkTitleBar(hwnd, EsOscuro);
        }
    }

    private void CambiarTema(object sender, RoutedEventArgs e)
    {
        bool oscuro = !EsOscuro;
        _vm.Oscuro = oscuro;
        PonerTema(oscuro);
    }

    /// <summary>«Ajustes», abajo en la barra lateral: abre los ajustes y no se queda marcado.</summary>
    private void AbrirAjustes(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lista && lista.SelectedIndex >= 0)
        {
            lista.SelectedIndex = -1;
            MostrarAjustes(null);
        }
    }

    // ------------------------------------------------------------------ Escritorios

    private FrameworkElement? _automatizaciones;
    private FrameworkElement? _distribuir;

    /// <summary>
    /// En Automatizaciones o Distribuir ventanas, su página de los ajustes. Los editores de reglas y
    /// las confirmaciones los presta el panel de ajustes, así que se crea si aún no estaba.
    /// </summary>
    private void PonerPagina()
    {
        if (_vm.Ajustes is not MainViewModel ajustes || !(_vm.EnAutomatizaciones || _vm.EnDistribuir))
        {
            ScrollPaginas.Visibility = Visibility.Collapsed;
            HostPaginas.Content = null;
            return;
        }

        if (_ajustes is null && _crearAjustes is not null)
        {
            _ajustes = _crearAjustes();
            _ajustes.Volver += (_, _) => OcultarAjustes();
            HostAjustes.Content = _ajustes;
        }

        HostPaginas.Content = _vm.EnAutomatizaciones
            ? _automatizaciones ??= new AutomationsPage { DataContext = ajustes }
            : _distribuir ??= new ZonesPage { DataContext = ajustes };
        ScrollPaginas.Visibility = Visibility.Visible;
        ScrollPaginas.ScrollToTop();
    }

    private static EscritorioVista? EscritorioDe(object sender) => (sender as FrameworkElement)?.DataContext as EscritorioVista;

    /// <summary>
    /// Un clic en una tarjeta lleva a ese escritorio. Al soltar y no al pulsar: si se pulsa en una
    /// ventana de la miniatura puede ser para arrastrarla, y entonces no se suelta aquí.
    /// </summary>
    private void TarjetaPulsada(object sender, MouseButtonEventArgs e)
    {
        _pulsada = false;

        if (EscritorioDe(sender) is { Actual: false } escritorio)
        {
            escritorio.IrCommand.Execute(null);
        }
    }

    /// <summary>«⋯» de una tarjeta: ir a él, ponerle nombre o eliminarlo.</summary>
    private void MenuEscritorio(object sender, RoutedEventArgs e)
    {
        if (EscritorioDe(sender) is not { } escritorio)
        {
            return;
        }

        ContextMenu menu = new() { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        MenuItem ir = new() { Header = Core.Localization.Loc.Get("Vdesk.GoToDesktop"), IsEnabled = !escritorio.Actual };
        ir.Click += (_, _) => escritorio.IrCommand.Execute(null);
        menu.Items.Add(ir);

        MenuItem renombrar = new() { Header = Core.Localization.Loc.Get("Vdesk.Rename") };
        renombrar.Click += (_, _) =>
        {
            string? nombre = TextPromptWindow.Ask(this, Core.Localization.Loc.Get("Vdesk.Rename"),
                Core.Localization.Loc.Get("Vdesk.DesktopName"), escritorio.Datos.Nombre);

            if (nombre is not null)
            {
                escritorio.Seccion.Renombrar(escritorio, nombre);
            }
        };
        menu.Items.Add(renombrar);

        // Separado y en rojo: es lo único del menú que quita algo. Con un solo escritorio no se
        // puede (Windows siempre necesita uno), y la entrada lo dice en vez de desaparecer.
        menu.Items.Add(new Separator());

        bool varios = escritorio.Seccion.HayVarios;
        MenuItem eliminar = new()
        {
            Header = Core.Localization.Loc.Get("Desktops.Remove"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)),
            IsEnabled = varios,
            ToolTip = varios ? null : Core.Localization.Loc.Get("Desktops.RemoveLast")
        };
        ToolTipService.SetShowOnDisabled(eliminar, true);
        eliminar.Click += (_, _) => escritorio.Seccion.Eliminar(escritorio);
        menu.Items.Add(eliminar);

        menu.IsOpen = true;
        e.Handled = true;
    }

    private Point _desde;
    private bool _pulsada;

    /// <summary>Se pulsa en una ventana de la miniatura: puede empezar a arrastrarse.</summary>
    private void EmpezarArrastre(object sender, MouseButtonEventArgs e)
    {
        _desde = e.GetPosition(this);
        _pulsada = true;
    }

    /// <summary>
    /// Arrastrar una ventana de una miniatura a otra tarjeta la lleva a ese escritorio. Sólo cuando
    /// el ratón se ha movido de verdad: un clic sin mover sigue siendo «ir a este escritorio».
    /// </summary>
    private void ArrastrarVentana(object sender, MouseEventArgs e)
    {
        if (!_pulsada || e.LeftButton != MouseButtonState.Pressed || (sender as FrameworkElement)?.DataContext is not VentanaMiniatura ventana)
        {
            _pulsada = false;
            return;
        }

        Vector movido = e.GetPosition(this) - _desde;

        if (Math.Abs(movido.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(movido.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _pulsada = false;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(VentanaMiniatura), ventana), DragDropEffects.Move);
    }

    private void VentanaEncima(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(VentanaMiniatura)) && EscritorioDe(sender) is not null
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Se suelta una ventana en una tarjeta: va a ese escritorio.</summary>
    private void SoltarVentana(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(VentanaMiniatura)) is VentanaMiniatura ventana && EscritorioDe(sender) is { } destino)
        {
            destino.Seccion.Mover(ventana.Ventana.Handle, destino);
            e.Handled = true;
        }
    }

    /// <summary>Nulo o vacío, oculto (y al revés: visible sólo si está vacío).</summary>
    private sealed class VacioOculto : IValueConverter
    {
        public bool AlReves { get; init; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            (value is null || value is string { Length: 0 }) != AlReves ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
