using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Play-phase cookie request (<c>minecraft:cookie_request</c>, 1.20.5+).</summary>
        public static readonly PacketType<ClientboundCookieRequestPacket> CookieRequest =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("cookie_request"));
    }
}

/// <summary>Play-phase cookie request (1.20.5+): the server asks for a stored cookie by key and waits for the answer. The login, configuration, and play forms are wire-identical.</summary>
public sealed record ClientboundCookieRequestPacket(Identifier Key) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.CookieRequest;
}
