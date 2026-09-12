using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration reset-chat (empty).</summary>
    public static readonly PacketCodec<ClientboundConfigResetChatPacket> ResetChat =
        PacketCodec<ClientboundConfigResetChatPacket>.Of(
            static (ref PacketWriter _, ClientboundConfigResetChatPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ClientboundConfigResetChatPacket(),
            WireShape.Of("empty"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareResetChat(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.ResetChat)
            .From(JavaEras.ItemComponents, ConfigurationCodecs.ResetChat);
    }
}
