using System;
using System.Windows;
using System.Windows.Controls;
using VirtualDeskManager.App.ViewModels;

namespace VirtualDeskManager.App.Views.Settings;

/// <summary>Herramientas: el diario y los detalles técnicos.</summary>
public partial class ToolsPage : UserControl
{
    private DiaryViewModel? _diary;

    /// <summary>Crea la página; la ventana le pone el modelo de vista.</summary>
    public ToolsPage()
    {
        InitializeComponent();

        // El diario sigue a la última línea, como una consola. El modelo de vista vive más que la
        // página: mientras no se ve se suelta, para no tenerla moviéndose para nadie.
        Loaded += (_, _) => Follow((DataContext as MainViewModel)?.Diary);
        Unloaded += (_, _) => Follow(null);
    }

    private void Follow(DiaryViewModel? diary)
    {
        if (_diary is not null)
        {
            _diary.Appended -= OnAppended;
        }

        _diary = diary;

        if (_diary is not null)
        {
            _diary.Appended += OnAppended;
            OnAppended(this, EventArgs.Empty);
        }
    }

    private void OnAppended(object? sender, EventArgs e)
    {
        if (DiaryList.Items.Count > 0)
        {
            DiaryList.ScrollIntoView(DiaryList.Items[^1]);
        }
    }
}
