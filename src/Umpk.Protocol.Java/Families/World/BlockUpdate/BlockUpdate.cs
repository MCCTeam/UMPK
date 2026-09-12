using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Single block update (<c>minecraft:block_update</c> / 1.8 <c>block_change</c>).</summary>
        public static readonly PacketType<ClientboundBlockUpdatePacket> BlockUpdate =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("block_update"));
    }
}

// Block changes

/// <summary>Single block update: a position and a block-state id.</summary>
public sealed record ClientboundBlockUpdatePacket(BlockPos Position, int BlockStateId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.BlockUpdate;
}
