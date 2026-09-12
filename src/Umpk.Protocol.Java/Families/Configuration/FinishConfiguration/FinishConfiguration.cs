namespace Umpk.Protocol.Java.Packets;

public static partial class ConfigurationPackets
{
    public static partial class Clientbound
    {
        /// <summary>Finish configuration, terminal into play (<c>minecraft:finish_configuration</c>).</summary>
        public static readonly PacketType<ClientboundFinishConfigurationPacket> FinishConfiguration =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("finish_configuration"));
    }

    public static partial class Serverbound
    {
        /// <summary>Finish-configuration acknowledgment (<c>minecraft:finish_configuration</c>).</summary>
        public static readonly PacketType<ServerboundFinishConfigurationPacket> FinishConfiguration =
            new(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("finish_configuration"));
    }
}

/// <summary>Finish configuration; terminal into the play phase.</summary>
public sealed record ClientboundFinishConfigurationPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => ConfigurationPackets.Clientbound.FinishConfiguration;
}

/// <summary>Finish-configuration acknowledgment; terminal into play.</summary>
public sealed record ServerboundFinishConfigurationPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => ConfigurationPackets.Serverbound.FinishConfiguration;
}
