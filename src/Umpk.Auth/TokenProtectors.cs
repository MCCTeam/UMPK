using Microsoft.Extensions.Logging;

namespace Umpk.Auth;

/// <summary>Selects the default at-rest protector for the current OS: DPAPI on Windows, AES-256-GCM under an owner-only key file everywhere else, and the plaintext no-op only where neither is available.</summary>
public static class TokenProtectors
{
    /// <summary>Returns the recommended <see cref="ITokenProtector"/> for the current platform.</summary>
    /// <param name="logger">Reports the fallback when no at-rest encryption can be applied.</param>
    /// <param name="keyFilePath">Overrides where the AES key file lives on non-Windows platforms. Null uses <see cref="AesGcmTokenProtector.DefaultKeyFilePath"/>, which is under the user's local application data and deliberately outside the token directory.</param>
    /// <remarks>The returned protector may be <see cref="IDisposable"/> (the AES one holds a key schedule); callers that own its lifetime should dispose it, and callers that do not can safely ignore that, since the key material is zeroed as soon as the cipher is constructed either way.</remarks>
    public static ITokenProtector CreateDefault(ILogger? logger = null, string? keyFilePath = null)
    {
        if (OperatingSystem.IsWindows())
            return new DpapiTokenProtector();

        if (AesGcmTokenProtector.IsSupported)
            return new AesGcmTokenProtector(keyFilePath ?? AesGcmTokenProtector.DefaultKeyFilePath());

        // No DPAPI and no AES-GCM: a platform without the OS crypto this needs. Say so rather than pretending, and leave the file store's owner-only permissions as the only protection.
        return new NoOpTokenProtector(logger);
    }
}
