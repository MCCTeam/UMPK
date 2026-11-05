using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;

namespace Umpk.Client.Actions;

/// <summary>The held item's native local-destroy rejection, separate from START permission and mining speed. Null retains an unresolved item or local-player identity.</summary>
internal sealed class VanillaCreativeMiningRestriction
{
    private const int FlatteningProtocol = 393;
    private const int CachedOwnPlayerInfoProtocol = 761;
    private const int MaceProtocol = 766;
    private const int CreativeToolFlagProtocol = 770;
    private long? _interactionLifetime;
    private long _playerLifetime;
    private Guid _playerId;
    private TabListEntry? _cachedPlayerInfo;

    public void Synchronize(ClientState state)
    {
        if (_interactionLifetime == state.InteractionLifetime && _playerLifetime == state.PlayerLifetime && _playerId == state.Self.Uuid)
            return;
        _interactionLifetime = state.InteractionLifetime;
        _playerLifetime = state.PlayerLifetime;
        _playerId = state.Self.Uuid;
        _cachedPlayerInfo = null;
    }

    public bool? PreventsLocalRemoval(ItemStack item, ClientState state, int protocol)
    {
        if (item.IsEmpty)
            return false;
        if (!JavaGameData.Registries(protocol).Items.TryGet(item.Item.Id, out _))
            return null;
        string name = item.Item.Id.Path;
        // DebugStick's override rejects on the client; its separate server-side state editing is not predicted here.
        if (protocol >= FlatteningProtocol && name == "debug_stick")
            return true;
        if (protocol >= CreativeToolFlagProtocol)
            return item.Components.Get(DataComponents.Tool) is { CanDestroyBlocksInCreative: false } && state.Self.InstantBuild;
        bool restrictedClass = name is "wooden_sword" or "stone_sword" or "iron_sword" or "golden_sword" or "diamond_sword" or "netherite_sword"
            || protocol >= FlatteningProtocol && name == "trident"
            || protocol >= MaceProtocol && name == "mace";
        if (!restrictedClass)
            return false;
        if (protocol < FlatteningProtocol)
            return state.Self.GameMode == GameMode.Creative;
        return IsOwnCreative(state, protocol);
    }

    // Candle attachment through766 and the existing restricted mining items read the same native local-player object and cache. A preview observes this state without creating a native lookup event.
    internal bool? IsOwnCreative(ClientState state, int protocol) => ReadOwnCreative(state, protocol, remember: true);
    internal bool? PeekOwnCreative(ClientState state, int protocol) => ReadOwnCreative(state, protocol, remember: false);

    private bool? ReadOwnCreative(ClientState state, int protocol, bool remember)
    {
        if (remember)
            Synchronize(state);
        if (state.Self.Uuid == Guid.Empty)
            return null;
        if (protocol < CachedOwnPlayerInfoProtocol)
            return state.TabList.TryGet(state.Self.Uuid, out TabListEntry? direct) && direct.GameMode == GameMode.Creative;
        bool current = _interactionLifetime == state.InteractionLifetime && _playerLifetime == state.PlayerLifetime && _playerId == state.Self.Uuid;
        TabListEntry? info = current ? _cachedPlayerInfo : null;
        // A missing entry is retried; a found native PlayerInfo object survives map removal/replacement until the local player is replaced.
        if (info is null)
            state.TabList.TryGet(state.Self.Uuid, out info);
        if (remember)
            _cachedPlayerInfo = info;
        return info?.GameMode == GameMode.Creative;
    }
}
