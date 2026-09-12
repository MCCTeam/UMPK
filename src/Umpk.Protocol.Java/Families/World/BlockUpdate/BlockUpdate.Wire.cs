using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldBlockCodecs
{
    /// <summary>1.8 single block change: packed BlockPos (pre-1.14 layout) + VarInt state.</summary>
    public static readonly PacketCodec<ClientboundBlockUpdatePacket> BlockUpdateV1_8 = MakeBlockUpdate(BlockPosLayout.PrePacked114);

    /// <summary>Modern single block update: packed BlockPos + VarInt state.</summary>
    public static readonly PacketCodec<ClientboundBlockUpdatePacket> BlockUpdateV1_14 = MakeBlockUpdate(BlockPosLayout.Packed114);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareBlockUpdate(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.BlockUpdate)
            .From(JavaProtocols.V1_8, WorldBlockCodecs.BlockUpdateV1_8)
            .From(JavaProtocols.V1_14, WorldBlockCodecs.BlockUpdateV1_14)
            .AliasedAs(Identifier.Minecraft("block_change"));
    }
}
