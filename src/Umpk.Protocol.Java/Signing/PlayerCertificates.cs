namespace Umpk.Protocol.Java.Signing;

/// <summary>A player's profile key material as returned by the Minecraft-services certificates endpoint: an RSA key pair (PEM) plus Mojang's signatures over the public key and the key validity window. This is immutable input to the chat-signing session (<c>ChatSigningSession</c>); the mutable per-session chain state lives in the session, not here. Declared in <c>Umpk.Protocol.Java</c> and populated by <c>Umpk.Auth</c>, keeping the dependency edge one-way.</summary>
/// <remarks><see cref="PrivateKeyPem"/> is secret and is excluded from <see cref="ToString"/>.</remarks>
public sealed record PlayerCertificates(
    string PublicKeyPem,
    string PrivateKeyPem,
    string PublicKeySignature,
    string PublicKeySignatureV2,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RefreshedAfter)
{
    /// <summary>True when <see cref="ExpiresAt"/> is at or before <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    /// <summary>Redacts the private key; prints the public key length and validity window only.</summary>
    public override string ToString() =>
        $"PlayerCertificates {{ PublicKeyPem = <{PublicKeyPem.Length} chars>, PrivateKeyPem = <redacted>, " +
        $"ExpiresAt = {ExpiresAt:O}, RefreshedAfter = {RefreshedAfter:O} }}";
}
