using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class UiMiscCodecs
{
    // Each of the four sign lines has a 384-character wire limit.
    private const int SignLineMaxLength = 384;

    private static void WriteSignLines(ref PacketWriter w, ServerboundSignUpdatePacket p)
    {
        w.WriteString(p.Line1, SignLineMaxLength);
        w.WriteString(p.Line2, SignLineMaxLength);
        w.WriteString(p.Line3, SignLineMaxLength);
        w.WriteString(p.Line4, SignLineMaxLength);
    }

    /// <summary>Sign update (1.9-1.13.2): pre-1.14 block position (y in the middle 12 bits) followed by four line strings. No front/back flag.</summary>
    public static readonly PacketCodec<ServerboundSignUpdatePacket> SignUpdateV1_9 =
        PacketCodec<ServerboundSignUpdatePacket>.Of(
            static (ref PacketWriter w, ServerboundSignUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Pos, BlockPosLayout.PrePacked114);
                WriteSignLines(ref w, p);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.PrePacked114);
                return new ServerboundSignUpdatePacket(
                    pos, IsFrontText: true,
                    r.ReadString(SignLineMaxLength), r.ReadString(SignLineMaxLength),
                    r.ReadString(SignLineMaxLength), r.ReadString(SignLineMaxLength));
            });

    /// <summary>Sign update (1.14-1.19.4): 1.14 block position (y in the low 12 bits) followed by four line strings. No front/back flag.</summary>
    public static readonly PacketCodec<ServerboundSignUpdatePacket> SignUpdateV1_14 =
        PacketCodec<ServerboundSignUpdatePacket>.Of(
            static (ref PacketWriter w, ServerboundSignUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Pos, BlockPosLayout.Packed114);
                WriteSignLines(ref w, p);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.Packed114);
                return new ServerboundSignUpdatePacket(
                    pos, IsFrontText: true,
                    r.ReadString(SignLineMaxLength), r.ReadString(SignLineMaxLength),
                    r.ReadString(SignLineMaxLength), r.ReadString(SignLineMaxLength));
            });

    /// <summary>Sign update (1.20+): 1.14 block position, a front/back boolean, then four line strings.</summary>
    public static readonly PacketCodec<ServerboundSignUpdatePacket> SignUpdateV1_20 =
        PacketCodec<ServerboundSignUpdatePacket>.Of(
            static (ref PacketWriter w, ServerboundSignUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Pos, BlockPosLayout.Packed114);
                w.WriteBool(p.IsFrontText);
                WriteSignLines(ref w, p);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.Packed114);
                bool isFront = r.ReadBool();
                return new ServerboundSignUpdatePacket(
                    pos, isFront,
                    r.ReadString(SignLineMaxLength), r.ReadString(SignLineMaxLength),
                    r.ReadString(SignLineMaxLength), r.ReadString(SignLineMaxLength));
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSignUpdate(PacketBindings bindings)
    {
        // Protocol 47 carries a BlockPos plus four strings, the same framing as 1.9, but each string is component JSON rather than the plain line the record models, so the 1.9 codec would decode raw JSON into Lines. 1.9-1.13.2 uses the pre-1.14 block-pos packing; 1.14+ uses the 1.14 packing; 1.20+ adds the front/back text flag.
        bindings.Packet(UiPackets.Serverbound.SignUpdate)
            .MarkerFrom(
                JavaProtocols.V1_8,
                MarkerReason.WrongCodecWouldBeWorse,
                "1.8 writes each of the four sign lines as an IChatComponent JSON string where the 1.9 codec writes a plain line. The framing is identical, which is exactly why it looks like an off-by-one, and binding the 1.9 form would encode plain text into a field the 1.8 server hands to the JSON deserializer.")
            .From(JavaProtocols.V1_9, UiMiscCodecs.SignUpdateV1_9)
            .From(JavaProtocols.V1_14, UiMiscCodecs.SignUpdateV1_14)
            .From(JavaProtocols.V1_20, UiMiscCodecs.SignUpdateV1_20);
    }
}
