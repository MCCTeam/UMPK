using Umpk.Client.Internal;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Nbt;

namespace Umpk.Client.Actions;

/// <summary>Tool.getMiningSpeed and isCorrectForDrops scan independently: each uses the first matching rule that supplies its own optional value.</summary>
internal static class VanillaToolRules
{
    public static bool TryResolve(ToolComponent tool, int blockNetworkId, ClientRegistryTags tags, out ToolMiningInfo result)
    {
        result = default;
        if (blockNetworkId < 0)
            return false;
        float speed = tool.DefaultMiningSpeed;
        bool correct = false;
        bool foundSpeed = false;
        bool foundCorrect = false;
        foreach (NbtCompound rule in tool.Rules)
        {
            NbtFloat? ruleSpeed = null;
            NbtByte? ruleCorrect = null;
            bool wantsSpeed = !foundSpeed && rule.TryGet("speed", out ruleSpeed);
            bool wantsCorrect = !foundCorrect && rule.TryGet("correct_for_drops", out ruleCorrect);
            if (!wantsSpeed && !wantsCorrect)
                continue;
            bool? matches = Matches(rule.GetList("blocks"), blockNetworkId, tags);
            if (matches is null)
                return false;
            if (!matches.Value)
                continue;
            if (wantsSpeed)
            {
                speed = ruleSpeed!.Value;
                foundSpeed = true;
            }
            if (wantsCorrect)
            {
                correct = ruleCorrect!.Value != 0;
                foundCorrect = true;
            }
            if (foundSpeed && foundCorrect)
                break;
        }
        if (!float.IsFinite(speed) || speed < 0)
            return false;
        result = new ToolMiningInfo(speed, correct);
        return true;
    }

    private static bool? Matches(NbtList? blocks, int blockNetworkId, ClientRegistryTags tags)
    {
        if (blocks is null || blocks.Count == 0)
            return false;
        if (blocks.ElementType == NbtTagType.String)
        {
            if (blocks.Count != 1 || !Identifier.TryParse(((NbtString)blocks[0]).Value, out Identifier tag))
                return null;
            return tags.Contains(RegistryIds.Block, tag, blockNetworkId);
        }
        if (blocks.ElementType != NbtTagType.Int)
            return null;
        foreach (NbtTag member in blocks)
            if (((NbtInt)member).Value == blockNetworkId)
                return true;
        return false;
    }
}
