using VirtualDeskManager.Core.Abstractions;

namespace VirtualDeskManager.Core.Zones;

/// <summary>
/// Quita el filo y el redondeo de las ventanas encajadas, y se acuerda de cómo estaban.
/// </summary>
/// <remarks>
/// Windows 11 dibuja dos cosas alrededor de cada ventana que hacen que dos ventanas pegadas al
/// píxel sigan sin parecerlo:
///
/// <list type="number">
/// <item>Las <b>esquinas redondeadas</b>, por las que se ve el escritorio arriba y abajo de la
/// junta. Medido en el equipo del usuario: Visual Studio Code pide redondeo grande y Teams se deja
/// el que Windows quiera.</item>
/// <item>El <b>filo de un píxel</b> del borde. Medido en la junta de dos ventanas encajadas, los
/// píxeles salían en #7C7D7B y #707270 sobre un contenido de #121314: una raya gris clara de dos
/// píxeles, uno por ventana, sobre fondo oscuro. Al quitarlo pasaron a #202020 y #181818,
/// indistinguibles del contenido.</item>
/// </list>
///
/// Ninguna de las dos es un hueco, pero las dos se ven como un hueco, que para el caso es lo mismo.
///
/// Se guarda lo que cada ventana tenía y se le devuelve al apagar esto o al cerrar la aplicación.
/// Cambiarle el aspecto a la ventana de otro programa y no deshacerlo sería dejar rastro en un
/// sitio donde no nos han invitado.
/// </remarks>
public sealed class WindowEdgeKeeper
{
    private readonly IWindowManager _windows;
    private readonly Dictionary<nint, int> _originalCorner = new();
    private readonly object _gate = new();

    /// <summary>Crea el guardián sobre el gestor de ventanas, que es quien cambia los bordes de verdad.</summary>
    public WindowEdgeKeeper(IWindowManager windows) => _windows = windows;

    /// <summary>Cuántas ventanas tienen ahora los bordes cambiados por nosotros.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _originalCorner.Count;
            }
        }
    }

    /// <summary>Deja la ventana sin redondeo y sin filo, para que se pegue a su vecina.</summary>
    public void MakeFlush(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_originalCorner.ContainsKey(hwnd))
            {
                // Ya es nuestra: volver a guardar lo de ahora grabaria el cuadrado como si fuera
                // lo que tenia el usuario, y entonces ya no habria a que volver.
                return;
            }

            int? before = _windows.GetCornerPreference(hwnd);

            if (before is not int corner)
            {
                return;
            }

            if (!_windows.SetSquareCorners(hwnd, true).IsSuccess)
            {
                return;
            }

            // El filo no se puede consultar, solo poner, asi que del suyo no hay nada que guardar:
            // se repone al valor por defecto de Windows, que es lo que tenia cualquier ventana que
            // no se lo haya cambiado a mano.
            _windows.SetBorderVisible(hwnd, false);

            _originalCorner[hwnd] = corner;
        }
    }

    /// <summary>Le devuelve a una ventana los bordes que tenía.</summary>
    public void Restore(nint hwnd)
    {
        lock (_gate)
        {
            if (_originalCorner.Remove(hwnd, out int corner))
            {
                _windows.SetCornerPreference(hwnd, corner);
                _windows.SetBorderVisible(hwnd, true);
            }
        }
    }

    /// <summary>Se los devuelve a todas. Al apagar la opción y al cerrar la aplicación.</summary>
    public void RestoreAll()
    {
        lock (_gate)
        {
            foreach ((nint hwnd, int corner) in _originalCorner)
            {
                _windows.SetCornerPreference(hwnd, corner);
                _windows.SetBorderVisible(hwnd, true);
            }

            _originalCorner.Clear();
        }
    }
}
