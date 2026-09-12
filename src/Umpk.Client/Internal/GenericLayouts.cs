using Umpk.Game.Inventory;

namespace Umpk.Client.Internal;

/// <summary>Builds a generic <see cref="SlotLayout"/> for a window of a given size when the exact menu-type layout is not modeled. Every slot is Storage with a single quick-move target spanning the whole window; sufficient for prediction of pickup/place/throw/pickup-all and a best-effort shift-click.</summary>
internal static class GenericLayouts
{
    public static SlotLayout For(int slotCount, bool hasOpenContainer)
    {
        if (slotCount <= 0)
            slotCount = 1;

        var roles = new SlotRole[slotCount];
        Array.Fill(roles, SlotRole.Storage);

        // Every regular open menu appends ALL 36 player storage/hotbar slots after its own slots, not merely the hotbar's final nine. Using slotCount - 9 made a large chest's main-inventory slots quick-move into the hotbar in the prediction, while vanilla moved them into the chest. The changed-slot set was then rejected by the server, producing an open-without-deposit loop. The player inventory window itself keeps the previous conservative split because it also has crafting, armor and offhand slots that this generic layout cannot classify precisely.
        int split = hasOpenContainer && slotCount >= 36
            ? slotCount - 36
            : slotCount > 9 ? slotCount - 9 : slotCount;
        var perSlot = new IReadOnlyList<SlotRange>[slotCount];
        for (int i = 0; i < slotCount; i++)
            perSlot[i] = i < split
                ? [new SlotRange(split, slotCount)]
                : [new SlotRange(0, split)];

        return new SlotLayout(roles, perSlot);
    }
}
