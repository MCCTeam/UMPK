namespace Umpk.Client.Movement;

/// <summary>Exclusive movement ownership. One lease exists at a time. Disposal or owner-plugin unload releases it automatically.</summary>
public interface IMovementLease : IDisposable
{
    /// <summary>The owner tag supplied when the lease was acquired.</summary>
    string OwnerTag { get; }

    /// <summary>True while this lease still holds movement control.</summary>
    bool IsHeld { get; }
}
