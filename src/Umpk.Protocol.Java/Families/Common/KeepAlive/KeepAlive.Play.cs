using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Keep-alive request (<c>minecraft:keep_alive</c>).</summary>
        public static readonly PacketType<ClientboundPlayKeepAlivePacket> KeepAlive =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("keep_alive"));
    }

    public static partial class Serverbound
    {
        /// <summary>Keep-alive response (<c>minecraft:keep_alive</c>).</summary>
        public static readonly PacketType<ServerboundPlayKeepAlivePacket> KeepAlive =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("keep_alive"));
    }
}

/// <summary>Play-phase keep-alive request carrying a nonce (long on 1.9+, int on 1.8).</summary>
public sealed record ClientboundPlayKeepAlivePacket(long Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.KeepAlive;
}

/// <summary>Play-phase keep-alive response echoing the nonce.</summary>
public sealed record ServerboundPlayKeepAlivePacket(long Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.KeepAlive;
}
