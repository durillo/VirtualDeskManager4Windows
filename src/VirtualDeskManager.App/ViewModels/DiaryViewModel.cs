using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>
/// La pestaña del diario: lo que va pasando, en directo, para saber por qué algo no funciona sin
/// tener que buscar el fichero.
/// </summary>
/// <remarks>
/// Lee de la memoria del registrador (<see cref="FileAppLogger.Recent"/>), no del fichero. Las
/// líneas llegan de cualquier hilo y se juntan en la interfaz cada poco, para que un ssh -v
/// hablador no la atasque.
/// </remarks>
public sealed class DiaryViewModel : ObservableObject
{
    /// <summary>Las que se enseñan como mucho: más no se leen, y la lista se vuelve lenta.</summary>
    private const int Shown = 2000;

    private readonly FileAppLogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly List<string> _incoming = new();
    private readonly DispatcherTimer _flush;

    private string _filter = "";
    private bool _paused;
    private string _feedback = "";

    /// <summary>Carga lo que el registrador ya tiene en memoria y empieza a recoger lo que llegue.</summary>
    /// <param name="logger">El registrador del que se leen las líneas.</param>
    /// <param name="dispatcher">El hilo de la interfaz, donde se van juntando las líneas nuevas.</param>
    public DiaryViewModel(FileAppLogger logger, Dispatcher dispatcher)
    {
        _logger = logger;
        _dispatcher = dispatcher;

        CopyCommand = new RelayCommand(Copy);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        ClearCommand = new RelayCommand(() => Lines.Clear());

        _flush = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => Flush(), dispatcher);

        _logger.LineWritten += line =>
        {
            lock (_incoming)
            {
                _incoming.Add(line);
            }
        };

        Reload();
        _flush.Start();
    }

    /// <summary>Las líneas que se ven, ya filtradas; como mucho las últimas dos mil.</summary>
    public ObservableCollection<string> Lines { get; } = new();

    /// <summary>El botón «Copiar lo que se ve»: lleva al portapapeles las líneas de la lista, tal cual.</summary>
    public RelayCommand CopyCommand { get; }

    /// <summary>El botón «Abrir la carpeta»: abre en el Explorador la carpeta de los ficheros del diario.</summary>
    public RelayCommand OpenFolderCommand { get; }

    /// <summary>El botón «Vaciar la vista»: limpia la lista, pero no borra nada del fichero.</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>Se añadieron líneas al final: la vista baja hasta la última.</summary>
    public event EventHandler? Appended;

    /// <summary>El cuadro «Buscar»: sólo se ven las líneas que contienen este texto, sin distinguir mayúsculas.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value ?? ""))
            {
                Reload();
            }
        }
    }

    /// <summary>
    /// La casilla «Pausa»: la lista deja de moverse para poder leerla. Al quitarla se vuelve a
    /// cargar entera, con lo que llegó mientras tanto.
    /// </summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (Set(ref _paused, value) && !value)
            {
                Reload();
            }
        }
    }

    /// <summary>El aviso junto a los botones: cuántas líneas se copiaron o por qué no se pudo.</summary>
    public string Feedback
    {
        get => _feedback;
        private set => Set(ref _feedback, value);
    }

    private bool Passes(string line)
    {
        return _filter.Trim().Length == 0 || line.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void Reload()
    {
        lock (_incoming)
        {
            _incoming.Clear();
        }

        Lines.Clear();

        foreach (string line in _logger.Recent().Where(Passes).TakeLast(Shown))
        {
            Lines.Add(line);
        }

        Appended?.Invoke(this, EventArgs.Empty);
    }

    private void Flush()
    {
        List<string> nuevas;

        lock (_incoming)
        {
            if (_incoming.Count == 0)
            {
                return;
            }

            nuevas = new List<string>(_incoming);
            _incoming.Clear();
        }

        if (_paused)
        {
            return;
        }

        bool hay = false;

        foreach (string line in nuevas.Where(Passes))
        {
            Lines.Add(line);
            hay = true;
        }

        while (Lines.Count > Shown)
        {
            Lines.RemoveAt(0);
        }

        if (hay)
        {
            Appended?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Copy()
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, Lines));
            Feedback = Loc.Format("Diary.Copied", Lines.Count);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Feedback = Loc.Get("Diary.ClipboardBusy");
        }
    }

    private void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _logger.LogDirectory + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Feedback = ex.Message;
        }
    }
}
