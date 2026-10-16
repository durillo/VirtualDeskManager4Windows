using System.Collections.ObjectModel;
using System.Linq;
using VirtualDeskManager.Core.Backup;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>La lista de copias de la ventana de restaurar.</summary>
public sealed class BackupViewModel : ObservableObject
{
    private readonly ConfigurationBackupService _backups;
    private BackupSnapshotRow? _selected;

    /// <summary>Crea la lista y la llena con las copias que hay ahora.</summary>
    public BackupViewModel(ConfigurationBackupService backups)
    {
        _backups = backups;
        Refresh();
    }

    /// <summary>Las copias que hay, locales y de la nube.</summary>
    public ObservableCollection<BackupSnapshotRow> Snapshots { get; } = new();

    /// <summary>La copia elegida en la lista; de ella dependen los botones de restaurar y borrar.</summary>
    public BackupSnapshotRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Raise(nameof(HasSelection));
                Raise(nameof(CanDelete));
            }
        }
    }

    /// <summary>Hay alguna copia; si no, se enseña «Todavía no hay ninguna copia.».</summary>
    public bool HasSnapshots => Snapshots.Count > 0;

    /// <summary>Hay una copia elegida, y por tanto se puede restaurar.</summary>
    public bool HasSelection => _selected is not null;

    /// <summary>
    /// La copia de la nube no se borra desde aquí.
    /// </summary>
    /// <remarks>
    /// Es el mismo fichero en todos los equipos que comparten la carpeta, así que borrarlo desde
    /// uno se lo quita a los demás. Quien quiera hacerlo puede, pero desde su carpeta, donde está
    /// claro lo que está borrando.
    /// </remarks>
    public bool CanDelete => _selected is not null && !_selected.Snapshot.IsCloud;

    /// <summary>Vuelve a leer las copias y conserva la elegida si sigue estando.</summary>
    public void Refresh()
    {
        string? selectedPath = _selected?.Snapshot.Path;

        Snapshots.Clear();

        foreach (BackupSnapshot snapshot in _backups.Snapshots())
        {
            Snapshots.Add(new BackupSnapshotRow(snapshot));
        }

        Selected = Snapshots.FirstOrDefault(row => row.Snapshot.Path == selectedPath);
        Raise(nameof(HasSnapshots));
    }

    /// <summary>Restaura la copia elegida; sin ninguna elegida no hace nada y lo devuelve como fallo sin mensaje.</summary>
    public BackupOutcome Restore() =>
        _selected is null
            ? new BackupOutcome(false, "")
            : _backups.RestoreFrom(_selected.Snapshot.Path);

    /// <summary>Borra la copia elegida y vuelve a leer la lista; sin ninguna elegida no hace nada.</summary>
    public BackupOutcome Delete()
    {
        if (_selected is null)
        {
            return new BackupOutcome(false, "");
        }

        BackupOutcome outcome = _backups.Delete(_selected.Snapshot);
        Refresh();
        return outcome;
    }
}
