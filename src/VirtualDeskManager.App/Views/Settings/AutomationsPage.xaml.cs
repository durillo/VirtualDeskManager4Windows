using System.Windows.Controls;
using System.Windows.Input;
using VirtualDeskManager.App.ViewModels;

namespace VirtualDeskManager.App.Views.Settings;

/// <summary>Automatizaciones: las reglas, leídas como «cuando… hacer…».</summary>
public partial class AutomationsPage : UserControl
{
    /// <summary>Crea la página; la ventana le pone el modelo de vista.</summary>
    public AutomationsPage() => InitializeComponent();

    /// <summary>Doble clic en una automatización: abrirla para editar.</summary>
    private void OnRulesDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel model && model.EditRuleCommand.CanExecute(null))
        {
            model.EditRuleCommand.Execute(null);
        }
    }
}
