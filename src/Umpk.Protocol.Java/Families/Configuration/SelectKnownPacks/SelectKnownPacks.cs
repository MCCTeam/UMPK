namespace Umpk.Protocol.Java.Packets;

public static partial class ConfigurationPackets
{
    public static partial class Clientbound
    {
        /// <summary>Known-pack selection request (<c>minecraft:select_known_packs</c>).</summary>
        public static readonly PacketType<ClientboundSelectKnownPacksPacket> SelectKnownPacks =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("select_known_packs"));
    }

    public static partial class Serverbound
    {
        /// <summary>Known-pack selection response (<c>minecraft:select_known_packs</c>).</summary>
        public static readonly PacketType<ServerboundSelectKnownPacksPacket> SelectKnownPacks =
            new(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("select_known_packs"));
    }
}

/// <summary>Known-pack selection request listing the server's datapacks.</summary>
public sealed record ClientboundSelectKnownPacksPacket(IReadOnlyList<KnownPack> Packs) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => ConfigurationPackets.Clientbound.SelectKnownPacks;
}

/// <summary>Known-pack selection response listing packs the client accepts.</summary>
public sealed record ServerboundSelectKnownPacksPacket(IReadOnlyList<KnownPack> Packs) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => ConfigurationPackets.Serverbound.SelectKnownPacks;
}
