using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginCodecs
{
    /// <summary>Login acknowledged (empty terminal packet, 1.20.2+).</summary>
    public static readonly PacketCodec<ServerboundLoginAcknowledgedPacket> LoginAcknowledged =
        PacketCodec<ServerboundLoginAcknowledgedPacket>.Of(
            static (ref PacketWriter _, ServerboundLoginAcknowledgedPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ServerboundLoginAcknowledgedPacket(),
            WireShape.Of("empty"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareLoginAcknowledged(PacketBindings bindings)
    {
        bindings.Packet(LoginPackets.Serverbound.LoginAcknowledged)
            .From(JavaEras.ConfigurationPhase, LoginCodecs.LoginAcknowledged);
    }
}
