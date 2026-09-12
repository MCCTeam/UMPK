using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Login
    {
        /// <summary>Cookie response (<c>minecraft:cookie_response</c>), serverbound.</summary>
        public static readonly PacketType<ServerboundLoginCookieResponsePacket> CookieResponse =
            new(ProtocolPhase.Login, PacketFlow.Serverbound, Identifier.Minecraft("cookie_response"));
    }
}

/// <summary>Cookie response: the client returns the stored cookie payload (absent when it has none).</summary>
public sealed record ServerboundLoginCookieResponsePacket(Identifier Key, byte[]? Payload) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Login.CookieResponse;
}
