using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Transient block (<c>minecraft:add_transient_block</c>, 26.3+).</summary>
        public static readonly PacketType<ClientboundAddTransientBlockPacket> AddTransientBlock =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("add_transient_block"));
    }
}

/// <summary>Transient block (26.3+): a client-side-only block state at a position, the same wire body as a block update.</summary>
public sealed record ClientboundAddTransientBlockPacket(BlockPos Position, int BlockStateId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.AddTransientBlock;
}
