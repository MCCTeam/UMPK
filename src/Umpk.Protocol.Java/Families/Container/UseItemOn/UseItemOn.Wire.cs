using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class UseItemCodecs
{
    /// <summary>Modern use-item-on: hand enum, block hit result (pos, face, cursor xyz, inside, worldBorderHit), sequence.</summary>
    public static PacketCodec<ServerboundUseItemOnPacket> UseItemOnModern { get; } =
        PacketCodec<ServerboundUseItemOnPacket>.Of(
            static (ref PacketWriter w, ServerboundUseItemOnPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Hand);
                w.WriteBlockPos(p.Position, BlockPosLayout.Packed114);
                w.WriteVarInt(p.Face);
                w.WriteFloat(p.CursorX);
                w.WriteFloat(p.CursorY);
                w.WriteFloat(p.CursorZ);
                w.WriteBool(p.Inside);
                w.WriteBool(p.WorldBorderHit);
                w.WriteVarInt(p.Sequence);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int hand = r.ReadVarInt();
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.Packed114);
                int face = r.ReadVarInt();
                float cx = r.ReadFloat();
                float cy = r.ReadFloat();
                float cz = r.ReadFloat();
                bool inside = r.ReadBool();
                bool worldBorder = r.ReadBool();
                int sequence = r.ReadVarInt();
                return new ServerboundUseItemOnPacket(hand, pos, face, cx, cy, cz, inside, worldBorder, sequence);
            });

    /// <summary>1.14-1.18.2 use-item-on (block_place): hand VarInt, then the block hit result (1.14 packed block position, direction VarInt, f32 cursor xyz, inside bool). The trailing sequence VarInt arrived at 1.19 and the world-border bool at 1.21.2, so neither is written here.</summary>
    public static PacketCodec<ServerboundUseItemOnPacket> UseItemOnV1_14 { get; } =
        PacketCodec<ServerboundUseItemOnPacket>.Of(
            static (ref PacketWriter w, ServerboundUseItemOnPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Hand);
                w.WriteBlockPos(p.Position, BlockPosLayout.Packed114);
                w.WriteVarInt(p.Face);
                w.WriteFloat(p.CursorX);
                w.WriteFloat(p.CursorY);
                w.WriteFloat(p.CursorZ);
                w.WriteBool(p.Inside);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int hand = r.ReadVarInt();
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.Packed114);
                int face = r.ReadVarInt();
                float cx = r.ReadFloat();
                float cy = r.ReadFloat();
                float cz = r.ReadFloat();
                bool inside = r.ReadBool();
                return new ServerboundUseItemOnPacket(hand, pos, face, cx, cy, cz, inside, WorldBorderHit: false, Sequence: 0);
            });

    /// <summary>1.9-1.10 use-item-on (block_place): pre-1.14 packed block position, direction VarInt, hand VarInt, three i8 cursor coords carried in SIXTEENTHS, so the packet's documented 0..1 cursor is scaled by the codec (see <c>ItemPacketCodecShared.LegacyCursorScale</c>). The 1.9 wire has no inside/world-border/sequence fields. Field order is position, direction, hand; the modern codec leads with hand.</summary>
    public static PacketCodec<ServerboundUseItemOnPacket> UseItemOnV1_9 { get; } = MakeUseItemOnPre114(floatCursor: false);

    /// <summary>1.11-1.13.2 use-item-on: as 1.9 but the cursor coordinates are f32 values in the 0..1 range.</summary>
    public static PacketCodec<ServerboundUseItemOnPacket> UseItemOnV1_11 { get; } = MakeUseItemOnPre114(floatCursor: true);

    /// <summary>764-767 use-item-on: hand, block hit (pos, face, cursor, inside), sequence; NO world-border flag.</summary>
    public static PacketCodec<ServerboundUseItemOnPacket> UseItemOnV1_19 { get; } =
        PacketCodec<ServerboundUseItemOnPacket>.Of(
            static (ref PacketWriter w, ServerboundUseItemOnPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Hand);
                w.WriteBlockPos(p.Position, BlockPosLayout.Packed114);
                w.WriteVarInt(p.Face);
                w.WriteFloat(p.CursorX);
                w.WriteFloat(p.CursorY);
                w.WriteFloat(p.CursorZ);
                w.WriteBool(p.Inside);
                w.WriteVarInt(p.Sequence);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int hand = r.ReadVarInt();
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.Packed114);
                int face = r.ReadVarInt();
                float cx = r.ReadFloat();
                float cy = r.ReadFloat();
                float cz = r.ReadFloat();
                bool inside = r.ReadBool();
                int sequence = r.ReadVarInt();
                return new ServerboundUseItemOnPacket(hand, pos, face, cx, cy, cz, inside, WorldBorderHit: false, sequence);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUseItemOn(PacketBindings bindings)
    {
        // Block-place cursor coords flip from i8 to f32 at 1.11; the wire then grew two trailing fields over time: the sequence VarInt at 1.19 and the world-border bool at 1.21.2. Each era form is picked by protocol so 1.14-1.18.2 write neither and 1.19-1.21.1 write only the sequence.
        bindings.Packet(ItemPackets.Serverbound.UseItemOn)
            .From(JavaProtocols.V1_9, UseItemCodecs.UseItemOnV1_9)
            .From(JavaProtocols.V1_11, UseItemCodecs.UseItemOnV1_11)
            .From(JavaProtocols.V1_14, UseItemCodecs.UseItemOnV1_14)
            .From(JavaProtocols.V1_19, UseItemCodecs.UseItemOnV1_19)
            .From(JavaProtocols.V1_21_2, UseItemCodecs.UseItemOnModern);
    }
}
