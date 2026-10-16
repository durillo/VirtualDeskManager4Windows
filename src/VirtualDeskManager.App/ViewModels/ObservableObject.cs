using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>Base de los modelos de vista: avisa a la interfaz de cada propiedad que cambia.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <summary>Cambió una propiedad; WPF lo escucha para repintar lo que está atado a ella.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Avisa de que cambió una propiedad, aunque no haya pasado por <see cref="Set{T}"/>.</summary>
    /// <param name="propertyName">La propiedad; si no se da, la que llama.</param>
    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Guarda el valor en el campo y avisa, sólo si es distinto del que había.</summary>
    /// <param name="field">El campo que respalda la propiedad.</param>
    /// <param name="value">El valor nuevo.</param>
    /// <param name="propertyName">La propiedad; si no se da, la que llama.</param>
    /// <returns><c>true</c> si cambió; así quien llama sabe si tiene que hacer algo más.</returns>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }
}

/// <summary>Un comando para los botones y menús de la interfaz, sin parámetro.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>Crea el comando.</summary>
    /// <param name="execute">Lo que hace al pulsarlo.</param>
    /// <param name="canExecute">Cuándo se puede pulsar; sin él, siempre.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>
    /// Puede haber cambiado si se puede pulsar. Sólo salta con <see cref="RaiseCanExecuteChanged"/>:
    /// no se engancha a <c>CommandManager</c>.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>Si se puede pulsar ahora. Se ignora el parámetro.</summary>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <summary>Hace lo del comando. Se ignora el parámetro.</summary>
    public void Execute(object? parameter) => _execute();

    /// <summary>Pide a la interfaz que vuelva a mirar si el botón se puede pulsar.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
