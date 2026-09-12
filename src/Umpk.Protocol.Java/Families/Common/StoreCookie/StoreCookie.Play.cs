using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Play-phase store cookie (<c>minecraft:store_cookie</c>, 1.20.5+).</summary>
        public static readonly PacketType<ClientboundStoreCookiePacket> StoreCookie =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("store_cookie"));
    }
}

/// <summary>Play-phase store cookie (1.20.5+): persist a payload under a key for the rest of the session.</summary>
public sealed record ClientboundStoreCookiePacket(Identifier Key, byte[] Payload) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.StoreCookie;
}
