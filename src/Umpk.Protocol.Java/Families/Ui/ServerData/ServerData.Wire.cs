using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChatDisplayCodecs
{
    /// <summary>Server data (770/776): a MOTD component and an optional favicon byte array.</summary>
    public static readonly PacketCodec<ClientboundServerDataPacket> ServerDataV1_21_5 =
        PacketCodec<ClientboundServerDataPacket>.Of(
            static (ref PacketWriter w, ClientboundServerDataPacket p, PacketCodecContext _) =>
            {
                WriteModernComponent(ref w, p.Motd);
                w.WriteOptional(p.IconBytes, static (ref PacketWriter sw, byte[] b) => sw.WriteByteArray(b));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                Component motd = ReadModernComponent(ref r);
                byte[]? icon = r.ReadOptional(static (ref PacketReader sr) => sr.ReadByteArray().ToArray());
                return new ClientboundServerDataPacket(motd, icon);
            });

    /// <summary>764 server data: JSON MOTD, optional icon, trailing enforces-secure-chat bool. The MOTD is preserved verbatim (see <see cref="ClientboundServerDataPacket.MotdJsonVerbatim"/>): the vanilla 1.20.2 server serializes a literal MOTD in object form (<c>{"text":"..."}</c>), but <see cref="ComponentJson.ToJsonString"/> collapses a pure-text component to a bare string, so a parse-then-serialize round-trip would drop the object wrapper (observed: recorded 35 bytes vs naive re-encode 26). Writing the captured raw string back keeps the frame byte-exact; a synthesized packet (no verbatim string) falls back to serializing the component.</summary>
    public static PacketCodec<ClientboundServerDataPacket> ServerDataV1_19_4 { get; } =
        PacketCodec<ClientboundServerDataPacket>.Of(
            (ref PacketWriter w, ClientboundServerDataPacket p, PacketCodecContext _) =>
            {
                if (p.MotdJsonVerbatim is { } verbatim)
                    w.WriteString(verbatim, MaxComponentBytes);

                else
                    WriteJson(ref w, p.Motd);

                w.WriteOptional(p.IconBytes, static (ref PacketWriter sw, byte[] b) => sw.WriteByteArray(b));
                w.WriteBool(p.EnforcesSecureChat);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string rawMotd = r.ReadString(MaxComponentBytes);
                Component motd = ComponentJson.Parse(rawMotd, ComponentWireEra.Legacy);
                byte[]? icon = r.ReadOptional(static (ref PacketReader sr) => sr.ReadByteArray().ToArray());
                bool secure = r.ReadBool();
                return new ClientboundServerDataPacket(motd, icon) { EnforcesSecureChat = secure, MotdJsonVerbatim = rawMotd };
            });

    /// <summary>765 server data: NBT MOTD, optional icon, trailing enforces-secure-chat bool.</summary>
    public static PacketCodec<ClientboundServerDataPacket> ServerDataV1_20_3 { get; } =
        MakeServerData(ComponentWire.V1_20_3, hasSecureChatBool: true);

    /// <summary>766-769 server data: NBT MOTD + optional icon, legacy interactions (the enforces-secure-chat bool moved into JoinGame at 1.20.5). The body is unchanged to 769.</summary>
    public static PacketCodec<ClientboundServerDataPacket> ServerDataV1_20_5 { get; } =
        MakeServerData(ComponentWire.V1_20_3, hasSecureChatBool: false);

    /// <summary>759 (1.19) server data: OPTIONAL JSON MOTD, OPTIONAL base64 icon STRING, then the chat-preview bool.</summary>
    public static PacketCodec<ClientboundServerDataPacket> ServerDataV1_19 { get; } =
        MakeServerDataV1_19(hasPreviewsChat: true, hasSecureChatBool: false);

    /// <summary>760 (1.19.1/1.19.2) server data: the 1.19 body plus a SECOND trailing bool, enforces-secure-chat. The recorded 760 frame is exactly one byte longer than the 759 form.</summary>
    public static PacketCodec<ClientboundServerDataPacket> ServerDataV1_19_1 { get; } =
        MakeServerDataV1_19(hasPreviewsChat: true, hasSecureChatBool: true);

    /// <summary>761 (1.19.3) server data: chat preview is gone, so the body is the two optionals plus the single enforces-secure-chat bool. The recorded 761 frame is 29 bytes.</summary>
    public static PacketCodec<ClientboundServerDataPacket> ServerDataV1_19_3 { get; } =
        MakeServerDataV1_19(hasPreviewsChat: false, hasSecureChatBool: true);

    // The 1.19-1.19.3 body: both the MOTD and the icon are OPTIONAL, the icon is a base64 STRING rather than a byte array, and one or two trailing bools follow. 1.19.4 drops the MOTD optional and turns the icon into Optional<byte[]>, which is why the 1.20.2 member covers 762 as well. The MOTD JSON is preserved verbatim for the same reason as 764.
    private static PacketCodec<ClientboundServerDataPacket> MakeServerDataV1_19(bool hasPreviewsChat, bool hasSecureChatBool) =>
        PacketCodec<ClientboundServerDataPacket>.Of(
            (ref PacketWriter w, ClientboundServerDataPacket p, PacketCodecContext _) =>
            {
                w.WriteBool(p.HasMotd);
                if (p.HasMotd)
                    if (p.MotdJsonVerbatim is { } verbatim)
                        w.WriteString(verbatim, MaxComponentBytes);

                    else
                        WriteJson(ref w, p.Motd);

                w.WriteOptional(p.IconBase64, static (ref PacketWriter sw, string s) => sw.WriteString(s));
                if (hasPreviewsChat)
                    w.WriteBool(p.PreviewsChat);

                if (hasSecureChatBool)
                    w.WriteBool(p.EnforcesSecureChat);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                bool hasMotd = r.ReadBool();
                string? rawMotd = hasMotd ? r.ReadString(MaxComponentBytes) : null;
                Component motd = rawMotd is null
                    ? Component.Text(string.Empty)
                    : ComponentJson.Parse(rawMotd, ComponentWireEra.Legacy);
                string? icon = r.ReadOptional(static (ref PacketReader sr) => sr.ReadString());
                bool previews = hasPreviewsChat && r.ReadBool();
                bool secure = hasSecureChatBool && r.ReadBool();
                return new ClientboundServerDataPacket(motd, null)
                {
                    HasMotd = hasMotd,
                    MotdJsonVerbatim = rawMotd,
                    IconBase64 = icon,
                    PreviewsChat = previews,
                    EnforcesSecureChat = secure,
                };
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareServerData(PacketBindings bindings)
    {
        // The 1.19 signing band has three wire eras: 759 is optional-motd + optional-base64-icon + previews-chat; 760 appends a second bool, enforces-secure-chat; 761 drops previews-chat and keeps the other; and 762 is already the 763 body (non-optional JSON motd + optional BYTE-ARRAY icon + enforces-secure-chat). Equal-MOTD frames on 759/760/761/762/763 have lengths 29 / 30 / 29 / 28 / 28. The component encoding then modernizes across 1.20.3-1.20.5; the MOTD's interaction era only modernizes at 1.21.5, so ServerDataV1_20_5 covers 766-769).
        bindings.Packet(UiPackets.Clientbound.ServerData)
            .From(JavaProtocols.V1_19, ChatDisplayCodecs.ServerDataV1_19)
            .From(JavaProtocols.V1_19_1, ChatDisplayCodecs.ServerDataV1_19_1)
            .From(JavaProtocols.V1_19_3, ChatDisplayCodecs.ServerDataV1_19_3)
            .From(JavaProtocols.V1_19_4, ChatDisplayCodecs.ServerDataV1_19_4)
            .From(JavaProtocols.V1_20_3, ChatDisplayCodecs.ServerDataV1_20_3)
            .From(JavaProtocols.V1_20_5, ChatDisplayCodecs.ServerDataV1_20_5)
            .From(JavaProtocols.V1_21_5, ChatDisplayCodecs.ServerDataV1_21_5);
    }
}
