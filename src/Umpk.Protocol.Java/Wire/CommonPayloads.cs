using Umpk.Protocol.Java.Packets;
using Umpk.Text;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Wire shapes registered in more than one phase protocol. UMPK's packet identity is <c>(phase, flow, Identifier)</c>, so each of these keeps its own <c>PacketType</c> and its own record; only the BODY lives here. The cookie exchange's three-phase primitives are the older members of the same idea and stay in <see cref="LoginConfigWire"/>, the dialog click body in <see cref="UiCodecShared"/>, and the disconnect reason in <see cref="ComponentWire"/>.</summary>
/// <remarks>Plain static methods rather than a <c>PacketCodec.Map</c> adapter: <c>Map</c> costs an extra delegate invocation per decode and two closures per binding. For keep-alive that is unmeasurable, but disconnect arrives inside a bundle, so the rule is uniform.</remarks>
internal static class CommonPayloads
{
    /// <summary>The default main hand: right, ordinal 1.</summary>
    private const int DefaultMainHand = 1;

    /// <summary>The 1.12.2+ keep-alive body: one big-endian long, in every phase that carries it.</summary>
    internal static long ReadKeepAliveId(ref PacketReader r) => r.ReadLong();

    /// <summary>The write half of <see cref="ReadKeepAliveId"/>.</summary>
    internal static void WriteKeepAliveId(ref PacketWriter w, long id) => w.WriteLong(id);

    /// <summary>The 47-338 keep-alive body: a VarInt id, widened into the record's long.</summary>
    internal static long ReadLegacyKeepAliveId(ref PacketReader r) => r.ReadVarInt();

    /// <summary>The write half of <see cref="ReadLegacyKeepAliveId"/>.</summary>
    internal static void WriteLegacyKeepAliveId(ref PacketWriter w, long id) => w.WriteVarInt((int)id);

    /// <summary>The ping/pong body: a big-endian int id the peer echoes back.</summary>
    internal static int ReadPingId(ref PacketReader r) => r.ReadInt();

    /// <summary>The write half of <see cref="ReadPingId"/>.</summary>
    internal static void WritePingId(ref PacketWriter w, int id) => w.WriteInt(id);

    /// <summary>The ping-request/pong-response body: a big-endian long nonce.</summary>
    internal static long ReadPingPayload(ref PacketReader r) => r.ReadLong();

    /// <summary>The write half of <see cref="ReadPingPayload"/>.</summary>
    internal static void WritePingPayload(ref PacketWriter w, long payload) => w.WriteLong(payload);

    /// <summary>The transfer body: the destination host and its VarInt port.</summary>
    internal static (string Host, int Port) ReadTransfer(ref PacketReader r) => (r.ReadString(), r.ReadVarInt());

    /// <summary>The write half of <see cref="ReadTransfer"/>.</summary>
    internal static void WriteTransfer(ref PacketWriter w, string host, int port)
    {
        w.WriteString(host);
        w.WriteVarInt(port);
    }

    /// <summary>The resource-pack-pop body: the optional uuid of the pack to drop.</summary>
    internal static Guid? ReadResourcePackId(ref PacketReader r) => r.ReadBool() ? r.ReadUuid() : null;

    /// <summary>The write half of <see cref="ReadResourcePackId"/>.</summary>
    internal static void WriteResourcePackId(ref PacketWriter w, Guid? id) =>
        w.WriteOptionalStruct(id, static (ref PacketWriter iw, Guid value) => iw.WriteUuid(value));

    /// <summary>The client's answer to a pack push: the pack uuid from 1.20.3 and the action ordinal. Below that the uuid is not on the wire at all, in either phase, and writing it puts sixteen bytes in front of the ordinal.</summary>
    /// <param name="r">The reader.</param>
    /// <param name="hasId">Whether this era carries the pack uuid.</param>
    /// <returns>The uuid (empty when the era has none) and the action ordinal.</returns>
    internal static (Guid Id, int Action) ReadResourcePackResponse(ref PacketReader r, bool hasId) =>
        (hasId ? r.ReadUuid() : Guid.Empty, r.ReadVarInt());

    /// <summary>The write half of <see cref="ReadResourcePackResponse"/>.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="id">The pack uuid, written only when the era carries one.</param>
    /// <param name="action">The action ordinal.</param>
    /// <param name="hasId">Whether this era carries the pack uuid.</param>
    internal static void WriteResourcePackResponse(ref PacketWriter w, Guid id, int action, bool hasId)
    {
        if (hasId)
            w.WriteUuid(id);

        w.WriteVarInt(action);
    }

    /// <summary>The pack-push body: the uuid (1.20.3+), the url, the 40-character hash, the required flag and an optional prompt component in the era's own dialect.</summary>
    /// <param name="r">The reader.</param>
    /// <param name="hasId">Whether this era carries the pack uuid.</param>
    /// <param name="text">The era's component dialect.</param>
    /// <returns>The decoded fields.</returns>
    internal static ResourcePackPushFields ReadResourcePackPush(ref PacketReader r, bool hasId, ComponentWire text)
    {
        Guid id = hasId ? r.ReadUuid() : Guid.Empty;
        string url = r.ReadString();
        string hash = r.ReadString(40);
        bool required = r.ReadBool();
        return new ResourcePackPushFields(id, url, hash, required, r.ReadOptional(text.Read));
    }

    /// <summary>The write half of <see cref="ReadResourcePackPush"/>.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="fields">The fields to write.</param>
    /// <param name="hasId">Whether this era carries the pack uuid.</param>
    /// <param name="text">The era's component dialect.</param>
    internal static void WriteResourcePackPush(ref PacketWriter w, ResourcePackPushFields fields, bool hasId, ComponentWire text)
    {
        if (hasId)
            w.WriteUuid(fields.Id);

        w.WriteString(fields.Url);
        w.WriteString(fields.Hash, 40);
        w.WriteBool(fields.Required);
        w.WriteOptional(fields.Prompt, text.Write);
    }

    /// <summary>One server-links entry: an <c>Either&lt;KnownLinkType, Component&gt;</c> then the url.</summary>
    /// <remarks>The leading boolean selects the branch: <see langword="true"/> carries the known type id and <see langword="false"/> the custom label, and the two phase copies of this body disagreed about which.</remarks>
    /// <param name="r">The reader.</param>
    /// <param name="text">The era's component dialect for a custom label.</param>
    /// <returns>The entry.</returns>
    internal static ServerLinkEntry ReadServerLink(ref PacketReader r, ComponentWire text)
    {
        bool isKnown = r.ReadBool();
        int? known = isKnown ? r.ReadVarInt() : null;
        Component? label = isKnown ? null : text.Read(ref r);
        return new ServerLinkEntry(known, label, r.ReadString());
    }

    /// <summary>The write half of <see cref="ReadServerLink"/>.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="link">The entry to write.</param>
    /// <param name="text">The era's component dialect for a custom label.</param>
    internal static void WriteServerLink(ref PacketWriter w, ServerLinkEntry link, ComponentWire text)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.KnownTypeId is { } known)
        {
            w.WriteBool(true);
            w.WriteVarInt(known);
        }
        else
        {
            w.WriteBool(false);
            text.Write(ref w, link.Label ?? throw new ProtocolViolationException("A custom server link requires a label."));
        }

        w.WriteString(link.Url);
    }

    /// <summary>The client-information body, in the shape one era carries it.</summary>
    /// <param name="r">The reader.</param>
    /// <param name="wire">The era's field set.</param>
    /// <returns>The decoded fields.</returns>
    internal static ClientInformationFields ReadClientInformation(ref PacketReader r, ClientInformationWire wire)
    {
        string language = r.ReadString(16);
        sbyte viewDistance = r.ReadSByte();
        int chatVisibility = wire.ChatVisibilityIsVarInt ? r.ReadVarInt() : r.ReadByte();
        bool chatColors = r.ReadBool();
        byte modelCustomisation = r.ReadByte();
        int mainHand = wire.HasMainHand ? r.ReadVarInt() : DefaultMainHand;
        bool textFiltering = wire.HasTextFiltering && r.ReadBool();
        bool allowsListing = wire.HasAllowsListing && r.ReadBool();
        int particleStatus = wire.HasParticleStatus ? r.ReadVarInt() : 0;
        return new ClientInformationFields(
            language, viewDistance, chatVisibility, chatColors, modelCustomisation, mainHand,
            textFiltering, allowsListing, particleStatus);
    }

    /// <summary>The write half of <see cref="ReadClientInformation"/>.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="fields">The fields to write.</param>
    /// <param name="wire">The era's field set.</param>
    internal static void WriteClientInformation(ref PacketWriter w, ClientInformationFields fields, ClientInformationWire wire)
    {
        w.WriteString(fields.Language, 16);
        w.WriteSByte(fields.ViewDistance);
        if (wire.ChatVisibilityIsVarInt)
            w.WriteVarInt(fields.ChatVisibility);

        else
            w.WriteByte(unchecked((byte)fields.ChatVisibility));

        w.WriteBool(fields.ChatColors);
        w.WriteByte(fields.ModelCustomisation);
        if (wire.HasMainHand)
            w.WriteVarInt(fields.MainHand);

        if (wire.HasTextFiltering)
            w.WriteBool(fields.TextFilteringEnabled);

        if (wire.HasAllowsListing)
            w.WriteBool(fields.AllowsListing);

        if (wire.HasParticleStatus)
            w.WriteVarInt(fields.ParticleStatus);

    }
}

/// <summary>The resource-pack-push fields, minus the phase-bound record that carries them.</summary>
/// <param name="Id">The pack uuid, <see cref="Guid.Empty"/> on the eras that have none.</param>
/// <param name="Url">The pack url.</param>
/// <param name="Hash">The 40-character SHA-1 hash, or the empty string.</param>
/// <param name="Required">Whether the server requires the pack.</param>
/// <param name="Prompt">The optional prompt component.</param>
internal readonly record struct ResourcePackPushFields(
    Guid Id,
    string Url,
    string Hash,
    bool Required,
    Component? Prompt);

/// <summary>The client-information fields, minus the phase-bound record that carries them.</summary>
/// <param name="Language">The locale, capped at 16 characters.</param>
/// <param name="ViewDistance">The client's render distance in chunks.</param>
/// <param name="ChatVisibility">The chat-visibility ordinal.</param>
/// <param name="ChatColors">Whether the client renders chat colours.</param>
/// <param name="ModelCustomisation">The skin-part bitmask.</param>
/// <param name="MainHand">The main-hand ordinal.</param>
/// <param name="TextFilteringEnabled">Whether server-side text filtering is on.</param>
/// <param name="AllowsListing">Whether the player may appear in the server list.</param>
/// <param name="ParticleStatus">The particle-status ordinal.</param>
internal readonly record struct ClientInformationFields(
    string Language,
    sbyte ViewDistance,
    int ChatVisibility,
    bool ChatColors,
    byte ModelCustomisation,
    int MainHand,
    bool TextFilteringEnabled,
    bool AllowsListing,
    int ParticleStatus);

/// <summary>Which client-information fields one era puts on the wire. Five generations exist and both phases draw from the same five: 1.8 has a byte chat visibility and no main hand, 1.9 widens the visibility and adds the main hand, 1.17 adds text filtering, 1.18 adds allows-listing, and 1.21.2 adds the particle status.</summary>
/// <param name="ChatVisibilityIsVarInt">False only on 1.8, where the ordinal is a plain byte.</param>
/// <param name="HasMainHand">True from 1.9.</param>
/// <param name="HasTextFiltering">True from 1.17.</param>
/// <param name="HasAllowsListing">True from 1.18.</param>
/// <param name="HasParticleStatus">True from 1.21.2.</param>
internal readonly record struct ClientInformationWire(
    bool ChatVisibilityIsVarInt,
    bool HasMainHand,
    bool HasTextFiltering,
    bool HasAllowsListing,
    bool HasParticleStatus)
{
    /// <summary>47: byte chat visibility, no main hand.</summary>
    internal static ClientInformationWire V1_8 { get; } = new(false, false, false, false, false);

    /// <summary>107-754: VarInt chat visibility and the main hand.</summary>
    internal static ClientInformationWire V1_9 { get; } = V1_8 with { ChatVisibilityIsVarInt = true, HasMainHand = true };

    /// <summary>755/756: adds the text-filtering flag.</summary>
    internal static ClientInformationWire V1_17 { get; } = V1_9 with { HasTextFiltering = true };

    /// <summary>757-767: adds the allows-listing flag.</summary>
    internal static ClientInformationWire V1_18 { get; } = V1_17 with { HasAllowsListing = true };

    /// <summary>768+: adds the particle-status VarInt.</summary>
    internal static ClientInformationWire V1_21_2 { get; } = V1_18 with { HasParticleStatus = true };
}
