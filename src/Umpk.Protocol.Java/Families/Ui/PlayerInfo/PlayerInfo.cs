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
        /// <summary>Legacy single-packet player list item (<c>minecraft:player_info</c>, 47-760). This is the pre-1.19.3 tab-list wire form: one action for a batch of entries. 1.19.3 (761) replaced it with the <see cref="PlayerInfoUpdate"/> / <see cref="PlayerInfoRemove"/> pair.</summary>
        public static readonly PacketType<ClientboundLegacyPlayerListItemPacket> LegacyPlayerListItem =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("player_info"));
    }
}

/// <summary>Legacy player list item (47-760): a single action for a batch of entries. This is the whole pre-1.19.3 tab list; 1.19.3 split it into player-info-update plus player-info-remove.</summary>
public sealed record ClientboundLegacyPlayerListItemPacket(
    LegacyPlayerListAction Action,
    IReadOnlyList<LegacyPlayerListEntry> Entries) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.LegacyPlayerListItem;
}

/// <summary>The legacy (47-760) player-list-item action (add=0, gamemode=1, latency=2, display-name=3, remove=4).</summary>
public enum LegacyPlayerListAction : byte
{
    /// <summary>Add player (full profile).</summary>
    AddPlayer = 0,

    /// <summary>Update game mode.</summary>
    UpdateGameMode = 1,

    /// <summary>Update latency.</summary>
    UpdateLatency = 2,

    /// <summary>Update display name.</summary>
    UpdateDisplayName = 3,

    /// <summary>Remove player.</summary>
    RemovePlayer = 4,
}

/// <summary>One entry of the legacy (47-760) player-list-item packet; per-action fields keep their defaults when unused. Only the add action carries the name, properties and the full add payload; the update actions carry the single field they name.</summary>
public sealed record LegacyPlayerListEntry(
    Guid ProfileId,
    string? Name,
    IReadOnlyList<GameProfileProperty>? Properties,
    int GameMode,
    int Latency,
    Component? DisplayName)
{
    /// <summary>The trailing nullable profile public key the 759/760 (1.19 / 1.19.1-1.19.2) add-player payload carries (<c>Optional&lt;ProfilePublicKey.Data&gt;</c>: expiry epoch-millis, DER SubjectPublicKeyInfo key bytes, Mojang key signature). Null on every other era, on every non-add action, and when the sending player has no signing key.</summary>
    public ProfilePublicKeyData? ProfileKey { get; init; }
}
