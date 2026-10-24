using Umpk.Game.Blocks;

namespace Umpk.Client.Actions;

/// <summary>The native local result. Unknown preserves packet-only behavior and does not assert a server outcome.</summary>
internal enum BlockUseResult
{
    Unknown,
    Pass,
    Fail,
    Consume,
    Success,
}

internal enum BlockUseSwing
{
    None,
    Local,
    Packet,
}

/// <summary>Separates a native pre-send rejection from a result predicted after the use packet is committed.</summary>
internal readonly record struct BlockUsePrediction(bool SendPacket, BlockUseResult Result, BlockUseSwing Swing)
{
    public static BlockUsePrediction Unknown => new(true, BlockUseResult.Unknown, BlockUseSwing.None);
    public static BlockUsePrediction Pass => new(true, BlockUseResult.Pass, BlockUseSwing.None);
    public static BlockUsePrediction Fail => new(true, BlockUseResult.Fail, BlockUseSwing.None);
    public static BlockUsePrediction Consume => new(true, BlockUseResult.Consume, BlockUseSwing.None);
    public static BlockUsePrediction Rejected => new(false, BlockUseResult.Fail, BlockUseSwing.None);
    public static BlockUsePrediction Success(bool localSwing) => new(true, BlockUseResult.Success, localSwing ? BlockUseSwing.Local : BlockUseSwing.Packet);
}

internal readonly record struct BlockUseActivationProfile(bool HasActivationRules, bool EmptyHandActivationIsMainHandOnly, bool PoweredButtonConsumes, bool HasMenuActivationRules, bool HasDoorActivationRules, bool LegacyDoorLowerCell, bool IronDoorConsumesUse, bool HasTrapdoorGateRules, bool SmithingTablePassesUse, bool HasStateOnlyJukeboxRules)
{
    // 1.14.4-client-decompiled net/minecraft/world/level/block/LeverBlock.java:75 returns true on the client. Later LeverBlock.use/useWithoutItem returns SUCCESS on the client. MultiPlayerGameMode introduces main-hand-only useWithoutItem fallback in 1.20.5; the source audit covers every release through 26.3.
    public static BlockUseActivationProfile ForProtocol(int protocol) => new(protocol is >= 498 and <= 777, protocol >= 766, protocol >= 573, protocol is >= 47 and <= 777, protocol is >= 47 and <= 777, protocol <= 340, protocol <= 210, protocol is >= 47 and <= 777, protocol < 735, protocol is >= 47 and <= 761);

    public bool RequiresNativeTerrain(BlockState block)
        => block.Block.Id.IsMinecraft && (HasMenuActivationRules && IsMenuBlock(block) || HasDoorActivationRules && IsDoor(block) || HasTrapdoorGateRules && (IsTrapdoor(block) || IsFenceGate(block)));

    public bool RequiresLowerCell(BlockState block)
        => HasDoorActivationRules && LegacyDoorLowerCell && IsDoor(block) && block.Block.Id.Path != "iron_door" && (block.LegacyMeta & 8) != 0;

    public bool TryPredict(BlockState block, out BlockUseResult result, BlockState lower = default)
    {
        result = BlockUseResult.Unknown;
        if (!block.Block.Id.IsMinecraft)
            return false;
        // 1.8.9–1.10.2 iron doors handle use without opening; 1.11 introduces PASS. Through1.12.2 a wooden upper half checks only the lower block identity, including a lower cell whose own half is upper. From1.13 the clicked state is toggled directly. Blocks/BlockSetType registrations prove this exact door family through26.3, including all waxed/weathered copper variants.
        if (HasDoorActivationRules && IsDoor(block))
        {
            if (block.Block.Id.Path == "iron_door")
                result = IronDoorConsumesUse ? BlockUseResult.Success : BlockUseResult.Pass;
            else if (!RequiresLowerCell(block))
                result = BlockUseResult.Success;
            else if (lower.IsValid)
                result = lower.Block.Id == block.Block.Id ? BlockUseResult.Success : BlockUseResult.Pass;
            return true;
        }
        // Original TrapDoorBlock/FenceGateBlock registrations prove these exact families. Wood/copper trapdoors and all gates return client SUCCESS independent of their state; no lower cell is read. Iron trapdoors share the door material boundary: handled success through1.10.2, PASS from1.11. The existing caller owns hand, feature, secondary-use, item continuation and swing policy.
        if (HasTrapdoorGateRules && IsTrapdoor(block))
        {
            result = block.Block.Id.Path == "iron_trapdoor" && !IronDoorConsumesUse ? BlockUseResult.Pass : BlockUseResult.Success;
            return true;
        }
        if (HasTrapdoorGateRules && IsFenceGate(block))
        {
            result = BlockUseResult.Success;
            return true;
        }
        // Original registered menu classes return client true/SUCCESS regardless of server menu, lock, recipe or obstruction outcomes. Smithing tables instead return false/PASS through 1.15.2; 1.16 adds their menu. The caller requires a known native block entry and owns hand, secondary use and swing policy.
        if (HasMenuActivationRules && IsMenuBlock(block))
        {
            // LecternBlock's filled state returns native client success; empty/item insertion branches retain their separate context-dependent uncertainty.
            if (block.Block.Id.Path == "lectern" && (!block.TryGetProperty("has_book", out string hasBook) || hasBook != "true"))
                return false;
            // Through 1.19.3, filled jukebox use returns client success without reading a block entity. From 1.19.4, the native entity-presence branch retains its separate context requirement.
            if (block.Block.Id.Path == "jukebox" && (!HasStateOnlyJukeboxRules || block.LegacyMeta <= 0 && (!block.TryGetProperty("has_record", out string hasRecord) || hasRecord != "true")))
                return false;
            result = SmithingTablePassesUse && block.Block.Id.Path == "smithing_table" ? BlockUseResult.Pass : BlockUseResult.Success;
            return true;
        }
        if (!HasActivationRules)
            return false;
        if (block.Block.Id.Path == "lever")
        {
            result = BlockUseResult.Success;
            return true;
        }
        // Blocks.java registers exactly these native ButtonBlock implementations. ButtonBlock.use returns true for either powered state in 1.14.4; 1.15 introduces CONSUME when already powered. No button overrides item interaction in the audited releases through 26.3.
        if (block.Block.Id.Path is not ("stone_button" or "oak_button" or "spruce_button" or "birch_button" or "jungle_button" or "acacia_button" or "dark_oak_button" or "crimson_button" or "warped_button" or "polished_blackstone_button" or "mangrove_button" or "cherry_button" or "bamboo_button" or "pale_oak_button" or "poplar_button"))
            return false;
        if (block.TryGetProperty("powered", out string powered) && powered is "true" or "false")
            result = PoweredButtonConsumes && powered == "true" ? BlockUseResult.Consume : BlockUseResult.Success;
        return true;
    }
    private static bool IsMenuBlock(BlockState block) => block.Block.Id.Path is "crafting_table" or "chest" or "furnace" or "lit_furnace" or "trapped_chest" or "ender_chest" or "dispenser" or "dropper" or "hopper" or "brewing_stand" or "enchanting_table" or "anvil" or "chipped_anvil" or "damaged_anvil" or "barrel" or "smoker" or "blast_furnace" or "loom" or "cartography_table" or "stonecutter" or "grindstone" or "smithing_table" or "crafter" or "lectern" or "jukebox";
    private static bool IsTrapdoor(BlockState block) => block.Block.Id.IsMinecraft && block.Block.Id.Path is "trapdoor" or "oak_trapdoor" or "iron_trapdoor" or "spruce_trapdoor" or "birch_trapdoor" or "jungle_trapdoor" or "acacia_trapdoor" or "dark_oak_trapdoor" or "crimson_trapdoor" or "warped_trapdoor" or "mangrove_trapdoor" or "bamboo_trapdoor" or "cherry_trapdoor" or "pale_oak_trapdoor" or "poplar_trapdoor" or "copper_trapdoor" or "exposed_copper_trapdoor" or "weathered_copper_trapdoor" or "oxidized_copper_trapdoor" or "waxed_copper_trapdoor" or "waxed_exposed_copper_trapdoor" or "waxed_weathered_copper_trapdoor" or "waxed_oxidized_copper_trapdoor";
    private static bool IsFenceGate(BlockState block) => block.Block.Id.IsMinecraft && block.Block.Id.Path is "fence_gate" or "oak_fence_gate" or "spruce_fence_gate" or "birch_fence_gate" or "jungle_fence_gate" or "acacia_fence_gate" or "dark_oak_fence_gate" or "crimson_fence_gate" or "warped_fence_gate" or "mangrove_fence_gate" or "bamboo_fence_gate" or "cherry_fence_gate" or "pale_oak_fence_gate" or "poplar_fence_gate";
    private static bool IsDoor(BlockState block) => block.Block.Id.IsMinecraft && block.Block.Id.Path is "wooden_door" or "oak_door" or "iron_door" or "spruce_door" or "birch_door" or "jungle_door" or "acacia_door" or "dark_oak_door" or "crimson_door" or "warped_door" or "mangrove_door" or "bamboo_door" or "cherry_door" or "pale_oak_door" or "poplar_door" or "copper_door" or "exposed_copper_door" or "weathered_copper_door" or "oxidized_copper_door" or "waxed_copper_door" or "waxed_exposed_copper_door" or "waxed_weathered_copper_door" or "waxed_oxidized_copper_door";

}
