using System.Windows;
using VirtualDeskManager.App.ViewModels;
using VirtualDeskManager.Core.Backup;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.Views;

public partial class BackupWindow : Window
{
    private readonly BackupViewModel _viewModel;

    /// <summary>La lista de copias de la configuración, para restaurar o borrar una.</summary>
    public BackupWindow(BackupViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        RestoreButton.Click += OnRestore;
        DeleteButton.Click += OnDelete;
        CloseButton.Click += (_, _) => Close();
    }

    /// <summary>Lo que hay que contarle al usuario al volver, y si hay que recargar.</summary>
    public BackupOutcome? Outcome { get; private set; }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        BackupSnapshotRow? selected = _viewModel.Selected;

        if (selected is null)
        {
            return;
        }

        // Restaurar pisa las reglas, los atajos y los ajustes de golpe, así que se pregunta. Y se
        // dice en la propia pregunta que es reversible, que es lo que hace que uno se atreva.
        MessageBoxResult answer = MessageBox.Show(
            this,
            Loc.Instance.Format("Backup.ConfirmRestore", selected.Taken),
            Loc.Get("Backup.Title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        Outcome = _viewModel.Restore();
        DialogResult = Outcome.Ok;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Selected is null)
        {
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            Loc.Get("Backup.ConfirmDelete"),
            Loc.Get("Backup.Title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.OK)
        {
            _viewModel.Delete();
        }
    }
}
