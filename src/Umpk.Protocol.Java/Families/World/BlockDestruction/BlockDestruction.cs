using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Block destruction progress (<c>minecraft:block_destruction</c> / 1.8 <c>block_break_animation</c>).</summary>
        public static readonly PacketType<ClientboundBlockDestructionPacket> BlockDestruction =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("block_destruction"));
    }
}

/// <summary>Block destruction progress: the breaker entity id, the position, and a 0-10 progress byte.</summary>
public sealed record ClientboundBlockDestructionPacket(int EntityId, BlockPos Position, byte Progress) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.BlockDestruction;
}
