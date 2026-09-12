using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Block action (<c>minecraft:block_event</c>).</summary>
        public static readonly PacketType<ClientboundBlockEventPacket> BlockEvent =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("block_event"));
    }
}

/// <summary>Block action / block event: position, two action bytes, and the acting block id.</summary>
public sealed record ClientboundBlockEventPacket(BlockPos Position, byte B0, byte B1, int BlockId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.BlockEvent;
}
