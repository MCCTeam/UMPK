using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Text;

namespace Umpk.Game.Inventory;

/// <summary>The read model of an open container window. It pairs a <see cref="ContainerSnapshot"/> (slots, cursor, state id) with the window identity, the menu-type handle, the title, the slot layout, the raw container properties, and any attached merchant offers. The type is immutable; state updates replace the view.</summary>
public sealed class ContainerView
{
    /// <summary>Builds a container view.</summary>
    /// <param name="windowId">The window id (0 is the player inventory).</param>
    /// <param name="menuType">The menu-type registry handle.</param>
    /// <param name="title">The window title component.</param>
    /// <param name="snapshot">The slot/cursor/state-id snapshot.</param>
    /// <param name="layout">The slot layout for this menu type.</param>
    /// <param name="properties">The raw container properties (furnace progress, enchant seed, ...).</param>
    /// <param name="trades">The merchant offers, when this is a merchant window.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public ContainerView(
        int windowId,
        RegistryEntry<MenuTypeDefinition> menuType,
        Component title,
        ContainerSnapshot snapshot,
        SlotLayout layout,
        IReadOnlyDictionary<int, int>? properties = null,
        MerchantOffers? trades = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(layout);
        WindowId = windowId;
        MenuType = menuType;
        Title = title;
        Snapshot = snapshot;
        Layout = layout;
        Properties = properties ?? new Dictionary<int, int>();
        Trades = trades;
    }

    /// <summary>The window id (0 is the player inventory).</summary>
    public int WindowId { get; }

    /// <summary>The menu-type registry handle; per-version numbering is dataset content.</summary>
    public RegistryEntry<MenuTypeDefinition> MenuType { get; }

    /// <summary>The window title.</summary>
    public Component Title { get; }

    /// <summary>The slot/cursor/state-id snapshot backing this view.</summary>
    public ContainerSnapshot Snapshot { get; }

    /// <summary>The slot layout (roles, output slots, shift-click ranges).</summary>
    public SlotLayout Layout { get; }

    /// <summary>The container state/revision id.</summary>
    public int StateId => Snapshot.StateId;

    /// <summary>The number of slots (excluding the cursor).</summary>
    public int SlotCount => Snapshot.SlotCount;

    /// <summary>The carried (cursor) stack.</summary>
    public ItemStack Cursor => Snapshot.Cursor;

    /// <summary>The contents of a slot.</summary>
    public ItemStack GetSlot(int index) => Snapshot.GetSlot(index);

    /// <summary>The raw container properties keyed by property index.</summary>
    public IReadOnlyDictionary<int, int> Properties { get; }

    /// <summary>The merchant offers when this is a merchant window; otherwise null.</summary>
    public MerchantOffers? Trades { get; }

    /// <summary>Returns a copy of this view with a new backing snapshot (e.g. after a click prediction).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    public ContainerView WithSnapshot(ContainerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ContainerView(WindowId, MenuType, Title, snapshot, Layout, Properties, Trades);
    }

    /// <summary>Returns a copy of this view with merchant offers attached or replaced.</summary>
    public ContainerView WithTrades(MerchantOffers? trades) =>
        new(WindowId, MenuType, Title, Snapshot, Layout, Properties, trades);
}
