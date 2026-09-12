using Umpk.Client.Internal;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client;

/// <summary>
/// Which version-optional client actions the negotiated protocol version can actually perform.
/// <para>Every property here is the OUTBOUND-TABLE answer for the exact packet record the matching action constructs, which is the same predicate the encoder applies. Three things make an identifier lookup (<c>the version's serverbound play table holds minecraft:X</c>) the wrong question, and all three are live in this codebase:</para>
/// <list type="number">
/// <item>A registered-but-unimplemented MARKER occupies a real wire id, so the identifier is present and
/// the encode throws (<c>sign_update</c> on protocol 47).</item>
/// <item>An era can bind a DIFFERENT record under the same identifier: <c>edit_book</c> on 393-753 binds
/// the legacy item-stack record, which <see cref="Actions.InventoryActions.EditBookAsync"/> never constructs, so the identifier is present and the encode throws.</item>
/// <item>An action can span two records that are chosen by era rather than by caller
/// (<c>place_recipe</c>), where the identifier cannot say which form is live.</item>
/// </list>
/// <para>A true answer means the matching action will not raise <see cref="ActionNotSupportedException"/>. Resolved once per session from the negotiated version; the only phase-sensitive member is <see cref="CanSubmitDialog"/>, which is documented on itself.</para>
/// </summary>
public sealed class ClientActionCapabilities
{
    private static readonly Identifier CustomPayloadId = Identifier.Minecraft("custom_payload");

    /// <summary>The LAST protocol whose server still handles the <c>MC|ItemName</c> anvil-rename channel: 340 (1.12.2). 1.13 replaced the legacy plugin channel with the dedicated <c>rename_item</c> packet.</summary>
    private const int LastLegacyItemNameProtocol = 340;

    private readonly WireIndex _wire;
    private readonly Func<ProtocolPhase> _currentPhase;

    internal ClientActionCapabilities(WireIndex wire, int protocol, Func<ProtocolPhase> currentPhase)
    {
        _wire = wire;
        _currentPhase = currentPhase;
        Protocol = protocol;

        CanClickContainerButton = wire.CanSendPlay(ItemPackets.Serverbound.ContainerButtonClick);
        CanEditBook = wire.CanSendPlay(UiPackets.Serverbound.EditBook);
        CanPlaceRecipe = wire.CanSendPlay(ItemPackets.Serverbound.PlaceRecipe);
        CanPlaceRecipeByName = wire.CanSendPlay(ItemPackets.Serverbound.PlaceRecipeByName);
        CanUpdateSign = wire.CanSendPlay(UiPackets.Serverbound.SignUpdate);
        CanSpectatorTeleport = wire.CanSendPlay(EntityPackets.Serverbound.TeleportToEntity);
        CanUpdateCommandBlock = wire.CanSendPlay(WorldPackets.Serverbound.SetCommandBlock);

        // Place/use route across the 1.9 boundary: the modern packet where it exists, the 1.8 block_place otherwise. The capability is the OR, because the action itself picks.
        bool legacyBlockPlace = wire.CanSendPlay(ItemPackets.Serverbound.LegacyBlockPlace);
        CanPlaceBlock = wire.CanSendPlay(ItemPackets.Serverbound.UseItemOn) || legacyBlockPlace;
        CanUseItem = wire.CanSendPlay(ItemPackets.Serverbound.UseItem) || legacyBlockPlace;

        // The plugin-channel send writes a RAW frame (wire id + hand-built body) rather than encoding a
        // record, so the identifier lookup is the right question here and the outbound table is not:
        // play custom_payload is a marker on every version by design.
        CanSendPluginMessage = wire.ServerboundPlay(CustomPayloadId) >= 0;

        // The anvil rename routes across the 1.13 boundary exactly the way place/use route across 1.9: the dedicated rename_item packet where it exists, the MC|ItemName plugin channel before that. 1.13 deleted processVanilla250Packet along with every MC| channel, so the legacy route is bounded ABOVE at 1.12.2 rather than being "whenever rename_item is missing": claiming it on a version whose server has no handler would be the false-success shape this class exists to stop.
        CanRenameItemDirect = wire.CanSendPlay(ItemPackets.Serverbound.RenameItem);
        CanRenameItemLegacy = !CanRenameItemDirect
            && protocol <= LastLegacyItemNameProtocol
            && CanSendPluginMessage;
        CanRenameItem = CanRenameItemDirect || CanRenameItemLegacy;
    }

    /// <summary>The negotiated wire protocol number these answers were resolved for.</summary>
    public int Protocol { get; }

    /// <summary>Whether <see cref="Actions.InventoryActions.ClickContainerButtonAsync"/> can send.</summary>
    public bool CanClickContainerButton { get; }

    /// <summary>Whether <see cref="Actions.InventoryActions.RenameItemAsync"/> can send the anvil rename at all, by EITHER route: the dedicated <c>rename_item</c> packet from 1.13, or the <c>MC|ItemName</c> plugin channel on 1.8 through 1.12.2. True on every supported version. A consumer can branch on this answer without knowing which route carries the rename.</summary>
    public bool CanRenameItem { get; }

    /// <summary>Whether the rename goes out as the dedicated 1.13+ <c>rename_item</c> packet. Internal because which of the two wire routes carries it is the ACTION's business, not a branch a consumer should have to make: both routes produce the same server-side effect.</summary>
    internal bool CanRenameItemDirect { get; }

    /// <summary>Whether the rename goes out on the pre-1.13 <c>MC|ItemName</c> plugin channel. Mutually exclusive with <see cref="CanRenameItemDirect"/>.</summary>
    internal bool CanRenameItemLegacy { get; }

    /// <summary>Whether <see cref="Actions.InventoryActions.EditBookAsync"/> can send. False on 393-753 even though those versions register <c>edit_book</c>: that band binds the legacy item-stack record.</summary>
    public bool CanEditBook { get; }

    /// <summary>Whether <see cref="Actions.InventoryActions.PlaceRecipeAsync"/> (the 1.21.2+ network-id form) can send.</summary>
    public bool CanPlaceRecipe { get; }

    /// <summary>Whether <see cref="Actions.InventoryActions.PlaceRecipeByNameAsync"/> (the pre-1.21.2 identifier form) can send.</summary>
    public bool CanPlaceRecipeByName { get; }

    /// <summary>Whether <see cref="Actions.InteractionActions.UpdateSignAsync"/> can send. False on 47, where the lines are JSON components.</summary>
    public bool CanUpdateSign { get; }

    /// <summary>Whether <see cref="Actions.InteractionActions.SpectatorTeleportAsync"/> can send.</summary>
    public bool CanSpectatorTeleport { get; }

    /// <summary>Whether <see cref="Actions.InteractionActions.UpdateCommandBlockAsync"/> can send (1.13+).</summary>
    public bool CanUpdateCommandBlock { get; }

    /// <summary>Whether <see cref="Actions.InteractionActions.PlaceBlockAsync"/> can send on either side of the 1.9 boundary.</summary>
    public bool CanPlaceBlock { get; }

    /// <summary>Whether <see cref="Actions.InteractionActions.UseItemAsync"/> can send on either side of the 1.9 boundary.</summary>
    public bool CanUseItem { get; }

    /// <summary>Whether a plugin can put a custom-payload frame on the wire.</summary>
    public bool CanSendPluginMessage { get; }

    /// <summary>Whether <see cref="Actions.DialogActions"/> can answer a dialog right now. Unlike the others this is evaluated per read: <c>custom_click_action</c> is a COMMON packet registered separately in the configuration and play phases under distinct identities, and the answer has to be the live phase's.</summary>
    public bool CanSubmitDialog =>
        _currentPhase() == ProtocolPhase.Configuration
            ? _wire.CanSendConfiguration(LoginFamilyPackets.Config.CustomClickAction)
            : _wire.CanSendPlay(UiPackets.Serverbound.CustomClickAction);
}
