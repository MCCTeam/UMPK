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
    /// <summary>1.8 block entity data: packed BlockPos, action byte, NAMED-root NBT compound (or a bare End byte). Pre-1.20.2 network NBT carries the root name; reading it unnamed under-consumes.</summary>
    public static readonly PacketCodec<ClientboundBlockEntityDataPacket> BlockEntityDataV1_8 =
        MakeBlockEntityData(BlockEntityDataWire.V1_8);

    /// <summary>1.14-1.17.1 block entity data: packed BlockPos, action BYTE, NAMED-root NBT.</summary>
    public static readonly PacketCodec<ClientboundBlockEntityDataPacket> BlockEntityDataV1_14 =
        MakeBlockEntityData(BlockEntityDataWire.V1_14);

    /// <summary>1.18-1.20.1 block entity data: packed BlockPos, VarInt registry type, NAMED-root NBT.</summary>
    public static readonly PacketCodec<ClientboundBlockEntityDataPacket> BlockEntityDataV1_18 =
        MakeBlockEntityData(BlockEntityDataWire.V1_18);

    /// <summary>1.20.2+ block entity data: packed BlockPos, VarInt type, UNNAMED-root NBT (the network NBT framing change at 1.20.2).</summary>
    public static readonly PacketCodec<ClientboundBlockEntityDataPacket> BlockEntityDataV1_20_2 =
        MakeBlockEntityData(BlockEntityDataWire.V1_20_2);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareBlockEntityData(PacketBindings bindings)
    {
        // 47-404 share the pre-1.14 packed pos + action BYTE + NAMED-root NBT body; 1.14 moves the pos packing, 1.18 widens the action to a VarInt, and 1.20.2 unnames the NBT root.
        bindings.Packet(WorldPackets.Clientbound.BlockEntityData)
            .From(JavaProtocols.V1_8, WorldBlockCodecs.BlockEntityDataV1_8)
            .From(JavaProtocols.V1_14, WorldBlockCodecs.BlockEntityDataV1_14)
            .From(JavaProtocols.V1_18, WorldBlockCodecs.BlockEntityDataV1_18)
            .From(JavaProtocols.V1_20_2, WorldBlockCodecs.BlockEntityDataV1_20_2);
    }
}
