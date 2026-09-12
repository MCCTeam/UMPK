using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration transfer (host + VarInt port).</summary>
    public static readonly PacketCodec<ClientboundConfigTransferPacket> Transfer =
        PacketCodec<ClientboundConfigTransferPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigTransferPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteTransfer(ref w, p.Host, p.Port),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                (string host, int port) = CommonPayloads.ReadTransfer(ref r);
                return new ClientboundConfigTransferPacket(host, port);
            },
            WireShape.Of("string,varint"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareTransferConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.Transfer)
            .From(JavaEras.ItemComponents, ConfigurationCodecs.Transfer);
    }
}
