using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>1.8 player action / block dig: status byte, block position, and face byte.</summary>
    public static readonly PacketCodec<ServerboundPlayerActionPacket> PlayerActionV1_8 =
        PacketCodec<ServerboundPlayerActionPacket>.Of(
            static (ref PacketWriter w, ServerboundPlayerActionPacket p, PacketCodecContext _) =>
            {
                w.WriteByte((byte)p.Action);
                w.WriteBlockPos(p.Position, BlockPosLayout.PrePacked114);
                w.WriteByte(p.Direction);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundPlayerActionPacket(r.ReadByte(), r.ReadBlockPos(BlockPosLayout.PrePacked114), r.ReadByte(), null));

    /// <summary>1.9-1.13.2 player action / block dig: action VarInt (status became a VarInt at 1.9), pre-1.14 packed block position, direction byte. No trailing sequence VarInt (that arrived at 1.19).</summary>
    public static readonly PacketCodec<ServerboundPlayerActionPacket> PlayerActionV1_9 =
        PacketCodec<ServerboundPlayerActionPacket>.Of(
            static (ref PacketWriter w, ServerboundPlayerActionPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Action);
                w.WriteBlockPos(p.Position, BlockPosLayout.PrePacked114);
                w.WriteByte(p.Direction);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundPlayerActionPacket(r.ReadVarInt(), r.ReadBlockPos(BlockPosLayout.PrePacked114), r.ReadByte(), null));

    /// <summary>1.14-1.18.2 player action / block dig: action VarInt, 1.14 packed block position, direction byte. The 1.14 block-position packing (x,z,y order) replaced the pre-1.14 layout, but the trailing sequence VarInt only arrived at 1.19, so this era writes none. Sharing the 1.19+ codec here would append a spurious byte the server rejects.</summary>
    public static readonly PacketCodec<ServerboundPlayerActionPacket> PlayerActionV1_14 =
        PacketCodec<ServerboundPlayerActionPacket>.Of(
            static (ref PacketWriter w, ServerboundPlayerActionPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Action);
                w.WriteBlockPos(p.Position, BlockPosLayout.Packed114);
                w.WriteByte(p.Direction);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundPlayerActionPacket(r.ReadVarInt(), r.ReadBlockPos(BlockPosLayout.Packed114), r.ReadByte(), null));

    /// <summary>Modern player action: action VarInt, block position, direction byte, sequence VarInt.</summary>
    public static readonly PacketCodec<ServerboundPlayerActionPacket> PlayerActionV1_19 =
        PacketCodec<ServerboundPlayerActionPacket>.Of(
            static (ref PacketWriter w, ServerboundPlayerActionPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Action);
                w.WriteBlockPos(p.Position, BlockPosLayout.Packed114);
                w.WriteByte(p.Direction);
                w.WriteVarInt(p.Sequence ?? 0);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundPlayerActionPacket(r.ReadVarInt(), r.ReadBlockPos(BlockPosLayout.Packed114), r.ReadByte(), r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerAction(PacketBindings bindings)
    {
        // The block-dig send gained a trailing sequence VarInt at 1.19; 1.14-1.18.2 share the 1.14 packed-position wire but write no sequence, so the era form is picked by protocol to avoid appending the spurious byte the server rejects.
        bindings.Packet(EntityPackets.Serverbound.PlayerAction)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.PlayerActionV1_8)
            .From(JavaProtocols.V1_9, EntityServerboundCodecs.PlayerActionV1_9)
            .From(JavaProtocols.V1_14, EntityServerboundCodecs.PlayerActionV1_14)
            .From(JavaProtocols.V1_19, EntityServerboundCodecs.PlayerActionV1_19);
    }
}
