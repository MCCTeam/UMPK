using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Store cookie (<c>minecraft:store_cookie</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigStoreCookiePacket> StoreCookie =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("store_cookie"));
    }
}

/// <summary>Configuration store-cookie: server tells the client to persist a cookie payload.</summary>
public sealed record ClientboundConfigStoreCookiePacket(Identifier Key, byte[] Payload) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.StoreCookie;
}
