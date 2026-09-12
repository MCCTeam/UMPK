using System.Security.Cryptography;

namespace Umpk.Protocol.Java.Signing;

/// <summary>Imports the RSA keys Mojang issues for chat signing.</summary>
/// <remarks>Mojang's certificate endpoint MISLABELS both PEM armors: the bodies carry X.509 <c>SubjectPublicKeyInfo</c> and PKCS#8 <c>PrivateKeyInfo</c> DER, but the armor says <c>RSA PUBLIC KEY</c> / <c>RSA PRIVATE KEY</c>, which are the PKCS#1 labels. <see cref="RSA.ImportFromPem(ReadOnlySpan{char})"/> dispatches on the LABEL, so it selects the PKCS#1 parsers and throws <c>CryptographicException: ASN1 corrupted data</c> on Mojang-issued material. These helpers therefore ignore the label and try the DER shapes Mojang actually sends first, falling back to the PKCS#1 shapes so a correctly-labelled key from any other source still works.</remarks>
internal static class RsaPemKeys
{
    /// <summary>Imports a private key PEM (PKCS#8 first, then PKCS#1), ignoring a mislabelled armor.</summary>
    public static RSA ImportPrivateKey(string pem)
    {
        ArgumentNullException.ThrowIfNull(pem);
        byte[] der = DecodePemBody(pem);
        RSA rsa = RSA.Create();
        try
        {
            try
            {
                rsa.ImportPkcs8PrivateKey(der, out _);
            }
            catch (CryptographicException)
            {
                rsa.ImportRSAPrivateKey(der, out _);
            }

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    /// <summary>Imports a public key PEM (SubjectPublicKeyInfo first, then PKCS#1), ignoring a mislabelled armor.</summary>
    public static RSA ImportPublicKey(string pem)
    {
        ArgumentNullException.ThrowIfNull(pem);
        byte[] der = DecodePemBody(pem);
        RSA rsa = RSA.Create();
        try
        {
            try
            {
                rsa.ImportSubjectPublicKeyInfo(der, out _);
            }
            catch (CryptographicException)
            {
                rsa.ImportRSAPublicKey(der, out _);
            }

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    /// <summary>Returns the DER body of a PEM, or the whole payload base64-decoded when there is no armor. Mirrors <c>ProfileKeyMaterial.DecodePemBody</c>: the body is taken verbatim so the bytes stay exactly what Mojang signed.</summary>
    private static byte[] DecodePemBody(string pem)
    {
        ReadOnlySpan<char> span = pem;
        if (PemEncoding.TryFind(span, out PemFields fields))
        {
            // Convert.FromBase64String ignores the embedded line breaks in the PEM base64 body.
            return Convert.FromBase64String(span[fields.Base64Data].ToString());
        }

        return Convert.FromBase64String(pem.Trim());
    }
}
