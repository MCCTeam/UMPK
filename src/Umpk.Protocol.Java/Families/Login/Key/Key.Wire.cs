using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginCodecs
{
    // Serverbound encryption response

    /// <summary>Encryption response (shared secret + verify token, both byte arrays).</summary>
    public static readonly PacketCodec<ServerboundKeyPacket> Key =
        PacketCodec<ServerboundKeyPacket>.Of(
            static (ref PacketWriter w, ServerboundKeyPacket p, PacketCodecContext _) =>
            {
                w.WriteByteArray(p.SharedSecret);
                w.WriteByteArray(p.VerifyToken);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundKeyPacket(r.ReadByteArray().ToArray(), r.ReadByteArray().ToArray()),
            WireShape.Of("bytes,bytes"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareKey(PacketBindings bindings)
    {
        // The encryption response is the plain shared-secret + verify-token shape on every protocol EXCEPT 759/760: those two wrap the second field in vanilla's Either<byte[], Crypt.SaltSignaturePair>, read UNCONDITIONALLY that way regardless of whether login-start carried a profile key. 761+ reverts to plain once the key moves out of login-start entirely. See LoginCodecs.KeyV1_19 for the wire evidence.
        bindings.Packet(LoginPackets.Serverbound.Key)
            .From(JavaProtocols.V1_8, LoginCodecs.Key)
            .From(JavaProtocols.V1_19, LoginCodecs.KeyV1_19)
            .From(JavaProtocols.V1_19_3, LoginCodecs.Key);
    }

    /// <summary>759/760: shared secret, then Either&lt;verify token, salt+signature&gt;.</summary>
    public static readonly PacketCodec<ServerboundKeyPacket> KeyV1_19 =
        PacketCodec<ServerboundKeyPacket>.Of(
            static (ref PacketWriter w, ServerboundKeyPacket p, PacketCodecContext _) =>
            {
                w.WriteByteArray(p.SharedSecret);
                if (p.SignedChallenge is { } challenge)
                {
                    w.WriteBool(false); // Either: right = signed challenge
                    w.WriteLong(challenge.Salt);
                    w.WriteByteArray(challenge.Signature);
                }
                else
                {
                    w.WriteBool(true); // Either: left = plain (RSA-encrypted) verify token
                    w.WriteByteArray(p.VerifyToken);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                byte[] sharedSecret = r.ReadByteArray().ToArray();
                if (r.ReadBool())
                {
                    byte[] verifyToken = r.ReadByteArray().ToArray();
                    return new ServerboundKeyPacket(sharedSecret, verifyToken);
                }

                long salt = r.ReadLong();
                byte[] signature = r.ReadByteArray().ToArray();
                return new ServerboundKeyPacket(sharedSecret, [])
                {
                    SignedChallenge = new SignedChallengeData(salt, signature),
                };
            });

    /// <summary>True for protocols 759 and 760, whose serverbound <c>key</c> packet selects either a verify token or a signed challenge. Other protocols carry the plain verify-token shape.</summary>
    /// <remarks>The plain shape has no discriminator and always writes <c>VerifyToken</c>. Protocols 759 and 760 replace that field with the salt and signature when the signed arm is selected. Choosing the signed arm for a plain-shape protocol would encode an invalid zero-length challenge.</remarks>
    internal static bool KeyPacketIsEitherWrapped(int protocol) =>
        protocol >= JavaProtocols.V1_19 && protocol < JavaProtocols.V1_19_3;
}
