using System;
using System.Globalization;
using VirtualDeskManager.Core.Backup;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>Una carpeta de nube en el desplegable de ajustes, más la opción de no usar ninguna.</summary>
public sealed class CloudFolderOption
{
    private CloudFolderOption(CloudFolder? folder, string display)
    {
        Folder = folder;
        Display = display;
    }

    /// <summary>La carpeta de nube; <c>null</c> en la opción de no usar ninguna.</summary>
    public CloudFolder? Folder { get; }

    /// <summary>Lo que se lee en el desplegable.</summary>
    public string Display { get; }

    /// <summary>La ruta guardada en settings.json, vacía cuando no hay carpeta.</summary>
    public string Root => Folder?.Root ?? "";

    /// <summary>La opción de no copiar a ninguna nube.</summary>
    public static CloudFolderOption None() => new(null, Loc.Get("Settings.BackupNone"));

    /// <summary>La opción de una carpeta encontrada, con su proveedor y su ruta entera.</summary>
    public static CloudFolderOption For(CloudFolder folder)
    {
        // El nombre del proveedor y la ruta entera: con dos cuentas de OneDrive, la personal y la
        // de la empresa, "OneDrive" a secas no distingue una de la otra.
        string provider = folder.Provider == CloudProvider.OneDrive ? "OneDrive" : "Google Drive";

        return new CloudFolderOption(folder, provider + "  -  " + folder.Root);
    }
}

/// <summary>Una copia guardada, tal y como se lee en la lista de restaurar.</summary>
public sealed class BackupSnapshotRow
{
    /// <summary>La fila de una copia.</summary>
    public BackupSnapshotRow(BackupSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    /// <summary>La copia que representa la fila.</summary>
    public BackupSnapshot Snapshot { get; }

    /// <summary>La columna «Hecha»: fecha y hora de la copia, en hora local.</summary>
    public string Taken => Snapshot.Taken.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// La columna «Motivo»: antes de un cambio, a petición, antes de restaurar o exportada. Un
    /// motivo desconocido se enseña tal cual.
    /// </summary>
    public string Origin => Snapshot.Origin switch
    {
        "automatic" => Loc.Get("Backup.OriginAutomatic"),
        "manual" => Loc.Get("Backup.OriginManual"),
        "before-restore" => Loc.Get("Backup.OriginBeforeRestore"),
        "export" => Loc.Get("Backup.OriginExport"),
        _ => Snapshot.Origin
    };

    /// <summary>La columna «Equipo»: el que hizo la copia.</summary>
    public string Machine => Snapshot.Machine;

    /// <summary>La columna «Dónde»: «Este equipo» o «Nube».</summary>
    public string Where => Snapshot.IsCloud ? Loc.Get("Backup.WhereCloud") : Loc.Get("Backup.WhereLocal");

    /// <summary>En KB: son ocho, y decirlo en bytes sólo obliga a contar ceros.</summary>
    public string Size => Math.Max(1, (Snapshot.Bytes + 512) / 1024).ToString(CultureInfo.CurrentCulture) + " KB";
}
