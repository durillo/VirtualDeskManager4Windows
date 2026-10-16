using System.Security.Cryptography;

namespace VirtualDeskManager.Core.Updates;

/// <summary>
/// Firma y comprobación de los manifiestos de actualización, con ECDSA P-256 y SHA-256.
/// </summary>
/// <remarks>
/// ECDSA y no Ed25519 porque viene dentro de .NET, sin librerías. La clave privada vive sólo en el
/// equipo de quien publica (la guarda <c>tools/Publicar</c>, cifrada para su usuario, fuera del
/// repositorio); la pública va dentro de Vdesk (<see cref="UpdateKeys"/>).
/// </remarks>
public static class UpdateSignature
{
    /// <summary>Si la firma es de esos bytes con la clave pública dada.</summary>
    /// <param name="data">Los bytes del manifiesto, tal como llegaron.</param>
    /// <param name="signatureBase64">La firma, en base64, como está en el <c>.sig</c>.</param>
    /// <param name="publicKeyBase64">La clave pública en SubjectPublicKeyInfo (DER), en base64.</param>
    public static bool Verify(byte[] data, string signatureBase64, string publicKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using ECDsa clave = ECDsa.Create();
            clave.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _);
            return clave.VerifyData(data, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // Una firma o una clave que no se pueden leer son una firma que no vale.
            return false;
        }
    }

    /// <summary>Firma esos bytes; devuelve la firma en base64. Sólo lo usa la herramienta de publicar.</summary>
    /// <param name="data">Los bytes del manifiesto.</param>
    /// <param name="privateKeyPkcs8">La clave privada en PKCS#8 (DER).</param>
    public static string Sign(byte[] data, byte[] privateKeyPkcs8)
    {
        using ECDsa clave = ECDsa.Create();
        clave.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        return Convert.ToBase64String(clave.SignData(data, HashAlgorithmName.SHA256));
    }

    /// <summary>Una pareja de claves nueva: la privada en PKCS#8 y la pública en base64.</summary>
    public static (byte[] PrivateKeyPkcs8, string PublicKeyBase64) CreateKeys()
    {
        using ECDsa clave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (clave.ExportPkcs8PrivateKey(), Convert.ToBase64String(clave.ExportSubjectPublicKeyInfo()));
    }
}
