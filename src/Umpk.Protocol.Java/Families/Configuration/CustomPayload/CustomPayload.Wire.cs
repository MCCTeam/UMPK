using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // Custom payload in both configuration directions: channel plus remaining bytes.

    /// <summary>Configuration custom payload (clientbound).</summary>
    public static readonly PacketCodec<ClientboundConfigCustomPayloadPacket> ConfigCustomPayloadClient =
        PacketCodec<ClientboundConfigCustomPayloadPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigCustomPayloadPacket p, PacketCodecContext _) =>
            {
                WriteId(ref w, p.Channel);
                w.WriteBytes(p.Data);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                Identifier channel = ReadId(ref r);
                return new ClientboundConfigCustomPayloadPacket(channel, r.ReadRemaining().ToArray());
            });

    /// <summary>Configuration custom payload (serverbound).</summary>
    public static readonly PacketCodec<ServerboundConfigCustomPayloadPacket> ConfigCustomPayloadServer =
        PacketCodec<ServerboundConfigCustomPayloadPacket>.Of(
            static (ref PacketWriter w, ServerboundConfigCustomPayloadPacket p, PacketCodecContext _) =>
            {
                WriteId(ref w, p.Channel);
                w.WriteBytes(p.Data);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                Identifier channel = ReadId(ref r);
                return new ServerboundConfigCustomPayloadPacket(channel, r.ReadRemaining().ToArray());
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCustomPayload(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.CustomPayload)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.ConfigCustomPayloadClient);

        bindings.Packet(LoginFamilyPackets.Config.CustomPayloadServerbound)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.ConfigCustomPayloadServer);
    }
}
