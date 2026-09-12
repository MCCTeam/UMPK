using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Play-phase cookie response (<c>minecraft:cookie_response</c>, 1.20.5+).</summary>
        public static readonly PacketType<ServerboundCookieResponsePacket> CookieResponse =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("cookie_response"));
    }
}

/// <summary>Play-phase cookie response (1.20.5+): the stored payload for the requested key, or absent when the client holds no cookie under it.</summary>
public sealed record ServerboundCookieResponsePacket(Identifier Key, byte[]? Payload) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.CookieResponse;
}
