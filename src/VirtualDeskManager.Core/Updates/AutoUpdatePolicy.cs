namespace VirtualDeskManager.Core.Updates;

/// <summary>
/// Cuándo se puede reiniciar Virtual Desk Manager para ponerse al día sin molestar.
/// </summary>
/// <remarks>
/// Pedido el 2026-10-10: con «Instalar las actualizaciones solas» encendido, la aplicación se baja
/// la versión nueva en cuanto la ve y la aplica sola. Aplicarla es cerrar y volver a abrir, unos
/// segundos sin bandeja ni atajos. Por eso espera a que el usuario lleve un rato sin tocar nada.
/// </remarks>
public static class AutoUpdatePolicy
{
    /// <summary>Lo que tiene que llevar el usuario sin tocar teclado ni ratón.</summary>
    public static readonly TimeSpan IdleNeeded = TimeSpan.FromMinutes(3);

    /// <summary>Cada cuánto se busca con la instalación sola encendida (si no, una vez al día).</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(4);

    /// <summary>Si se puede reiniciar ya para aplicar una versión que está bajada.</summary>
    /// <param name="idle">Lo que lleva el usuario sin tocar nada.</param>
    public static bool CanRestartNow(TimeSpan idle) => idle >= IdleNeeded;
}
