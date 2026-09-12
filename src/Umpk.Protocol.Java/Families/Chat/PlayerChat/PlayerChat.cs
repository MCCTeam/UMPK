using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        // chat auxiliaries

        /// <summary>Signed player chat (<c>minecraft:player_chat</c>, signing era 759-763).</summary>
        public static readonly PacketType<ClientboundPlayerChatPacket> PlayerChat =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("player_chat"));
    }
}

/// <summary>Inbound (clientbound) signed chat, <c>minecraft:player_chat</c>, across the whole signing era (protocols 759-776). The generations differ on the wire (see <c>Codecs.ChatCodecs</c>): v1 (759) is a flat signed component + salt/signature; v2 (760) nests a <c>PlayerChatMessage</c> (header + signature + body); v3 (761 onward) is sender uuid + message index + nullable signature + signed body (content string + timestamp + salt + last-seen) + filter + chat type. Within v3 the components move from JSON strings to network NBT at 765, and 770 prefixes the packet with <see cref="GlobalIndex"/>. This record is the union of those fields; era-specific carriers are init-only and null or zero when not on that era's wire. (1.19 / 1.19.2 / 1.19.3 / 1.20.4 / 1.21.5 / 26.2).</summary>
public sealed record ClientboundPlayerChatPacket(
    Guid Sender,
    int Index,
    byte[]? Signature,
    string? SignedContent,
    long TimestampMillis,
    long Salt,
    Component? UnsignedContent,
    int ChatTypeId,
    Component SenderName,
    Component? TargetName) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.PlayerChat;

    /// <summary>770+ only: the server-global message index that leads the packet. Zero on every earlier protocol, where the field is absent from the wire.</summary>
    public int GlobalIndex { get; init; }

    /// <summary>v1 (759) only: the signed content as a JSON component (v2/v3 sign a plain string).</summary>
    public Component? SignedComponent { get; init; }

    /// <summary>v2 (760) only: the RAW JSON text of the decorated component that rode inside the signed body, exactly as it arrived, or null when the frame carried no decoration.</summary>
    /// <remarks>This is carried as text rather than as the parsed <see cref="SignedComponent"/> because it is signature material, not display material. The body digest includes stable JSON for the decorated component, derived from the same JsonElement tree this text encodes, so the tree has to survive the trip unmodified. A component MODEL cannot promise that: anything it normalises, drops or reorders changes the reconstructed digest and fails an otherwise valid signature. See <c>Umpk.Protocol.Java.Signing.VanillaStableJson</c>.</remarks>
    public string? SignedComponentJson { get; init; }

    /// <summary>v2 (760) only: the previous-message signature from the signed header (nullable).</summary>
    public byte[]? PreviousSignature { get; init; }

    /// <summary>v2/v3: the last-seen message signatures. v3 packs each entry as either a signature-cache id or a full inline signature; v2 always carries full signatures. Empty when none.</summary>
    public IReadOnlyList<PackedMessageSignature> LastSeen { get; init; } = [];

    /// <summary>v2/v3: the filter-mask type (0 pass-through, 1 fully filtered, 2 partially filtered).</summary>
    public int FilterType { get; init; }

    /// <summary>v2/v3: the partially-filtered bitset words, present only when <see cref="FilterType"/> == 2. The wire form is a VarInt-length array of longs.</summary>
    public IReadOnlyList<long> FilterBits { get; init; } = [];
}

/// <summary>
/// One entry of a last-seen message window, covering both signing generations that carry one.
/// <para>v3 (1.19.3 onward) carries either a reference into the receiver's signature cache (<see cref="Id"/> >= 0, no bytes on the wire) or a full inline 256-byte signature (<see cref="Id"/> == <see cref="FullSignatureId"/>). The wire form is a single VarInt <c>id + 1</c>, with 0 meaning a full signature follows. A steady-state server packs most last-seen entries as cache ids, so an id-only entry is the common case rather than an edge case.</para>
/// <para>v2 (1.19.1/1.19.2) carries a pair of the acknowledged message's own SENDER profile id and that message's full signature. There is no packing on that generation, so every v2 entry is a full signature and every v2 entry names a profile id.</para>
/// </summary>
/// <param name="Id">The signature-cache id, or -1 when <paramref name="FullSignature"/> carries the bytes.</param>
/// <param name="FullSignature">The inline 256-byte signature, or null for a cache reference.</param>
/// <param name="ProfileId">
/// v2 only: the profile id of the player who sent the acknowledged message. It is load bearing rather than decorative, because 1.19.2 hashes each entry's own UUID into the signed body as <c>0x46</c>, the UUID's most and least significant longs, then the signature bytes. A window built with the wrong UUIDs produces the wrong body digest and every message from that peer fails verification.
/// <para>Always <see cref="Guid.Empty"/> on v3, and provably so: 1.19.3 onward has no profile id anywhere in this structure. Neither the v3 wire nor its signed payload, which contains only the entry count and each entry's bytes, has a slot for it.</para>
/// </param>
public readonly record struct PackedMessageSignature(int Id, byte[]? FullSignature, Guid ProfileId)
{
    /// <summary>The id marking a full inline signature.</summary>
    public const int FullSignatureId = -1;

    /// <summary>A v3 full-signature entry carrying raw signature bytes and no profile id.</summary>
    public static PackedMessageSignature Full(byte[] signature) => new(FullSignatureId, signature, Guid.Empty);

    /// <summary>A v3 cache-reference entry carrying only an id.</summary>
    public static PackedMessageSignature Cached(int id) => new(id, null, Guid.Empty);

    /// <summary>A v2 entry: the acknowledged message's sender profile id paired with its full signature. Both halves ride the 1.19.1/1.19.2 wire and both are hashed into the signed body.</summary>
    public static PackedMessageSignature Full(Guid profileId, byte[] signature) =>
        new(FullSignatureId, signature, profileId);
}
