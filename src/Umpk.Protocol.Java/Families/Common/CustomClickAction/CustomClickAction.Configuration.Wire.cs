using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration custom-click action (1.21.6+ serverbound): the action id then the optional NBT payload behind a VarInt byte length capped at 65536 bytes. This shares the play-phase framing body verbatim rather than restating it.</summary>
    public static readonly PacketCodec<ServerboundConfigCustomClickActionPacket> CustomClickAction =
        PacketCodec<ServerboundConfigCustomClickActionPacket>.Of(
            static (ref PacketWriter w, ServerboundConfigCustomClickActionPacket p, PacketCodecContext _) =>
                UiCodecShared.WriteCustomClickAction(ref w, p.Id, p.Payload),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                (Identifier id, NbtTag? payload) = UiCodecShared.ReadCustomClickAction(ref r);
                return new ServerboundConfigCustomClickActionPacket(id, payload);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCustomClickActionConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.CustomClickAction)
            .From(JavaProtocols.V1_21_6, ConfigurationCodecs.CustomClickAction);
    }
}
