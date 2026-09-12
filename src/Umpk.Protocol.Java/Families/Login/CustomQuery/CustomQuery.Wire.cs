using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginChannelCodecs
{
    // Login plugin request and answer

    /// <summary>Login plugin request (custom_query): VarInt id, channel, remaining bytes.</summary>
    public static readonly PacketCodec<ClientboundLoginCustomQueryPacket> CustomQuery =
        PacketCodec<ClientboundLoginCustomQueryPacket>.Of(
            static (ref PacketWriter w, ClientboundLoginCustomQueryPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.TransactionId);
                WriteId(ref w, p.Channel);
                w.WriteBytes(p.Data);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int tx = r.ReadVarInt();
                Identifier channel = ReadId(ref r);
                byte[] data = r.ReadRemaining().ToArray();
                return new ClientboundLoginCustomQueryPacket(tx, channel, data);
            },
            WireShape.Of("varint,identifier,raw_tail"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCustomQuery(PacketBindings bindings)
    {
        // Login plugin requests begin at 1.13 and carry a transaction VarInt, channel identifier, and the remaining frame bytes.
        bindings.Packet(LoginFamilyPackets.Login.CustomQuery)
            .From(JavaEras.Flattening, LoginChannelCodecs.CustomQuery);
    }
}
