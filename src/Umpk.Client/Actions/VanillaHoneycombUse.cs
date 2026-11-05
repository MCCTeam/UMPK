using Umpk.Game.Blocks;

namespace Umpk.Client.Actions;

/// <summary>The original copper waxing result after the existing activation, cooldown and permission phases.</summary>
internal static class VanillaHoneycombUse
{
    public static bool TryPredict(int protocol, Identifier item, BlockState clicked, out BlockUsePrediction prediction)
    {
        prediction = BlockUsePrediction.Unknown;
        // HoneycombItem owns WAXABLES from 1.17 through 26.2. Protocol 777 replaces this item method with a transformer component.
        if (protocol is < 755 or > 776 || !item.IsMinecraft || item.Path != "honeycomb")
            return false;
        if (!clicked.Block.Id.IsMinecraft)
            return true;
        ReadOnlySpan<char> block = clicked.Block.Id.Path;
        bool waxed = block.StartsWith("waxed_", StringComparison.Ordinal);
        if (waxed)
            block = block[6..];
        // Every applicable original WAXABLES map contains these sixteen source blocks and excludes their already-waxed counterparts. Other targets retain unknown knowledge.
        if (block is "copper_block" or "exposed_copper" or "weathered_copper" or "oxidized_copper"
            or "cut_copper" or "exposed_cut_copper" or "weathered_cut_copper" or "oxidized_cut_copper"
            or "cut_copper_slab" or "exposed_cut_copper_slab" or "weathered_cut_copper_slab" or "oxidized_cut_copper_slab"
            or "cut_copper_stairs" or "exposed_cut_copper_stairs" or "weathered_cut_copper_stairs" or "oxidized_cut_copper_stairs")
            prediction = waxed ? BlockUsePrediction.Pass : BlockUsePrediction.Success(localSwing: false);
        return true;
    }
}
