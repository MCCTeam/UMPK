using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Relative move + rotation (<c>minecraft:move_entity_pos_rot</c> / 1.8 <c>move_entity_position_rotation</c>).</summary>
        public static readonly PacketType<ClientboundMoveEntityPosRotPacket> MoveEntityPosRot =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("move_entity_pos_rot"));
    }
}

/// <summary>Relative move + rotation.</summary>
public sealed record ClientboundMoveEntityPosRotPacket(
    int EntityId, short DeltaX, short DeltaY, short DeltaZ, float Yaw, float Pitch, bool OnGround) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.MoveEntityPosRot;

    /// <summary>The 26.3+ stepped deltas. Empty on older eras (and on 26.3 zero-step frames); the legacy delta fields mirror the first step where one exists so era-blind consumers keep working.</summary>
    public IReadOnlyList<EntityMoveStep> Steps { get; init; } = [];
}

/// <summary>One 26.3+ relative-move step: the tick delay plus the short deltas applied when it elapses.</summary>
/// <param name="Ticks">Ticks to wait before applying this step.</param>
/// <param name="DeltaX">The X delta in 1/4096 blocks.</param>
/// <param name="DeltaY">The Y delta in 1/4096 blocks.</param>
/// <param name="DeltaZ">The Z delta in 1/4096 blocks.</param>
public sealed record EntityMoveStep(int Ticks, short DeltaX, short DeltaY, short DeltaZ);
