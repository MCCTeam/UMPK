namespace Umpk.Realms;

/// <summary>The client-compatibility verdict returned by <c>GET /mco/client/compatible</c>. Realms gates access by the client version reported in the session cookie.</summary>
public enum RealmsCompatibility
{
    /// <summary>The verdict body was empty or not one of the known values.</summary>
    Unknown,

    /// <summary>The client version is accepted (<c>COMPATIBLE</c>).</summary>
    Compatible,

    /// <summary>The client version is too old and must upgrade (<c>OUTDATED</c>).</summary>
    Outdated,

    /// <summary>The client version is otherwise not accepted (<c>OTHER</c>).</summary>
    Other,
}
