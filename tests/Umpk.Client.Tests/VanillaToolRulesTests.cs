using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class VanillaToolRulesTests
{
    [Fact]
    public void SpeedAndHarvestUseTheirOwnFirstMatchingRule()
    {
        var tool = new ToolComponent(1, 1,
            [Rule(Direct(1), null, true), Rule(Direct(1), 16, false), Rule(Direct(1), 32, true)]);

        Assert.True(VanillaToolRules.TryResolve(tool, 1, new ClientRegistryTags(), out ToolMiningInfo result));

        Assert.Equal(new ToolMiningInfo(16, true), result);
    }

    [Fact]
    public void UnmatchedRulesUseTheComponentsOwnDefaultSpeedAndNoHarvestCapability()
    {
        var tool = new ToolComponent(3, 1, [Rule(Direct(5), 16, true)]);

        Assert.True(VanillaToolRules.TryResolve(tool, 1, new ClientRegistryTags(), out ToolMiningInfo result));

        Assert.Equal(new ToolMiningInfo(3, false), result);
    }

    [Fact]
    public void ATagSelectorRequiresKnownMembershipAndUsesReplacementUpdates()
    {
        var tags = new ClientRegistryTags();
        var tag = Identifier.Parse("example:mineable");
        var members = new NbtList(NbtTagType.String);
        members.Add(new NbtString(tag.ToString()));
        var tool = new ToolComponent(1, 1, [Rule(members, 16, true)]);
        Assert.False(VanillaToolRules.TryResolve(tool, 1, tags, out _));

        tags.Install([new TagRegistry(RegistryIds.Block, [new TagEntry(tag, [1])])]);
        Assert.True(VanillaToolRules.TryResolve(tool, 1, tags, out ToolMiningInfo matching));
        Assert.Equal(new ToolMiningInfo(16, true), matching);

        tags.Install([]);
        Assert.True(VanillaToolRules.TryResolve(tool, 1, tags, out ToolMiningInfo absent));
        Assert.Equal(new ToolMiningInfo(1, false), absent);
    }

    [Fact]
    public void ASelectorWhoseOptionalValuesAreAbsentDoesNotNeedTagData()
    {
        var members = new NbtList(NbtTagType.String);
        members.Add(new NbtString("example:unobserved"));
        var tool = new ToolComponent(3, 1, [Rule(members, null, null), Rule(Direct(1), 16, true)]);

        Assert.True(VanillaToolRules.TryResolve(tool, 1, new ClientRegistryTags(), out ToolMiningInfo result));

        Assert.Equal(new ToolMiningInfo(16, true), result);
    }

    private static NbtList Direct(int id)
    {
        var list = new NbtList(NbtTagType.Int);
        list.Add(new NbtInt(id));
        return list;
    }

    private static NbtCompound Rule(NbtList blocks, float? speed, bool? correct)
    {
        var rule = new NbtCompound();
        rule.Put("blocks", blocks);
        if (speed is { } presentSpeed)
            rule.PutFloat("speed", presentSpeed);
        if (correct is { } presentCorrect)
            rule.PutBool("correct_for_drops", presentCorrect);
        return rule;
    }
}
