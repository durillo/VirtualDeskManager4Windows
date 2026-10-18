using VirtualDeskManager.Core.Updates;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// La actualización por internet (2026-10-09): qué versión es más nueva, que nada sin firma valga,
/// que sólo se baje lo que cambia y que nada se escriba fuera de la carpeta de la aplicación.
/// </summary>
public class UpdateTests
{
    // ------------------------------------------------------------------ versiones

    [Theory]
    [InlineData("0.24.0-beta.1", "0.24.0-beta.2")]
    [InlineData("0.24.0-beta.9", "0.24.0-beta.10")]
    [InlineData("0.24.0-beta.2", "0.24.0")]
    [InlineData("0.24.0", "0.24.1-beta.1")]
    [InlineData("0.23.1", "0.24.0")]
    [InlineData("0.9.0", "0.10.0")]
    public void Versions_are_ordered_as_a_person_expects(string vieja, string nueva) =>
        Assert.True(UpdateVersion.Parse(nueva)!.IsNewerThan(UpdateVersion.Parse(vieja)!));

    [Fact]
    public void The_fourth_number_does_not_count() =>
        Assert.Equal(0, UpdateVersion.Parse("0.24.0.0")!.CompareTo(UpdateVersion.Parse("0.24.0")));

    [Theory]
    [InlineData("")]
    [InlineData("hola")]
    [InlineData("0.24.0-alfa")]
    public void What_is_not_a_version_is_not_read(string texto) =>
        Assert.Null(UpdateVersion.Parse(texto));

    // ------------------------------------------------------------------ firma

    private static UpdateManifest Ejemplo() => new(
        "0.24.0",
        "stable",
        new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc),
        "Notas",
        new UpdateFile("versiones/0.24.0/VirtualDeskManager-0.24.0.msi", new string('a', 64), 100),
        new[] { new UpdateFile("VirtualDeskManager.exe", new string('b', 64), 10) });

    [Fact]
    public void A_signed_manifest_is_believed()
    {
        var (privada, publica) = UpdateSignature.CreateKeys();
        byte[] json = Ejemplo().ToJson();

        Assert.True(UpdateSignature.Verify(json, UpdateSignature.Sign(json, privada), publica));
        Assert.Equal(Ejemplo().Version, UpdateManifest.Parse(json).Version);
    }

    /// <summary>Quien entre en el servidor puede cambiar el manifiesto, pero no firmarlo.</summary>
    [Fact]
    public void A_manifest_changed_by_one_byte_is_not_believed()
    {
        var (privada, publica) = UpdateSignature.CreateKeys();
        byte[] json = Ejemplo().ToJson();
        string firma = UpdateSignature.Sign(json, privada);

        json[^5] ^= 1;

        Assert.False(UpdateSignature.Verify(json, firma, publica));
    }

    [Fact]
    public void A_manifest_signed_with_another_key_is_not_believed()
    {
        var (otraPrivada, _) = UpdateSignature.CreateKeys();
        byte[] json = Ejemplo().ToJson();

        Assert.False(UpdateSignature.Verify(json, UpdateSignature.Sign(json, otraPrivada), UpdateKeys.PublicKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es base64")]
    public void A_signature_that_cannot_be_read_is_not_believed(string firma) =>
        Assert.False(UpdateSignature.Verify(Ejemplo().ToJson(), firma, UpdateKeys.PublicKey));

    [Fact]
    public void The_built_in_public_key_can_be_read()
    {
        using System.Security.Cryptography.ECDsa clave = System.Security.Cryptography.ECDsa.Create();
        clave.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdateKeys.PublicKey), out _);
        Assert.Equal(256, clave.KeySize);
    }

    // ------------------------------------------------------------------ qué se baja

    [Fact]
    public void Only_what_is_missing_or_changed_is_downloaded()
    {
        string a = new('a', 64), b = new('b', 64), c = new('c', 64);
        UpdateManifest m = Ejemplo() with
        {
            Files = new[]
            {
                new UpdateFile("igual.dll", a, 1),
                new UpdateFile("cambiado.dll", b, 1),
                new UpdateFile("nuevo/fichero.dll", c, 1)
            }
        };

        Dictionary<string, string> aqui = new() { ["igual.dll"] = a.ToUpperInvariant(), ["cambiado.dll"] = c };

        IReadOnlyList<UpdateFile> faltan = UpdatePlan.FilesToDownload(m, ruta => aqui.GetValueOrDefault(ruta));

        Assert.Equal(new[] { "cambiado.dll", "nuevo/fichero.dll" }, faltan.Select(f => f.Path));
    }

    /// <summary>Aunque venga firmado, nada se escribe fuera de la carpeta de la aplicación.</summary>
    [Theory]
    [InlineData("../fuera.dll")]
    [InlineData("runtimes/../../fuera.dll")]
    [InlineData("C:/Windows/System32/x.dll")]
    [InlineData("/raiz.dll")]
    [InlineData("\\\\servidor\\x.dll")]
    [InlineData("")]
    public void Paths_outside_the_application_are_refused(string ruta)
    {
        UpdateManifest m = Ejemplo() with { Files = new[] { new UpdateFile(ruta, new string('a', 64), 1) } };
        Assert.Throws<InvalidDataException>(() => UpdatePlan.FilesToDownload(m, _ => null));
    }

    [Theory]
    [InlineData("VirtualDeskManager.exe")]
    [InlineData("runtimes/win-x64/native/x.dll")]
    [InlineData("es/VirtualDeskManager.resources.dll")]
    public void Normal_paths_are_accepted(string ruta) =>
        Assert.True(UpdatePlan.IsSafePath(ruta));

    // ------------------------------------------------------------------ direcciones

    /// <summary>La ruta va en base64url y sin relleno; d.php la descodifica igual.</summary>
    [Fact]
    public void Addresses_go_through_the_gate_with_the_path_encoded()
    {
        Uri url = UpdateKeys.UrlFor("https://durillo.es/updates/", "stable.json");

        Assert.Equal("https://durillo.es/updates/d.php?f=c3RhYmxlLmpzb24", url.ToString());

        string f = url.Query["?f=".Length..].Replace('-', '+').Replace('_', '/');
        f += new string('=', (4 - f.Length % 4) % 4);
        Assert.Equal("stable.json", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(f)));
    }
}
