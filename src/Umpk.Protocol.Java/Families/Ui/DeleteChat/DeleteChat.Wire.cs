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
    /// <summary>Delete chat (770/776): a packed message signature (VarInt id+1, then a full sig when id is -1).</summary>
    public static readonly PacketCodec<ClientboundDeleteChatPacket> DeleteChatV1_19_1 =
        PacketCodec<ClientboundDeleteChatPacket>.Of(
            static (ref PacketWriter w, ClientboundDeleteChatPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Id + 1);
                if (p.Id == -1)
                {
                    byte[] sig = p.FullSignature ?? throw new ProtocolViolationException("A full-signature delete-chat requires signature bytes.");
                    if (sig.Length != 256)
                        throw new ProtocolViolationException("A chat signature must be exactly 256 bytes.");

                    w.WriteBytes(sig);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt() - 1;
                byte[]? sig = id == -1 ? r.ReadBytes(256).ToArray() : null;
                return new ClientboundDeleteChatPacket(id, sig);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareDeleteChat(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.DeleteChat)
            .From(JavaProtocols.V1_19_1, ChatDisplayCodecs.DeleteChatV1_19_1);
    }
}
