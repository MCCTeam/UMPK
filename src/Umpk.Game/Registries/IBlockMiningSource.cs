using Umpk.Game.Items.Components;

namespace Umpk.Game.Registries;

/// <summary>Vanilla block mining facts measured from the matching game artifact.</summary>
/// <param name="Hardness">The single-precision destroy speed; -1 is unbreakable and 0 breaks instantly.</param>
/// <param name="RequiresCorrectTool">Whether ordinary harvest capability requires a suitable held tool.</param>
public readonly record struct BlockMiningInfo(float Hardness, bool RequiresCorrectTool);

/// <summary>A default item stack's mining behavior against one block.</summary>
/// <param name="Speed">The base mining speed before player attributes, enchantments and environmental modifiers.</param>
/// <param name="CorrectForDrops">Whether the tool satisfies the block's special harvest requirement.</param>
public readonly record struct ToolMiningInfo(float Speed, bool CorrectForDrops);

/// <summary>Per-version default mining rules. These facts supplement tracked world and inventory state; custom item components and synchronized server tags can override default tool behavior.</summary>
public interface IBlockMiningSource
{
    /// <summary>Whether the version has a complete measurement.</summary>
    bool HasData { get; }

    /// <summary>Returns false when the measurement or block name is unknown.</summary>
    bool TryGetBlock(Identifier block, out BlockMiningInfo info);

    /// <summary>Returns a measured default-stack rule, including speed 1 and no special harvest capability for known items with no override. Unknown items or blocks return false.</summary>
    bool TryGetTool(Identifier item, Identifier block, out ToolMiningInfo info);

    /// <summary>Returns a measured default Tool component, or null for an item whose prototype has none. False means the version has no component measurement. Rule order and tag selectors are retained for evaluation against synchronized server tags.</summary>
    bool TryGetDefaultTool(Identifier item, out ToolComponent? tool)
    {
        tool = null;
        return false;
    }
}
