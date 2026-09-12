using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Client information (locale + display + chat settings).</summary>
    public static readonly PacketCodec<ServerboundClientInformationPacket> ClientInformation =
        MakeClientInformation(ClientInformationWire.V1_21_2);

    /// <summary>764-767 client information: ends at allows-listing; the trailing particle-status VarInt is 1.21.2+; earlier servers reject the extra byte.</summary>
    public static PacketCodec<ServerboundClientInformationPacket> ClientInformationV1_20_2 { get; } =
        MakeClientInformation(ClientInformationWire.V1_18);

    private static PacketCodec<ServerboundClientInformationPacket> MakeClientInformation(ClientInformationWire wire) =>
        PacketCodec<ServerboundClientInformationPacket>.Of(
            (ref PacketWriter w, ServerboundClientInformationPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteClientInformation(
                    ref w,
                    new ClientInformationFields(
                        p.Language, p.ViewDistance, p.ChatVisibility, p.ChatColors, p.ModelCustomisation,
                        p.MainHand, p.TextFilteringEnabled, p.AllowsListing, p.ParticleStatus),
                    wire),
            (ref PacketReader r, PacketCodecContext _) =>
            {
                ClientInformationFields f = CommonPayloads.ReadClientInformation(ref r, wire);
                return new ServerboundClientInformationPacket(
                    f.Language, f.ViewDistance, f.ChatVisibility, f.ChatColors, f.ModelCustomisation,
                    f.MainHand, f.TextFilteringEnabled, f.AllowsListing, f.ParticleStatus);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareClientInformationConfiguration(PacketBindings bindings)
    {
        // No trailing particle-status VarInt on 1.20.2-1.21.1 (added 1.21.2); the server disconnects on the extra byte otherwise.
        bindings.Packet(ConfigurationPackets.Serverbound.ClientInformation)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.ClientInformationV1_20_2)
            .From(JavaEras.WideIds, ConfigurationCodecs.ClientInformation);
    }
}
