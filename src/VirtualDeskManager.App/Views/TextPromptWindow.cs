using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.Views;

/// <summary>Pide un texto corto (el nombre de una carpeta nueva, o el nuevo de algo que se renombra).</summary>
public sealed class TextPromptWindow : Window
{
    private readonly TextBox _box;
    private bool _ok;

    private TextPromptWindow(Window? owner, string title, string label, string initial)
    {
        Settings.FluentTheme.Apply(this);
        Title = title;
        Owner = owner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        FlowDirection = Loc.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        StackPanel todo = new() { Margin = new Thickness(24), Width = 420 };
        todo.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        _box = new TextBox { Text = initial };
        todo.Children.Add(_box);

        StackPanel botones = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        Button si = new() { Content = Loc.Get("Common.Ok"), IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 6, 16, 6) };
        si.SetResourceReference(StyleProperty, "AccentButtonStyle");
        si.Click += (_, _) =>
        {
            _ok = true;
            Close();
        };
        Button no = new() { Content = Loc.Get("Common.Cancel"), IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        botones.Children.Add(si);
        botones.Children.Add(no);
        todo.Children.Add(botones);
        Content = todo;

        Loaded += (_, _) =>
        {
            _box.Focus();

            // Al renombrar, elegido el nombre sin la extensión, como hace el Explorador.
            int punto = initial.LastIndexOf('.');
            _box.Select(0, punto > 0 ? punto : initial.Length);
        };
    }

    /// <summary>El texto escrito, o nulo si se cancela o se deja vacío.</summary>
    public static string? Ask(Window? owner, string title, string label, string initial = "")
    {
        TextPromptWindow w = new(owner, title, label, initial);
        w.ShowDialog();
        string texto = w._box.Text.Trim();
        return w._ok && texto.Length > 0 ? texto : null;
    }
}
