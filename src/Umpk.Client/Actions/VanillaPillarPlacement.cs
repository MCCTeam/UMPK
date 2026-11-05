using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;

namespace Umpk.Client.Actions;

/// <summary>Native log/pillar and bounded legacy quartz items whose placement variants retain full-cube bounds and inherited survival rules.</summary>
internal static class VanillaPillarPlacement
{
    public static bool Supports(int protocol, Identifier item, ItemUseOnInfo use, IBlockDataSource blocks, IBlockShapeSource shapes)
    {
        if (protocol is < 47 or > 777 || !item.IsMinecraft || use.Kind != ItemUseOnKind.Unknown || use.PlacedBlock != item
            || !blocks.Blocks.TryGet(item, out RegistryEntry<BlockDefinition> entry))
            return false;
        // Legacy logs keep their default metadata; quartz variants use the same full box. ItemBlock's pre-send query still runs before the actual placement rule.
        if (blocks.IsLegacy)
            return protocol <= 340 && item.Path is "log" or "log2" or "hay_block" or "purpur_pillar" or "bone_block" or "quartz_block";
        BlockDefinition block = entry.Value;
        // Every matching registered BlockItem on the supported fifty datasets is source-bound to the native full-cube pillar family. The item mapping, complete axis schema and original collision grids are all required; a suffix or property name alone does not admit an item.
        if (protocol < 393 || block.StateCount != 3 || block.Properties.Count != 1 || shapes is not IBlockCollisionGridSource grids)
            return false;
        BlockPropertyDefinition axis = block.Properties[0];
        if (axis.Name != "axis" || axis.Values.Count != 3 || axis.Values[0] != "x" || axis.Values[1] != "y" || axis.Values[2] != "z")
            return false;
        for (int state = block.MinStateId; state <= block.MaxStateId; state++)
            if (grids.GetCollisionGrid(new BlockState(blocks, state)) is not { CanonicalFullBlock: true })
                return false;
        return true;
    }

    public static bool SupportsStack(int protocol, ItemStack stack)
    {
        if (!IsLegacyQuartz(protocol, stack))
            return stack.Components.HasNoPatch;
        // The legacy wire count is signed in native PacketBuffer; unrepresentable received counts are outside this bounded placement proof.
        if (stack.Count > sbyte.MaxValue)
            return false;
        foreach (DataComponentEntry entry in stack.Components.Patch)
            if (entry.IsRemoval || !entry.Type.Equals(DataComponents.Damage) || entry.Value is not DamageComponent { Value: >= short.MinValue and <= short.MaxValue })
                return false;
        return true;
    }

    public static bool IsLegacyQuartz(int protocol, ItemStack stack)
        => protocol is >= 47 and <= 340 && !stack.IsEmpty && stack.Item.Id == Identifier.Minecraft("quartz_block") && stack.Item.NetworkId == (155 << 16);

    public static ItemStack CaptureLegacyActionStack(int protocol, ItemStack stack)
        // The received stack preserves the raw signed short for byte round trips. Native PacketBuffer instead constructs a stack, whose constructor clamps negatives before the controller captures an action packet.
        => IsLegacyQuartz(protocol, stack) && stack.Damage is >= short.MinValue and < 0
            ? stack.With(DataComponents.Damage, new DamageComponent(0)) : stack;

}
