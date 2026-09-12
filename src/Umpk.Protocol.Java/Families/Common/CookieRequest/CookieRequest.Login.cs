using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Login
    {
        /// <summary>Cookie request (<c>minecraft:cookie_request</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundLoginCookieRequestPacket> CookieRequest =
            new(ProtocolPhase.Login, PacketFlow.Clientbound, Identifier.Minecraft("cookie_request"));
    }
}

// The server_links payload record (ServerLinkEntry) is declared with the UI family in UiPackets.cs.

// Login-phase packets

/// <summary>Cookie request: the server asks the client for a stored cookie by key.</summary>
public sealed record ClientboundLoginCookieRequestPacket(Identifier Key) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Login.CookieRequest;
}
