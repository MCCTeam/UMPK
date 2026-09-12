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
    /// <summary>Custom chat completions (770/776).</summary>
    public static readonly PacketCodec<ClientboundCustomChatCompletionsPacket> CustomChatCompletionsV1_19_1 =
        PacketCodec<ClientboundCustomChatCompletionsPacket>.Of(
            static (ref PacketWriter w, ClientboundCustomChatCompletionsPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt((int)p.Action);
                w.WriteList(p.Entries, static (ref PacketWriter sw, string s) => sw.WriteString(s));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                var action = (ChatCompletionsAction)r.ReadVarInt();
                string[] entries = r.ReadList(static (ref PacketReader sr) => sr.ReadString());
                return new ClientboundCustomChatCompletionsPacket(action, entries);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCustomChatCompletions(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.CustomChatCompletions)
            .From(JavaProtocols.V1_19_1, ChatDisplayCodecs.CustomChatCompletionsV1_19_1);
    }
}
