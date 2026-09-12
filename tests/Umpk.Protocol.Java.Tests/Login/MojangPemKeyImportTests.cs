using System.Security.Cryptography;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Mojang's certificate endpoint returns PEM armor whose label does not match its BODY. The public key is an X.509 SubjectPublicKeyInfo under an "RSA PUBLIC KEY" (PKCS#1) header, and the private key is a PKCS#8 PrivateKeyInfo under an "RSA PRIVATE KEY" (PKCS#1) header. These tests build keys in those mislabeled shapes and assert signing and verification work.</summary>
public sealed class MojangPemKeyImportTests
{
    [Fact]
    public void Sign_WithMojangShapedPrivateKeyPem_Succeeds()
    {
        using RSA key = RSA.Create(2048);
        PlayerCertificates certificates = MojangShapedCertificates(key);
        var session = new ChatSigningSession(Guid.NewGuid(), Guid.NewGuid());

        byte[] signature = session.Sign(
            certificates, ChatSignatureEra.V1_19_3, "hello", DateTimeOffset.UnixEpoch, 0L, []);

        Assert.NotEmpty(signature);
    }

    [Fact]
    public void Verify_WithMojangShapedPublicKeyPem_RoundTripsASignature()
    {
        using RSA key = RSA.Create(2048);
        PlayerCertificates certificates = MojangShapedCertificates(key);
        Guid sender = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        var session = new ChatSigningSession(sender, sessionId);

        DateTimeOffset timestamp = DateTimeOffset.UnixEpoch;
        byte[] signature = session.Sign(
            certificates, ChatSignatureEra.V1_19_3, "hello", timestamp, 42L, []);

        bool verified = ChatSigningSession.Verify(
            certificates.PublicKeyPem,
            signature,
            ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(sender, sessionId, 0, "hello", timestamp, 42L, [], null));

        Assert.True(verified);
    }

    [Fact]
    public void ImportFromPem_StillFails_OnTheMislabelledArmor()
    {
        // Pins WHY the helper exists: the BCL path this replaced throws on real Mojang material.
        using RSA key = RSA.Create(2048);
        string mislabelled = MojangShapedPrivateKeyPem(key);

        using RSA rsa = RSA.Create();
        Assert.ThrowsAny<CryptographicException>(() => rsa.ImportFromPem(mislabelled));
    }

    [Fact]
    public void CorrectlyLabelledPkcs1_StillImports()
    {
        // The fallback path: a correctly-labelled PKCS#1 key from any other source must still work.
        using RSA key = RSA.Create(2048);
        string pkcs1Private = new(PemEncoding.Write("RSA PRIVATE KEY", key.ExportRSAPrivateKey()));
        string pkcs1Public = new(PemEncoding.Write("RSA PUBLIC KEY", key.ExportRSAPublicKey()));

        var certificates = new PlayerCertificates(
            pkcs1Public, pkcs1Private, string.Empty, string.Empty, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue);
        var session = new ChatSigningSession(Guid.NewGuid(), Guid.NewGuid());

        byte[] signature = session.Sign(
            certificates, ChatSignatureEra.V1_19_3, "hello", DateTimeOffset.UnixEpoch, 0L, []);

        Assert.NotEmpty(signature);
    }

    /// <summary>PKCS#8 body under the PKCS#1 "RSA PRIVATE KEY" label, exactly as Mojang returns it.</summary>
    private static string MojangShapedPrivateKeyPem(RSA key) =>
        new(PemEncoding.Write("RSA PRIVATE KEY", key.ExportPkcs8PrivateKey()));

    /// <summary>SubjectPublicKeyInfo body under the PKCS#1 "RSA PUBLIC KEY" label, as Mojang returns it.</summary>
    private static string MojangShapedPublicKeyPem(RSA key) =>
        new(PemEncoding.Write("RSA PUBLIC KEY", key.ExportSubjectPublicKeyInfo()));

    private static PlayerCertificates MojangShapedCertificates(RSA key) => new(
        MojangShapedPublicKeyPem(key),
        MojangShapedPrivateKeyPem(key),
        string.Empty,
        string.Empty,
        DateTimeOffset.MaxValue,
        DateTimeOffset.MaxValue);
}
