namespace Umpk.Auth;

/// <summary>Identifies which authentication flow produced a <see cref="JavaSession"/>.</summary>
public enum AuthKind
{
    /// <summary>Offline mode: deterministic UUID, no online session verification.</summary>
    Offline,

    /// <summary>Microsoft account through the XBL / XSTS / Minecraft-services chain.</summary>
    Microsoft,

    /// <summary>A third-party Yggdrasil provider (authlib-injector).</summary>
    Yggdrasil,
}
