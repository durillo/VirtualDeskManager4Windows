using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VirtualDeskManager.App.ViewModels;
using VirtualDeskManager.App.Views;
using VirtualDeskManager.Core.Backup;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.App.Views.Settings;

/// <summary>
/// Los ajustes dentro de la ventana principal (ver el XAML): navega entre las secciones, busca y
/// explica con F1. Le presta al modelo de vista los diálogos (editores, confirmaciones, elegir
/// fichero), con la ventana principal de dueña.
/// </summary>
public partial class PanelAjustes : UserControl
{
    private readonly MainViewModel _viewModel;
    private readonly List<SettingsSection> _sections;
    private SettingsSection? _current;

    /// <summary>Los ajustes sobre ese modelo de vista.</summary>
    public PanelAjustes(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // Showing a dialog belongs to the window, not to the view model.
        _viewModel.EditHotkeyRequested = ShowHotkeyEditor;
        _viewModel.EditRuleRequested = ShowRuleEditor;
        _viewModel.ConfirmRequested = Confirm;
        _viewModel.RestoreBackupRequested = ShowBackupWindow;
        _viewModel.ChooseExportFileRequested = ChooseExportFile;
        _viewModel.ChooseImportFileRequested = ChooseImportFile;

        // El orden es el de la barra lateral: lo de cada día arriba, lo de una vez y la ayuda abajo.
        _sections = new List<SettingsSection>
        {
            new("home", "Home", "", new HomePage { DataContext = viewModel }),
            new("desktops", "Desktops", "", new DesktopsPage { DataContext = viewModel }),
            new("windows", "Windows", "", new WindowsPage { DataContext = viewModel }),
            new("automations", "Automations", "", new AutomationsPage { DataContext = viewModel }),
            new("zones", "Zones", "", new ZonesPage { DataContext = viewModel }),
            new("hotkeys", "Hotkeys", "", new HotkeysPage { DataContext = viewModel }),
            new("console", "Console", "", new ConsolePage { DataContext = viewModel }),
            new("backup", "Backup", "", new BackupPage { DataContext = viewModel }),
            new("general", "General", "", new GeneralPage { DataContext = viewModel }),
            new("tools", "Tools", "", new ToolsPage { DataContext = viewModel }),
            new("help", "Help", "", new HelpPage { DataContext = viewModel })
        };

        // Mis escritorios, Automatizaciones y Distribuir ventanas ya no están aquí: desde la
        // 0.28.0-beta.10 (2026-10-10) son el grupo ESCRITORIOS de la ventana principal.
        _sections.RemoveAll(s => s.Key is "desktops" or "automations" or "zones");

        Nav.ItemsSource = _sections;
        Nav.SelectedIndex = 0;

        ApplyFlowDirection();
        Loaded += (_, _) => Loc.Instance.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>«‹ Virtual Desk Manager»: volver a la ventana principal.</summary>
    public event EventHandler? Volver;

    private void OnVolver(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
        Volver?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Vuelve a leer todos los datos, como al abrir los ajustes.</summary>
    public void Refrescar() => _viewModel.RefreshEverything();

    /// <summary>Pone el foco en el buscador (Ctrl+F).</summary>
    public void Buscar()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private Window? Dueno => Window.GetWindow(this);

    // ------------------------------------------------------------------ navegación

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is SettingsSection section)
        {
            Show(section);
        }
    }

    /// <summary>Enseña la página de la sección, arriba del todo, y deja de buscar.</summary>
    private void Show(SettingsSection section)
    {
        _current = section;
        PageTitle.Text = section.Title;
        PageIntro.Text = section.Intro;
        PageHost.Content = section.Page;

        if (SearchBox.Text.Length > 0)
        {
            SearchBox.Text = string.Empty;
        }

        ShowResults(false);
        Scroller.ScrollToTop();
    }

    /// <summary>Va a otra sección; lo usan los botones de Inicio y de Ayuda.</summary>
    public void GoTo(string key)
    {
        SettingsSection? section = _sections.FirstOrDefault(s => s.Key == key);

        if (section is null)
        {
            return;
        }

        if (Nav.SelectedItem == section)
        {
            Show(section);
        }
        else
        {
            Nav.SelectedItem = section;
        }
    }

    private void OnGoToSection(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string key)
        {
            GoTo(key);
        }
    }

    // ------------------------------------------------------------------ buscador

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text.Trim();

        if (query.Length == 0)
        {
            ShowResults(false);

            if (_current is not null)
            {
                PageTitle.Text = _current.Title;
                PageIntro.Text = _current.Intro;
            }

            return;
        }

        string[] words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<SearchHit> hits = new();

        foreach (SettingsSection section in _sections)
        {
            foreach (SettingItem item in Descendants<SettingItem>(section.Page))
            {
                if (!IsShown(item, section.Page))
                {
                    continue;
                }

                string path = PathOf(item, section);
                string text = Normalize(item.SearchText + " " + path);

                if (words.All(text.Contains))
                {
                    hits.Add(new SearchHit(item, section, path));
                }
            }
        }

        PageTitle.Text = Loc.Get("Search.Title");
        PageIntro.Text = Loc.Format("Search.Results", hits.Count, query);
        ResultList.ItemsSource = hits;
        NoResults.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowResults(true);
    }

    private void ShowResults(bool searching)
    {
        Results.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        PageHost.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnResultClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SearchHit hit)
        {
            Reveal(hit.Section, hit.Item);
        }
    }

    /// <summary>Lleva hasta el ajuste: su sección, sus grupos desplegados, a la vista y resaltado.</summary>
    private void Reveal(SettingsSection section, SettingItem item)
    {
        GoTo(section.Key);

        for (DependencyObject? parent = LogicalTreeHelper.GetParent(item); parent is not null; parent = LogicalTreeHelper.GetParent(parent))
        {
            if (parent is Expander expander)
            {
                expander.IsExpanded = true;
            }
        }

        // Después de pintar la página: antes no tiene sitio y no hay a dónde desplazarse.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            item.BringIntoView();
            item.Flash();
        }));
    }

    /// <summary>Sin mayúsculas ni acentos, para que «pestana» encuentre «pestaña».</summary>
    private static string Normalize(string text)
    {
        StringBuilder plain = new(text.Length);

        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.ToLowerInvariant(c));
            }
        }

        return plain.ToString();
    }

    /// <summary>
    /// Que se ve en su página: no está oculto él ni nada de lo que lo contiene (un apartado del
    /// bastión no sale en un equipo que se une). Lo plegado sí cuenta: se despliega al ir.
    /// </summary>
    private static bool IsShown(DependencyObject item, DependencyObject page)
    {
        for (DependencyObject? node = item; node is not null && node != page; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>«Sección › grupo», con los títulos de los grupos que lo contienen.</summary>
    private static string PathOf(DependencyObject item, SettingsSection section)
    {
        List<string> parts = new();

        for (DependencyObject? node = LogicalTreeHelper.GetParent(item); node is not null && node != section.Page; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is HeaderedContentControl { Header: string header } && header.Length > 0)
            {
                parts.Insert(0, header);
            }
        }

        parts.Insert(0, section.Title);
        return string.Join(" › ", parts);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node)
            {
                continue;
            }

            if (node is T match)
            {
                yield return match;
            }

            foreach (T inner in Descendants<T>(node))
            {
                yield return inner;
            }
        }
    }

    // ------------------------------------------------------------------ ayuda (F1)

    /// <inheritdoc/>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            ShowHelp();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = string.Empty;
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// La ayuda de lo que se tiene delante: el ajuste que está bajo el ratón o, si no, el que
    /// tiene el foco; y si no hay ninguno, la de la sección.
    /// </summary>
    private void ShowHelp()
    {
        SettingItem? item = Ancestor<SettingItem>(Mouse.DirectlyOver as DependencyObject)
                            ?? Ancestor<SettingItem>(Keyboard.FocusedElement as DependencyObject);
        SettingsSection? section = item is null ? _current : _sections.FirstOrDefault(s => IsInside(item, s.Page));

        if (section is null)
        {
            return;
        }

        if (item is not null)
        {
            HelpTitle.Text = string.IsNullOrWhiteSpace(item.Header) ? PathOf(item, section) : item.Header;
            HelpWhere.Text = PathOf(item, section);

            List<string> paragraphs = new[] { item.Description, item.Tip, item.Help }
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();

            if (paragraphs.Count == 0)
            {
                paragraphs.Add(Loc.Get("HelpPanel.NoMore"));
            }

            HelpText.ItemsSource = paragraphs;
        }
        else
        {
            HelpTitle.Text = section.Title;
            HelpWhere.Text = Loc.Get("HelpPanel.Section");
            HelpText.ItemsSource = new[] { section.Intro, section.Help, Loc.Get("HelpPanel.PointAt") }
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();
        }

        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelp(object sender, RoutedEventArgs e) => HelpPanel.Visibility = Visibility.Collapsed;

    private static bool IsInside(DependencyObject item, DependencyObject page)
    {
        for (DependencyObject? node = item; node is not null; node = LogicalTreeHelper.GetParent(node))
        {
            if (node == page)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>El primero de ese tipo que lo contiene, subiendo por lo que se ve y, si no, por lo lógico.</summary>
    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    private HotkeyDefinition? ShowHotkeyEditor(HotkeyEditorViewModel editor)
    {
        HotkeyEditorWindow dialog = new(editor, _viewModel.Logger)
        {
            Owner = Dueno,
            FlowDirection = FlowDirection
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private WindowRule? ShowRuleEditor(RuleEditorViewModel editor)
    {
        RuleEditorWindow dialog = new(editor)
        {
            Owner = Dueno,
            FlowDirection = FlowDirection
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private BackupOutcome? ShowBackupWindow(BackupViewModel backups)
    {
        BackupWindow dialog = new(backups)
        {
            Owner = Dueno,
            FlowDirection = FlowDirection
        };

        dialog.ShowDialog();
        return dialog.Outcome;
    }

    private string? ChooseExportFile(string suggestedName)
    {
        SaveFileDialog dialog = new()
        {
            FileName = suggestedName,
            DefaultExt = ConfigurationArchive.Extension,
            Filter = Loc.Get("Backup.FileFilter"),
            AddExtension = true
        };

        return dialog.ShowDialog(Dueno) == true ? dialog.FileName : null;
    }

    /// <summary>Elige el fichero y pregunta, porque importar pisa toda la configuración.</summary>
    private string? ChooseImportFile()
    {
        OpenFileDialog dialog = new()
        {
            DefaultExt = ConfigurationArchive.Extension,
            Filter = Loc.Get("Backup.FileFilter"),
            CheckFileExists = true
        };

        if (dialog.ShowDialog(Dueno) != true)
        {
            return null;
        }

        MessageBoxResult answer = MessageBox.Show(
            Dueno!,
            Loc.Instance.Format("Backup.ConfirmRestore", System.IO.Path.GetFileName(dialog.FileName)),
            Loc.Get("Backup.Title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        return answer == MessageBoxResult.OK ? dialog.FileName : null;
    }

    private bool Confirm(string question) =>
        MessageBox.Show(Dueno!, question, AppVersion.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question)
        == MessageBoxResult.Yes;

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyFlowDirection();

            foreach (SettingsSection section in _sections)
            {
                section.RefreshTexts();
            }

            // Lo de arriba de la página y la ayuda abierta se escribieron en el idioma anterior.
            HelpPanel.Visibility = Visibility.Collapsed;
            OnSearchChanged(SearchBox, null!);
        }));

    /// <summary>El árabe y el urdu dan la vuelta a todo, no sólo al texto.</summary>
    private void ApplyFlowDirection() =>
        FlowDirection = Loc.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}
