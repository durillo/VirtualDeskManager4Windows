using System.Windows;
using VirtualDeskManager.App.ViewModels;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.App.Views;

public partial class RuleEditorWindow : Window
{
    private readonly RuleEditorViewModel _viewModel;

    /// <summary>The dialog that creates or edits a rule; the view model holds the fields and checks them.</summary>
    public RuleEditorWindow(RuleEditorViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        SaveButton.Click += OnSave;
        CancelButton.Click += (_, _) => DialogResult = false;
    }

    /// <summary>The rule the user accepted, or null when the dialog was cancelled.</summary>
    public WindowRule? Result { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        WindowRule? built = _viewModel.TryBuild();

        if (built is null)
        {
            // The view model already explains what is missing.
            return;
        }

        Result = built;
        DialogResult = true;
    }
}
