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
    /// <summary>1.8 block-place: blockpos, ubyte face, legacy held item, 3 ubyte cursor coords.</summary>
    public static PacketCodec<ServerboundLegacyBlockPlacePacket> BlockPlaceV1_8 { get; } =
        PacketCodec<ServerboundLegacyBlockPlacePacket>.Of(
            static (ref PacketWriter w, ServerboundLegacyBlockPlacePacket p, PacketCodecContext c) =>
            {
                w.WriteBlockPos(p.Position, BlockPosLayout.PrePacked114);
                w.WriteByte((byte)p.Face);
                ItemStackCodecs.WriteLegacyStack(ref w, p.HeldItem, c);
                w.WriteByte(p.CursorX);
                w.WriteByte(p.CursorY);
                w.WriteByte(p.CursorZ);
            },
            static (ref PacketReader r, PacketCodecContext c) =>
            {
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.PrePacked114);
                int face = r.ReadByte();
                ItemStack held = ItemStackCodecs.ReadLegacyStack(ref r, c);
                byte cx = r.ReadByte();
                byte cy = r.ReadByte();
                byte cz = r.ReadByte();
                return new ServerboundLegacyBlockPlacePacket(pos, face, held, cx, cy, cz);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareBlockPlace(PacketBindings bindings)
    {
        bindings.Packet(ItemPackets.Serverbound.LegacyBlockPlace)
            .From(JavaProtocols.V1_8, UseItemCodecs.BlockPlaceV1_8);
    }
}
