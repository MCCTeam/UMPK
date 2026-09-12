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
    /// <summary>Disguised chat (770/776). The bound chat type is a <c>Holder&lt;ChatType&gt;</c>: VarInt id (0 = inline). Vanilla always sends a registry reference here; an inline chat type (id 0) is not modeled and would surface as a frame-exact violation (trailing decoration bytes).</summary>
    public static readonly PacketCodec<ClientboundDisguisedChatPacket> DisguisedChatV1_21_5 =
        PacketCodec<ClientboundDisguisedChatPacket>.Of(
            static (ref PacketWriter w, ClientboundDisguisedChatPacket p, PacketCodecContext _) =>
            {
                WriteModernComponent(ref w, p.Message);
                w.WriteVarInt(p.ChatTypeId);
                WriteModernComponent(ref w, p.SenderName);
                w.WriteOptional(p.TargetName, WriteModernComponent);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                Component message = ReadModernComponent(ref r);
                int chatTypeId = r.ReadVarInt();
                Component sender = ReadModernComponent(ref r);
                Component? target = r.ReadOptional(ReadModernComponent);
                return new ClientboundDisguisedChatPacket(message, chatTypeId, sender, target);
            });

    /// <summary>764 disguised chat: JSON components; plain VarInt chat-type id.</summary>
    public static PacketCodec<ClientboundDisguisedChatPacket> DisguisedChatV1_19_3 { get; } =
        MakeDisguisedChat(ComponentWire.V1_8);

    /// <summary>765-769 disguised chat: NBT components, legacy interactions; plain VarInt chat-type id.</summary>
    public static PacketCodec<ClientboundDisguisedChatPacket> DisguisedChatV1_20_3 { get; } =
        MakeDisguisedChat(ComponentWire.V1_20_3);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareDisguisedChat(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.DisguisedChat)
            .From(JavaProtocols.V1_19_3, ChatDisplayCodecs.DisguisedChatV1_19_3)
            .From(JavaProtocols.V1_20_3, ChatDisplayCodecs.DisguisedChatV1_20_3)
            .From(JavaProtocols.V1_21_5, ChatDisplayCodecs.DisguisedChatV1_21_5);
    }
}
