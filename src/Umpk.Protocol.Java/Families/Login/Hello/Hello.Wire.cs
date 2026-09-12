using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginCodecs
{
    // Clientbound hello and encryption request

    /// <summary>The 47-765 encryption request: server id (max 20), RSA public key byte array, verify token byte array, and nothing after it. This three-field form remains unchanged through 1.20.4.</summary>
    public static readonly PacketCodec<ClientboundHelloPacket> ServerHelloV1_8 = ServerHello(LoginWire.V1_8);

    /// <summary>The 766+ encryption request: the same three fields plus the trailing <c>shouldAuthenticate</c> boolean 1.20.5 added. Unchanged through 26.2.</summary>
    public static readonly PacketCodec<ClientboundHelloPacket> ServerHelloV1_20_5 = ServerHello(LoginWire.V1_21_5);

    private static PacketCodec<ClientboundHelloPacket> ServerHello(LoginWire wire) =>
        PacketCodec<ClientboundHelloPacket>.Of(
            (ref PacketWriter w, ClientboundHelloPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.ServerId, 20);
                w.WriteByteArray(p.PublicKey);
                w.WriteByteArray(p.VerifyToken);
                if (wire.HelloHasShouldAuthenticate)
                    w.WriteBool(p.ShouldAuthenticate);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string serverId = r.ReadString(20);
                byte[] key = r.ReadByteArray().ToArray();
                byte[] token = r.ReadByteArray().ToArray();
                bool auth = wire.HelloHasShouldAuthenticate ? r.ReadBool() : true;
                return new ClientboundHelloPacket(serverId, key, token, auth);
            },
            WireShape.OfEra("server_hello", wire));

    // Serverbound login start

    /// <summary>1.8-era login start (name only).</summary>
    public static readonly PacketCodec<ServerboundHelloPacket> ClientHelloV1_8 = ClientHello(LoginWire.V1_8);

    /// <summary>The 764+ login start: name followed by a non-optional 16-byte profile UUID. The UUID was optional on 1.19.3-1.20.1. This form is unchanged through 26.2.</summary>
    public static readonly PacketCodec<ServerboundHelloPacket> ClientHelloV1_20_2 = ClientHello(LoginWire.V1_21_5);

    private static PacketCodec<ServerboundHelloPacket> ClientHello(LoginWire wire) =>
        PacketCodec<ServerboundHelloPacket>.Of(
            (ref PacketWriter w, ServerboundHelloPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Username, 16);
                if (wire.HelloHasProfileId)
                    w.WriteUuid(p.ProfileId ?? Guid.Empty);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString(16);
                Guid? id = wire.HelloHasProfileId ? r.ReadUuid() : null;
                return new ServerboundHelloPacket(name, id);
            },
            WireShape.OfEra("client_hello", wire));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareHello(PacketBindings bindings)
    {
        // The encryption request is server id + public key + verify token on every version up to 1.20.4, and 1.20.5 (766) appends the shouldAuthenticate boolean. Exactly two eras: reading the trailing boolean one byte early kills online-mode login outright, and it is invisible offline because an offline server never sends this packet at all. See LoginCodecs for the per-version evidence.
        bindings.Packet(LoginPackets.Clientbound.Hello)
            .From(JavaProtocols.V1_8, LoginCodecs.ServerHelloV1_8)
            .From(JavaProtocols.V1_20_5, LoginCodecs.ServerHelloV1_20_5);

        // login-start is username-only through 1.18.2; 1.19 adds an optional profile public key, 1.19.1 adds an optional uuid beside it, 1.19.3 drops the key, and 1.20.2 settles on the username plus a non-optional UUID.
        bindings.Packet(LoginPackets.Serverbound.Hello)
            .From(JavaProtocols.V1_8, LoginCodecs.ClientHelloV1_8)
            .From(JavaProtocols.V1_19, LoginCodecs.ClientHelloV1_19)
            .From(JavaProtocols.V1_19_1, LoginCodecs.ClientHelloV1_19_1)
            .From(JavaProtocols.V1_19_3, LoginCodecs.ClientHelloV1_19_3)
            .From(JavaProtocols.V1_20_2, LoginCodecs.ClientHelloV1_20_2);
    }

    /// <summary>759: name + optional public key (absent on send), no profile id.</summary>
    public static readonly PacketCodec<ServerboundHelloPacket> ClientHelloV1_19 =
        SigningEraHello(hasProfileKey: true, hasProfileId: false);

    /// <summary>760: name + optional public key (absent on send) + optional profile id.</summary>
    public static readonly PacketCodec<ServerboundHelloPacket> ClientHelloV1_19_1 =
        SigningEraHello(hasProfileKey: true, hasProfileId: true);

    /// <summary>761/762/763: name + optional profile id.</summary>
    public static readonly PacketCodec<ServerboundHelloPacket> ClientHelloV1_19_3 =
        SigningEraHello(hasProfileKey: false, hasProfileId: true);

    private static PacketCodec<ServerboundHelloPacket> SigningEraHello(bool hasProfileKey, bool hasProfileId) =>
        PacketCodec<ServerboundHelloPacket>.Of(
            (ref PacketWriter w, ServerboundHelloPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Username, 16);
                if (hasProfileKey)
                {
                    // ProfilePublicKey.Data optional: absent (offline/no key) writes a false flag; present writes the expiry (epoch millis), the DER SubjectPublicKeyInfo key bytes, and the Mojang signature (the caller supplies the era-correct v1/v2 signature bytes).
                    if (p.ProfileKey is { } key)
                    {
                        w.WriteBool(true);
                        w.WriteLong(key.ExpiresAtMillis);
                        w.WriteByteArray(key.KeyDer);
                        w.WriteByteArray(key.KeySignature);
                    }
                    else
                        w.WriteBool(false);

                }
                if (hasProfileId)
                    if (p.ProfileId is Guid gid)
                    {
                        w.WriteBool(true);
                        w.WriteUuid(gid);
                    }
                    else
                        w.WriteBool(false);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString(16);
                ProfilePublicKeyData? key = null;
                if (hasProfileKey && r.ReadBool())
                {
                    // ProfilePublicKey.Data = Instant expiresAt (long millis) + key bytes + signature bytes.
                    long expiresAt = r.ReadLong();
                    byte[] keyDer = r.ReadByteArray().ToArray();
                    byte[] signature = r.ReadByteArray().ToArray();
                    key = new ProfilePublicKeyData(expiresAt, keyDer, signature);
                }
                Guid? id = null;
                if (hasProfileId && r.ReadBool())
                    id = r.ReadUuid();

                return new ServerboundHelloPacket(name, id) { ProfileKey = key };
            });
}
