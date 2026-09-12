using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginCodecs
{
    // Clientbound compression setup

    /// <summary>Set-compression threshold (frozen VarInt shape across versions that have it).</summary>
    public static readonly PacketCodec<ClientboundLoginCompressionPacket> Compression =
        PacketCodec<ClientboundLoginCompressionPacket>.Of(
            static (ref PacketWriter w, ClientboundLoginCompressionPacket p, PacketCodecContext _) => w.WriteVarInt(p.Threshold),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundLoginCompressionPacket(r.ReadVarInt()),
            WireShape.Of("varint"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareLoginCompression(PacketBindings bindings)
    {
        bindings.Packet(LoginPackets.Clientbound.LoginCompression)
            .From(JavaProtocols.V1_8, LoginCodecs.Compression);
    }
}
