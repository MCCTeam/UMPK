using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Tests.Items;

namespace Umpk.Game.Tests.Inventory;

/// <summary>Builds <see cref="ContainerSnapshot"/> instances slot-by-slot for click scenarios.</summary>
internal sealed class SnapshotBuilder
{
    private readonly ItemStack[] _slots;
    private ItemStack _cursor = ItemStack.Empty;
    private int _stateId;

    public SnapshotBuilder(int slotCount)
    {
        _slots = new ItemStack[slotCount];
        Array.Fill(_slots, ItemStack.Empty);
    }

    public SnapshotBuilder Slot(int index, string item, int count)
    {
        _slots[index] = ItemTestData.Stack(item, count);
        return this;
    }

    public SnapshotBuilder Slot(int index, ItemStack stack)
    {
        _slots[index] = stack;
        return this;
    }

    public SnapshotBuilder Cursor(string item, int count)
    {
        _cursor = ItemTestData.Stack(item, count);
        return this;
    }

    public SnapshotBuilder StateId(int stateId)
    {
        _stateId = stateId;
        return this;
    }

    public ContainerSnapshot Build() => new(_slots, _cursor, _stateId);
}
