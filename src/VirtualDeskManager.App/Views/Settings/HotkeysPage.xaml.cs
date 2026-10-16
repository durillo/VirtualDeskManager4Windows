using System.Windows.Controls;
using System.Windows.Input;
using VirtualDeskManager.App.ViewModels;

namespace VirtualDeskManager.App.Views.Settings;

/// <summary>Atajos de teclado, agrupados por lo que hacen.</summary>
public partial class HotkeysPage : UserControl
{
    /// <summary>Crea la página; la ventana le pone el modelo de vista.</summary>
    public HotkeysPage() => InitializeComponent();

    /// <summary>Doble clic en un atajo: abrirlo para cambiarlo.</summary>
    private void OnHotkeysDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel model && model.EditHotkeyCommand.CanExecute(null))
        {
            model.EditHotkeyCommand.Execute(null);
        }
    }
}
