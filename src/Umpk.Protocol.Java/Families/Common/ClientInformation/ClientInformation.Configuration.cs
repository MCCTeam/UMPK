namespace Umpk.Protocol.Java.Packets;

public static partial class ConfigurationPackets
{
    public static partial class Serverbound
    {
        /// <summary>Client information (<c>minecraft:client_information</c>).</summary>
        public static readonly PacketType<ServerboundClientInformationPacket> ClientInformation =
            new(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("client_information"));
    }
}

/// <summary>Client information (locale, view distance, chat and skin settings).</summary>
public sealed record ServerboundClientInformationPacket(
    string Language,
    sbyte ViewDistance,
    int ChatVisibility,
    bool ChatColors,
    byte ModelCustomisation,
    int MainHand,
    bool TextFilteringEnabled,
    bool AllowsListing,
    int ParticleStatus) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => ConfigurationPackets.Serverbound.ClientInformation;
}
