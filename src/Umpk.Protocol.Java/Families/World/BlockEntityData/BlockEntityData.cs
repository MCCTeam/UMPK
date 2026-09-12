using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Block entity data (<c>minecraft:block_entity_data</c>).</summary>
        public static readonly PacketType<ClientboundBlockEntityDataPacket> BlockEntityData =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("block_entity_data"));
    }
}

/// <summary>Block entity data: position, block-entity type id, and the raw NBT payload (may be TAG_End).</summary>
public sealed record ClientboundBlockEntityDataPacket(BlockPos Position, int BlockEntityType, NbtTag Nbt) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.BlockEntityData;
}
