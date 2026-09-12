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
    private static byte PackCommandBlockFlags(ServerboundSetCommandBlockPacket p)
    {
        byte flags = 0;
        if (p.TrackOutput)
            flags |= 1;

        if (p.Conditional)
            flags |= 2;

        if (p.Automatic)
            flags |= 4;

        return flags;
    }

    private static PacketCodec<ServerboundSetCommandBlockPacket> MakeSetCommandBlock(BlockPosLayout layout) =>
        PacketCodec<ServerboundSetCommandBlockPacket>.Of(
            (ref PacketWriter w, ServerboundSetCommandBlockPacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Pos, layout);
                w.WriteString(p.Command);
                w.WriteVarInt(p.Mode);
                w.WriteByte(PackCommandBlockFlags(p));
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                BlockPos pos = r.ReadBlockPos(layout);
                string command = r.ReadString();
                int mode = r.ReadVarInt();
                byte flags = r.ReadByte();
                return new ServerboundSetCommandBlockPacket(
                    pos, command, mode,
                    TrackOutput: (flags & 1) != 0, Conditional: (flags & 2) != 0, Automatic: (flags & 4) != 0);
            });

    /// <summary>Set command block (1.13-1.13.2): pre-1.14 block position, command string, VarInt mode enum (SEQUENCE=0/AUTO=1/REDSTONE=2), then a flags byte (track_output=1, conditional=2, automatic=4). The only difference from the later layout is the block-position packing.</summary>
    public static readonly PacketCodec<ServerboundSetCommandBlockPacket> SetCommandBlockV1_13 =
        MakeSetCommandBlock(BlockPosLayout.PrePacked114);

    /// <summary>Set command block (1.14+): 1.14 block position, command string, VarInt mode enum, flags byte.</summary>
    public static readonly PacketCodec<ServerboundSetCommandBlockPacket> SetCommandBlockV1_14 =
        MakeSetCommandBlock(BlockPosLayout.Packed114);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetCommandBlock(PacketBindings bindings)
    {
        // Command block edit: 1.13+ (block position packing switches to the 1.14 layout at 1.14).
        bindings.Packet(WorldPackets.Serverbound.SetCommandBlock)
            .From(JavaEras.Flattening, WorldBlockCodecs.SetCommandBlockV1_13)
            .From(JavaEras.Palettes, WorldBlockCodecs.SetCommandBlockV1_14);
    }
}
