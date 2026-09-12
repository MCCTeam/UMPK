using System.Text.Json;
using Umpk.Protocol.Java.Signing;

namespace Umpk.Auth.Persistence;

/// <summary>Closed-type serialization for the persisted auth artifacts. The file store dispatches on the exact public type through this helper; unknown types throw. No reflection or open-generic serialization is used, only the source-generated <see cref="AuthJsonContext"/>.</summary>
internal static class PersistedJson
{
    /// <summary>Serializes a supported value to UTF-8 JSON, or throws for an unsupported type.</summary>
    public static byte[] Serialize<T>(T value)
    {
        switch (value)
        {
            case JavaSession session:
                return JsonSerializer.SerializeToUtf8Bytes(ToPersisted(session), AuthJsonContext.Default.PersistedSession);
            case PlayerCertificates certificates:
                return JsonSerializer.SerializeToUtf8Bytes(ToPersisted(certificates), AuthJsonContext.Default.PersistedCertificates);
            case PersistedRefreshToken refresh:
                return JsonSerializer.SerializeToUtf8Bytes(refresh, AuthJsonContext.Default.PersistedRefreshToken);
            default:
                throw new NotSupportedException(UnsupportedMessage(typeof(T)));
        }
    }

    /// <summary>Deserializes UTF-8 JSON to a supported type. Returns default when the payload is empty or malformed; throws for an unsupported type.</summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
        Type t = typeof(T);
        if (t == typeof(JavaSession))
        {
            PersistedSession? p = JsonSerializer.Deserialize(utf8Json, AuthJsonContext.Default.PersistedSession);
            return p is null ? default : (T)(object)FromPersisted(p);
        }

        if (t == typeof(PlayerCertificates))
        {
            PersistedCertificates? p = JsonSerializer.Deserialize(utf8Json, AuthJsonContext.Default.PersistedCertificates);
            return p is null ? default : (T)(object)FromPersisted(p);
        }

        if (t == typeof(PersistedRefreshToken))
        {
            PersistedRefreshToken? p = JsonSerializer.Deserialize(utf8Json, AuthJsonContext.Default.PersistedRefreshToken);
            return p is null ? default : (T)(object)p;
        }

        throw new NotSupportedException(UnsupportedMessage(t));
    }

    /// <summary>True when <typeparamref name="T"/> is one of the persisted types.</summary>
    public static bool IsSupported<T>() =>
        typeof(T) == typeof(JavaSession)
        || typeof(T) == typeof(PlayerCertificates)
        || typeof(T) == typeof(PersistedRefreshToken);

    private static PersistedSession ToPersisted(JavaSession s) =>
        new(s.Profile.Id, s.Profile.Name, s.AccessToken, s.ExpiresAt, s.RefreshToken, s.Kind);

    private static JavaSession FromPersisted(PersistedSession p) =>
        new(new GameProfile(p.ProfileId, p.ProfileName), p.AccessToken, p.ExpiresAt, p.RefreshToken, p.Kind);

    private static PersistedCertificates ToPersisted(PlayerCertificates c) =>
        new(c.PublicKeyPem, c.PrivateKeyPem, c.PublicKeySignature, c.PublicKeySignatureV2, c.ExpiresAt, c.RefreshedAfter);

    private static PlayerCertificates FromPersisted(PersistedCertificates p) =>
        new(p.PublicKeyPem, p.PrivateKeyPem, p.PublicKeySignature, p.PublicKeySignatureV2, p.ExpiresAt, p.RefreshedAfter);

    private static string UnsupportedMessage(Type t) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"The default token store only persists Umpk.Auth's own types (JavaSession, PlayerCertificates, PersistedRefreshToken); '{t.FullName}' is not supported. Implement a custom ITokenStore to persist other types.");
}
