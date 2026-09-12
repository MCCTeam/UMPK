using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginPackets
{
    public static partial class Clientbound
    {
        /// <summary>Encryption request (<c>minecraft:hello</c>).</summary>
        public static readonly PacketType<ClientboundHelloPacket> Hello =
            new(ProtocolPhase.Login, PacketFlow.Clientbound, Identifier.Minecraft("hello"));
    }

    public static partial class Serverbound
    {
        /// <summary>Login start (<c>minecraft:hello</c>).</summary>
        public static readonly PacketType<ServerboundHelloPacket> Hello =
            new(ProtocolPhase.Login, PacketFlow.Serverbound, Identifier.Minecraft("hello"));
    }
}

/// <summary>Encryption request: server id, RSA public key, verify token, and (modern) the authenticate flag.</summary>
public sealed record ClientboundHelloPacket(
    string ServerId,
    byte[] PublicKey,
    byte[] VerifyToken,
    bool ShouldAuthenticate) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginPackets.Clientbound.Hello;
}

/// <summary>Login start: username, (modern) the player UUID, and, on the 1.19/1.19.1 signing eras only, the player's profile public key. On 1.8 only the username is sent; the era codec reads/writes only what the version has. The 1.19.3+ eras carry no key here (it moved to <c>chat_session_update</c>), so <see cref="ProfileKey"/> is written only by the 759/760 codecs and is null on an offline send.</summary>
public sealed record ServerboundHelloPacket(string Username, Guid? ProfileId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginPackets.Serverbound.Hello;

    /// <summary>The profile public key attached to the login start on the 1.19/1.19.1 signing eras (protocols 759/760). Null on an offline/unsigned send and on every other era; the 1.19.3+ login-start codec never emits it (the key moved to <c>chat_session_update</c>).</summary>
    public ProfilePublicKeyData? ProfileKey { get; init; }
}
