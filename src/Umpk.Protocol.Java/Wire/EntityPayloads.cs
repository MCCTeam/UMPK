using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

/// <summary>The modern position/movement/rotation block shared by teleport, position-sync, and player-position.</summary>
public sealed record PositionMoveRotation(Vec3d Position, Vec3d DeltaMovement, float YRot, float XRot);

/// <summary>A single attribute modifier on the wire: id (string on 1.8 UUID, modern namespaced), amount, operation.</summary>
public sealed record AttributeModifierEntry(Guid LegacyUuid, string? ModernId, double Amount, int Operation);
