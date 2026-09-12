using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginPackets
{
    public static partial class Serverbound
    {
        /// <summary>Encryption response (<c>minecraft:key</c>).</summary>
        public static readonly PacketType<ServerboundKeyPacket> Key =
            new(ProtocolPhase.Login, PacketFlow.Serverbound, Identifier.Minecraft("key"));
    }
}

/// <summary>Encryption response: the RSA-encrypted shared secret, and either the RSA-encrypted verify token (<see cref="VerifyToken"/>, every era except the two below) or, on the 1.19/1.19.1 signing eras (protocols 759/760) when the login-start carried a profile public key, a signed challenge (<see cref="SignedChallenge"/>) instead. <see cref="VerifyToken"/> is ignored on the wire whenever <see cref="SignedChallenge"/> is set; the two are mutually exclusive, matching vanilla's <c>Either&lt;byte[], Crypt.SaltSignaturePair&gt;</c>.</summary>
public sealed record ServerboundKeyPacket(byte[] SharedSecret, byte[] VerifyToken) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginPackets.Serverbound.Key;

    /// <summary>The 1.19/1.19.1 (protocols 759/760) signed-challenge alternative to <see cref="VerifyToken"/>, used only when the login-start carried a profile public key (so the server already holds it and expects a signature rather than an RSA-encrypted echo of its verify token). Null on an unsigned/no-key send and on every other era; the era codec writes the plain <see cref="VerifyToken"/> form whenever this is null.</summary>
    public SignedChallengeData? SignedChallenge { get; init; }
}
