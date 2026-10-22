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

internal readonly record struct BlockUseActivationProfile(bool HasActivationRules, bool EmptyHandActivationIsMainHandOnly, bool PoweredButtonConsumes, bool HasMenuActivationRules)
{
    // 1.14.4-client-decompiled net/minecraft/world/level/block/LeverBlock.java:75 returns true on the client. Later LeverBlock.use/useWithoutItem returns SUCCESS on the client. MultiPlayerGameMode introduces main-hand-only useWithoutItem fallback in 1.20.5; the source audit covers every release through 26.3.
    public static BlockUseActivationProfile ForProtocol(int protocol) => new(protocol is >= 498 and <= 777, protocol >= 766, protocol >= 573, protocol is >= 47 and <= 777);

    public bool RequiresNativeTerrain(BlockState block)
        => HasMenuActivationRules && block.Block.Id.IsMinecraft && block.Block.Id.Path is "crafting_table" or "chest" or "furnace" or "lit_furnace";

    public bool TryPredict(BlockState block, out BlockUseResult result)
    {
        result = BlockUseResult.Unknown;
        if (!block.Block.Id.IsMinecraft)
            return false;
        // These native menu blocks return true/SUCCESS on the client before server menu, lock, or chest-obstruction checks. The legacy lit_furnace registry entry shares the furnace implementation; the caller already requires a known native block entry.
        if (RequiresNativeTerrain(block))
        {
            result = BlockUseResult.Success;
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
}
