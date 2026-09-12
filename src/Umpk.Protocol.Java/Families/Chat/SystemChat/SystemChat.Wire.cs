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

public static partial class ChatDisplayCodecs
{
    /// <summary>System chat (770/776).</summary>
    public static readonly PacketCodec<ClientboundSystemChatPacket> SystemChatV1_21_5 =
        PacketCodec<ClientboundSystemChatPacket>.Of(
            static (ref PacketWriter w, ClientboundSystemChatPacket p, PacketCodecContext _) =>
            {
                WriteModernComponent(ref w, p.Content);
                w.WriteBool(p.Overlay);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSystemChatPacket(ReadModernComponent(ref r), r.ReadBool()));

    /// <summary>764 system chat: JSON component + overlay bool.</summary>
    public static PacketCodec<ClientboundSystemChatPacket> SystemChatV1_19 { get; } =
        MakeSystemChat(ComponentWire.V1_8);

    /// <summary>765-769 system chat: NBT component + overlay bool, legacy interactions.</summary>
    public static PacketCodec<ClientboundSystemChatPacket> SystemChatV1_20_3 { get; } =
        MakeSystemChat(ComponentWire.V1_20_3);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSystemChat(PacketBindings bindings)
    {
        // system_chat / disguised_chat carry a JSON-string component on 1.19-1.20.2; the component was re-encoded as network NBT at 1.20.3, and interactions modernized at 1.21.5.
        bindings.Packet(UiPackets.Clientbound.SystemChat)
            .From(JavaEras.ChatSigning, ChatDisplayCodecs.SystemChatV1_19)
            .From(JavaEras.ComponentNbtTransport, ChatDisplayCodecs.SystemChatV1_20_3)
            .From(JavaEras.ModernComponents, ChatDisplayCodecs.SystemChatV1_21_5);
    }
}
