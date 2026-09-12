namespace Umpk.Protocol.Java.Packets;

public static partial class ConfigurationPackets
{
    public static partial class Clientbound
    {
        /// <summary>Keep-alive request (<c>minecraft:keep_alive</c>).</summary>
        public static readonly PacketType<ClientboundConfigKeepAlivePacket> KeepAlive =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("keep_alive"));
    }

    public static partial class Serverbound
    {
        /// <summary>Keep-alive response (<c>minecraft:keep_alive</c>).</summary>
        public static readonly PacketType<ServerboundConfigKeepAlivePacket> KeepAlive =
            new(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("keep_alive"));
    }
}

/// <summary>Configuration-phase keep-alive request carrying a nonce.</summary>
public sealed record ClientboundConfigKeepAlivePacket(long Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => ConfigurationPackets.Clientbound.KeepAlive;
}

/// <summary>Configuration-phase keep-alive response echoing the nonce.</summary>
public sealed record ServerboundConfigKeepAlivePacket(long Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => ConfigurationPackets.Serverbound.KeepAlive;
}
