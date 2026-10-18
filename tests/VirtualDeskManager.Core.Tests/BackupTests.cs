using System.IO.Compression;
using VirtualDeskManager.Core.Backup;
using VirtualDeskManager.Core.Configuration;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>Una carpeta de configuración de usar y tirar.</summary>
public sealed class TemporaryConfiguration : IDisposable
{
    public TemporaryConfiguration()
    {
        Root = Path.Combine(Path.GetTempPath(), "vdesk-tests", Guid.NewGuid().ToString("N"));
        Paths = new ConfigurationPaths(Root);
        Paths.EnsureCreated();
    }

    public string Root { get; }

    public ConfigurationPaths Paths { get; }

    public void Write(string settings = "{}", string hotkeys = "{}", string rules = "{}")
    {
        File.WriteAllText(Paths.Settings, settings);
        File.WriteAllText(Paths.Hotkeys, hotkeys);
        File.WriteAllText(Paths.Rules, rules);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public class ConfigurationArchiveTests
{
    [Fact]
    public void An_archive_puts_the_three_files_back_exactly_as_they_were()
    {
        using TemporaryConfiguration source = new();
        using TemporaryConfiguration target = new();

        source.Write(settings: "{\"a\":1}", hotkeys: "{\"b\":2}", rules: "{\"c\":3}");

        string archive = Path.Combine(source.Root, "copy.vdesk");
        ConfigurationArchive.Write(source.Paths, archive, "test", DateTimeOffset.Now, "1.2.3");

        Assert.Equal(3, ConfigurationArchive.Restore(archive, target.Paths));
        Assert.Equal("{\"a\":1}", File.ReadAllText(target.Paths.Settings));
        Assert.Equal("{\"b\":2}", File.ReadAllText(target.Paths.Hotkeys));
        Assert.Equal("{\"c\":3}", File.ReadAllText(target.Paths.Rules));
    }

    [Fact]
    public void The_manifest_says_when_it_was_taken_and_by_which_version()
    {
        using TemporaryConfiguration source = new();
        source.Write();

        DateTimeOffset taken = new(2026, 3, 4, 10, 30, 0, TimeSpan.Zero);
        string archive = Path.Combine(source.Root, "copy.vdesk");
        ConfigurationArchive.Write(source.Paths, archive, "manual", taken, "0.15.0");

        BackupManifest? manifest = ConfigurationArchive.ReadManifest(archive);

        Assert.NotNull(manifest);
        Assert.Equal(taken, manifest!.Taken);
        Assert.Equal("manual", manifest.Origin);
        Assert.Equal("0.15.0", manifest.Application);
        Assert.Equal(Environment.MachineName, manifest.Machine);
    }

    /// <summary>
    /// Un .vdesk puede venir de una carpeta compartida o de una nube, así que no se extrae lo que
    /// venga dentro: sólo los tres nombres conocidos, comparados exactamente.
    /// </summary>
    [Fact]
    public void An_entry_that_escapes_the_folder_is_not_extracted()
    {
        using TemporaryConfiguration target = new();

        string archive = Path.Combine(target.Root, "malicious.vdesk");
        string escaped = Path.Combine(target.Root, "escaped.json");

        using (FileStream stream = File.Create(archive))
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
        {
            using (StreamWriter writer = new(zip.CreateEntry("../../escaped.json").Open()))
            {
                writer.Write("owned");
            }

            using (StreamWriter writer = new(zip.CreateEntry("settings.json").Open()))
            {
                writer.Write("{\"ok\":true}");
            }
        }

        Assert.Equal(1, ConfigurationArchive.Restore(archive, target.Paths));
        Assert.Equal("{\"ok\":true}", File.ReadAllText(target.Paths.Settings));
        Assert.False(File.Exists(escaped));
        Assert.False(File.Exists(Path.Combine(target.Root, "..", "..", "escaped.json")));
    }

    [Fact]
    public void An_archive_without_any_configuration_restores_nothing()
    {
        using TemporaryConfiguration target = new();

        string archive = Path.Combine(target.Root, "empty.vdesk");

        using (FileStream stream = File.Create(archive))
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        Assert.Equal(0, ConfigurationArchive.Restore(archive, target.Paths));
    }

    [Fact]
    public void The_hash_only_changes_when_the_configuration_changes()
    {
        using TemporaryConfiguration source = new();
        source.Write();

        string before = ConfigurationArchive.Hash(source.Paths);
        Assert.Equal(before, ConfigurationArchive.Hash(source.Paths));

        File.WriteAllText(source.Paths.Rules, "{\"changed\":true}");

        Assert.NotEqual(before, ConfigurationArchive.Hash(source.Paths));
    }
}

public class ConfigurationBackupServiceTests
{
    private static (ConfigurationBackupService Service, BackupSettings Settings) Build(
        TemporaryConfiguration configuration,
        Func<DateTimeOffset> clock)
    {
        BackupSettings settings = new();

        ConfigurationBackupService service = new(
            configuration.Paths,
            () => settings,
            "1.0.0",
            locator: new CloudFolderLocator(_ => null, _ => false, Array.Empty<string>),
            clock: clock);

        return (service, settings);
    }

    [Fact]
    public void The_state_before_a_change_is_what_gets_kept()
    {
        using TemporaryConfiguration configuration = new();
        configuration.Write(rules: "{\"version\":\"old\"}");

        DateTimeOffset now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        (ConfigurationBackupService service, _) = Build(configuration, () => now);

        service.BeforeChange();
        File.WriteAllText(configuration.Paths.Rules, "{\"version\":\"new\"}");

        BackupSnapshot snapshot = Assert.Single(service.Snapshots());

        using TemporaryConfiguration restored = new();
        ConfigurationArchive.Restore(snapshot.Path, restored.Paths);

        Assert.Equal("{\"version\":\"old\"}", File.ReadAllText(restored.Paths.Rules));
    }

    /// <summary>
    /// Marcar cinco casillas seguidas son cinco escrituras. Lo que interesa guardar es el estado
    /// de antes de empezar, no cinco pasos intermedios de los últimos veinte segundos.
    /// </summary>
    [Fact]
    public void A_burst_of_changes_only_leaves_one_copy()
    {
        using TemporaryConfiguration configuration = new();
        configuration.Write();

        DateTimeOffset now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        (ConfigurationBackupService service, _) = Build(configuration, () => now);

        for (int i = 0; i < 5; i++)
        {
            service.BeforeChange();
            File.WriteAllText(configuration.Paths.Settings, $"{{\"click\":{i}}}");
            now = now.AddSeconds(4);
        }

        Assert.Single(service.Snapshots());
    }

    [Fact]
    public void A_change_long_after_the_last_copy_gets_its_own()
    {
        using TemporaryConfiguration configuration = new();
        configuration.Write();

        DateTimeOffset now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        (ConfigurationBackupService service, _) = Build(configuration, () => now);

        service.BeforeChange();
        File.WriteAllText(configuration.Paths.Settings, "{\"morning\":true}");

        now = now.AddHours(3);

        service.BeforeChange();

        Assert.Equal(2, service.Snapshots().Count);
    }

    [Fact]
    public void Nothing_is_kept_when_the_configuration_did_not_actually_change()
    {
        using TemporaryConfiguration configuration = new();
        configuration.Write();

        DateTimeOffset now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        (ConfigurationBackupService service, _) = Build(configuration, () => now);

        service.BeforeChange();

        now = now.AddHours(1);
        service.BeforeChange();

        Assert.Single(service.Snapshots());
    }

    [Fact]
    public void Turning_the_history_off_stops_it()
    {
        using TemporaryConfiguration configuration = new();
        configuration.Write();

        DateTimeOffset now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        (ConfigurationBackupService service, BackupSettings settings) = Build(configuration, () => now);

        settings.AutomaticHistory = false;
        service.BeforeChange();

        Assert.Empty(service.Snapshots());
    }

    [Fact]
    public void Only_the_last_copies_are_kept()
    {
        using TemporaryConfiguration configuration = new();
        configuration.Write();

        DateTimeOffset now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        (ConfigurationBackupService service, BackupSettings settings) = Build(configuration, () => now);
        settings.HistoryLimit = 3;

        for (int i = 0; i < 6; i++)
        {
            File.WriteAllText(configuration.Paths.Settings, $"{{\"step\":{i}}}");
            service.BeforeChange();
            now = now.AddMinutes(10);
        }

        Assert.Equal(3, service.Snapshots().Count);
    }

    /// <summary>
    /// Restaurar también se puede hacer sobre el fichero equivocado, así que lo que había queda
    /// guardado y el usuario puede volver.
    /// </summary>
    [Fact]
    public void Restoring_keeps_a_copy_of_what_was_there_first()
    {
        using TemporaryConfiguration configuration = new();
        configuration.Write(rules: "{\"mine\":true}");

        DateTimeOffset now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        (ConfigurationBackupService service, _) = Build(configuration, () => now);

        string other = Path.Combine(configuration.Root, "other.vdesk");

        using (TemporaryConfiguration elsewhere = new())
        {
            elsewhere.Write(rules: "{\"theirs\":true}");
            ConfigurationArchive.Write(elsewhere.Paths, other, "test", now, "1.0.0");
        }

        BackupOutcome outcome = service.RestoreFrom(other);

        Assert.True(outcome.Ok);
        Assert.Equal("{\"theirs\":true}", File.ReadAllText(configuration.Paths.Rules));

        BackupSnapshot safety = Assert.Single(service.Snapshots());
        Assert.Equal("before-restore", safety.Origin);

        using TemporaryConfiguration check = new();
        ConfigurationArchive.Restore(safety.Path, check.Paths);
        Assert.Equal("{\"mine\":true}", File.ReadAllText(check.Paths.Rules));
    }

    [Fact]
    public void Restoring_a_file_that_is_not_there_says_so_instead_of_throwing()
    {
        using TemporaryConfiguration configuration = new();
        configuration.Write();

        (ConfigurationBackupService service, _) = Build(configuration, () => DateTimeOffset.Now);

        BackupOutcome outcome = service.RestoreFrom(Path.Combine(configuration.Root, "nope.vdesk"));

        Assert.False(outcome.Ok);
    }

    [Fact]
    public void A_fresh_installation_has_nothing_to_keep()
    {
        using TemporaryConfiguration configuration = new();

        (ConfigurationBackupService service, _) = Build(configuration, () => DateTimeOffset.Now);

        service.BeforeChange();

        Assert.Empty(service.Snapshots());
    }
}

public class CloudFolderLocatorTests
{
    private static CloudFolderLocator Build(
        Dictionary<string, string> environment,
        params string[] existingDirectories)
    {
        HashSet<string> exists = new(existingDirectories, StringComparer.OrdinalIgnoreCase);

        return new CloudFolderLocator(
            name => environment.TryGetValue(name, out string? value) ? value : null,
            path => exists.Contains(path),
            () => new[] { @"C:\", @"G:\" });
    }

    [Fact]
    public void OneDrive_is_found_through_its_environment_variable()
    {
        CloudFolderLocator locator = Build(
            new Dictionary<string, string> { ["OneDrive"] = @"C:\Users\Someone\OneDrive" },
            @"C:\Users\Someone\OneDrive");

        CloudFolder folder = Assert.Single(locator.Find());

        Assert.Equal(CloudProvider.OneDrive, folder.Provider);
        Assert.Equal(@"C:\Users\Someone\OneDrive", folder.Root);
    }

    [Fact]
    public void A_personal_and_a_work_account_are_two_different_folders()
    {
        CloudFolderLocator locator = Build(
            new Dictionary<string, string>
            {
                ["OneDrive"] = @"C:\Users\Someone\OneDrive - Work",
                ["OneDriveCommercial"] = @"C:\Users\Someone\OneDrive - Work",
                ["OneDriveConsumer"] = @"C:\Users\Someone\OneDrive"
            },
            @"C:\Users\Someone\OneDrive - Work",
            @"C:\Users\Someone\OneDrive");

        Assert.Equal(2, locator.Find().Count);
    }

    [Fact]
    public void A_variable_pointing_at_a_folder_that_is_gone_is_ignored()
    {
        CloudFolderLocator locator = Build(
            new Dictionary<string, string> { ["OneDrive"] = @"C:\Users\Someone\OneDrive" });

        Assert.Empty(locator.Find());
    }

    /// <summary>
    /// Drive para escritorio monta una unidad, normalmente la G. Sólo se miran las unidades cuando
    /// el cliente está instalado: recorrerlas siempre es tocar unidades de red por nada.
    /// </summary>
    [Fact]
    public void The_google_drive_unit_is_found_when_the_client_is_installed()
    {
        CloudFolderLocator locator = Build(
            new Dictionary<string, string>
            {
                ["LOCALAPPDATA"] = @"C:\Users\Someone\AppData\Local",
                ["USERPROFILE"] = @"C:\Users\Someone"
            },
            @"C:\Users\Someone\AppData\Local\Google\DriveFS",
            @"G:\Mi unidad");

        CloudFolder folder = Assert.Single(locator.Find());

        Assert.Equal(CloudProvider.GoogleDrive, folder.Provider);
        Assert.Equal(@"G:\Mi unidad", folder.Root);
    }

    [Fact]
    public void Without_the_client_the_units_are_not_even_looked_at()
    {
        CloudFolderLocator locator = Build(
            new Dictionary<string, string> { ["USERPROFILE"] = @"C:\Users\Someone" },
            @"G:\Mi unidad");

        Assert.Empty(locator.Find());
    }

    [Fact]
    public void The_backup_lands_in_its_own_subfolder_and_not_loose_in_the_cloud()
    {
        CloudFolder folder = new(CloudProvider.OneDrive, @"C:\Users\Someone\OneDrive");

        Assert.Equal(@"C:\Users\Someone\OneDrive\VirtualDeskManager", folder.Directory);
        Assert.EndsWith("VirtualDeskManager.vdesk", folder.File);
    }
}
