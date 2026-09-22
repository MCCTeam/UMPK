using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

/// <summary>The modern position/movement/rotation block shared by teleport, position-sync, and player-position.</summary>
public sealed record PositionMoveRotation(Vec3d Position, Vec3d DeltaMovement, float YRot, float XRot);

/// <summary>A 26.3+ entity position path: how the synced entity reaches its destination. The hierarchy is closed so a <c>switch</c> over it is total.</summary>
public abstract record EntityPositionPath
{
    private protected EntityPositionPath()
    {
    }

    /// <summary>A straight destination: one absolute position.</summary>
    /// <param name="Position">The destination.</param>
    public sealed record Linear(Vec3d Position) : EntityPositionPath;

    /// <summary>A stepped destination: timed position knots.</summary>
    /// <param name="Steps">The knots, in wire order.</param>
    public sealed record Stepped(IReadOnlyList<PositionPathStep> Steps) : EntityPositionPath;
}

/// <summary>One 26.3+ position-path knot: an absolute position reached after a tick offset.</summary>
/// <param name="Position">The knot position.</param>
/// <param name="TickOffset">Ticks after the previous knot.</param>
public sealed record PositionPathStep(Vec3d Position, int TickOffset);

/// <summary>A single attribute modifier on the wire: id (string on 1.8 UUID, modern namespaced), amount, operation.</summary>
public sealed record AttributeModifierEntry(Guid LegacyUuid, string? ModernId, double Amount, int Operation);
