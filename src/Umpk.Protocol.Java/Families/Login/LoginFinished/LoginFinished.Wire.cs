using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginCodecs
{
    // Clientbound login-finished and game-profile packets

    /// <summary>1.8-era login success (dashed-string UUID + name).</summary>
    public static readonly PacketCodec<ClientboundLoginFinishedPacket> FinishedV1_8 = Finished(LoginWire.V1_8);

    /// <summary>The 759-765 and 768-775 login finished: raw UUID + name + a counted list of name/value/optional-signature profile properties.</summary>
    public static readonly PacketCodec<ClientboundLoginFinishedPacket> FinishedV1_19 = Finished(LoginWire.V1_21_5);

    /// <summary>The 776 login finished: the game profile plus the trailing session-id UUID 26.2 added.</summary>
    public static readonly PacketCodec<ClientboundLoginFinishedPacket> FinishedV26_2 = Finished(LoginWire.V26_2);

    /// <summary>1.16-1.18.2 login finished (16-byte UUID + name, no property array).</summary>
    public static readonly PacketCodec<ClientboundLoginFinishedPacket> FinishedV1_16 = Finished(LoginWire.V1_16);

    private static PacketCodec<ClientboundLoginFinishedPacket> Finished(LoginWire wire) =>
        PacketCodec<ClientboundLoginFinishedPacket>.Of(
            (ref PacketWriter w, ClientboundLoginFinishedPacket p, PacketCodecContext _) =>
            {
                if (wire.RawUuid)
                    w.WriteUuid(p.Uuid);

                else
                    w.WriteString(p.Uuid.ToString("D", System.Globalization.CultureInfo.InvariantCulture), 36);

                w.WriteString(p.Username, 16);
                if (wire.FinishedHasProperties)
                    w.WriteList(p.Properties, static (ref PacketWriter pw, GameProfileProperty prop) =>
                    {
                        pw.WriteString(prop.Name);
                        pw.WriteString(prop.Value);
                        pw.WriteOptional(prop.Signature, static (ref PacketWriter sw, string sig) => sw.WriteString(sig));
                    });

                if (wire.FinishedHasSessionId)
                    w.WriteUuid(p.SessionId ?? Guid.Empty);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                Guid uuid = wire.RawUuid
                    ? r.ReadUuid()
                    : Guid.Parse(r.ReadString(36));
                string name = r.ReadString(16);
                GameProfileProperty[] props = wire.FinishedHasProperties
                    ? r.ReadList(static (ref PacketReader pr) =>
                    {
                        string pname = pr.ReadString();
                        string pvalue = pr.ReadString();
                        string? sig = pr.ReadBool() ? pr.ReadString() : null;
                        return new GameProfileProperty(pname, pvalue, sig);
                    })
                    : [];
                Guid? session = wire.FinishedHasSessionId ? r.ReadUuid() : null;
                return new ClientboundLoginFinishedPacket(uuid, name, props, session);
            },
            WireShape.OfEra("login_finished", wire));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareLoginFinished(PacketBindings bindings)
    {
        // The uuid is a string through 1.15.2 and a 16-byte integer array from 1.16; the profile property array begins at 1.19. Protocols 766/767 (1.20.5-1.21.1) carry the deprecated trailing strict-error-handling bool (introduced 1.20.5, removed 1.21.2), and 26.2 appends the session id.
        bindings.Packet(LoginPackets.Clientbound.LoginFinished)
            .From(JavaProtocols.V1_8, LoginCodecs.FinishedV1_8)
            .From(JavaProtocols.V1_16, LoginCodecs.FinishedV1_16)
            .From(JavaProtocols.V1_19, LoginCodecs.FinishedV1_19)
            .From(JavaProtocols.V1_20_5, LoginCodecs.FinishedV1_20_5)
            .From(JavaProtocols.V1_21_2, LoginCodecs.FinishedV1_19)
            .From(JavaProtocols.V26_2, LoginCodecs.FinishedV26_2);
    }

    /// <summary>766/767 login finished: raw UUID + name + properties + strict-error-handling bool.</summary>
    public static readonly PacketCodec<ClientboundLoginFinishedPacket> FinishedV1_20_5 =
        PacketCodec<ClientboundLoginFinishedPacket>.Of(
            static (ref PacketWriter w, ClientboundLoginFinishedPacket p, PacketCodecContext _) =>
            {
                w.WriteUuid(p.Uuid);
                w.WriteString(p.Username, 16);
                w.WriteList(p.Properties, static (ref PacketWriter pw, GameProfileProperty prop) =>
                {
                    pw.WriteString(prop.Name);
                    pw.WriteString(prop.Value);
                    pw.WriteOptional(prop.Signature, static (ref PacketWriter sw, string sig) => sw.WriteString(sig));
                });
                w.WriteBool(p.StrictErrorHandling);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                Guid uuid = r.ReadUuid();
                string name = r.ReadString(16);
                GameProfileProperty[] props = r.ReadList(static (ref PacketReader pr) =>
                {
                    string pname = pr.ReadString();
                    string pvalue = pr.ReadString();
                    string? sig = pr.ReadBool() ? pr.ReadString() : null;
                    return new GameProfileProperty(pname, pvalue, sig);
                });
                bool strict = r.ReadBool();
                return new ClientboundLoginFinishedPacket(uuid, name, props, SessionId: null)
                {
                    StrictErrorHandling = strict,
                };
            });
}
