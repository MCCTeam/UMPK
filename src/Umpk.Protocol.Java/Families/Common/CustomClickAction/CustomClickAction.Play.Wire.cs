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
    /// <summary>Custom click action (1.21.6+ serverbound): an action id and an optional NBT payload, where the payload is LENGTH PREFIXED.</summary>
    /// <remarks>The payload codec renders the inner value into a scratch buffer, rejects a body larger
    /// than the limit, then writes a VarInt length followed by the body; the decoder rejects a length over the limit, and decodes the inner codec from exactly that many bytes. The inner optional tag codec uses unnamed-root network NBT with a bare TAG_End byte for the absent payload. So an ABSENT payload is length 1 carrying a single 0x00, never length 0.
    /// <para>Without the prefix, the leading NBT type byte is misread as the body length.</para>
    /// </remarks>
    public static readonly PacketCodec<ServerboundCustomClickActionPacket> CustomClickActionV1_21_6 =
        PacketCodec<ServerboundCustomClickActionPacket>.Of(
            static (ref PacketWriter w, ServerboundCustomClickActionPacket p, PacketCodecContext _) =>
                WriteCustomClickAction(ref w, p.Id, p.Payload),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                (Identifier id, NbtTag? payload) = ReadCustomClickAction(ref r);
                return new ServerboundCustomClickActionPacket(id, payload);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCustomClickActionPlay(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Serverbound.CustomClickAction)
            .From(JavaProtocols.V1_21_6, UiMiscCodecs.CustomClickActionV1_21_6);
    }
}
