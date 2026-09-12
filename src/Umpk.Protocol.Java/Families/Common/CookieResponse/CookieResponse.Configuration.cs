using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Cookie response (<c>minecraft:cookie_response</c>), serverbound.</summary>
        public static readonly PacketType<ServerboundConfigCookieResponsePacket> CookieResponse =
            new(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("cookie_response"));
    }
}

/// <summary>Configuration cookie response.</summary>
public sealed record ServerboundConfigCookieResponsePacket(Identifier Key, byte[]? Payload) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.CookieResponse;
}
