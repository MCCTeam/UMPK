using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Cookie request (<c>minecraft:cookie_request</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigCookieRequestPacket> CookieRequest =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("cookie_request"));
    }
}

// Configuration-phase packets

/// <summary>Configuration cookie request.</summary>
public sealed record ClientboundConfigCookieRequestPacket(Identifier Key) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.CookieRequest;
}
