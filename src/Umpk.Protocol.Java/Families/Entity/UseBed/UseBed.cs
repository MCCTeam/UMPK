using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.8 use-bed (<c>minecraft:use_bed</c>).</summary>
        public static readonly PacketType<ClientboundUseBedPacket> UseBed =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("use_bed"));
    }
}

/// <summary>1.8 use-bed: player entity id, bed block position.</summary>
public sealed record ClientboundUseBedPacket(int PlayerId, BlockPos Position) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.UseBed;
}
