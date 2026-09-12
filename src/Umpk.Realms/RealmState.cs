namespace Umpk.Realms;

/// <summary>The lifecycle state a Realms world reports in the worlds list.</summary>
public enum RealmState
{
    /// <summary>The state string was missing or not one of the known values.</summary>
    Unknown,

    /// <summary>The world is open and joinable (<c>OPEN</c>).</summary>
    Open,

    /// <summary>The world exists but is currently closed by its owner (<c>CLOSED</c>).</summary>
    Closed,

    /// <summary>The world slot has never been initialized with a world (<c>UNINITIALIZED</c>).</summary>
    Uninitialized,
}
