using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class RegistryTagStateTests
{
    private static readonly Identifier Mineable = Identifier.Parse("example:mineable");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigurationAndPlayTagsReachTheSameConnectionOwner(bool configuration)
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_11);
        TagRegistry[] groups = [new(RegistryIds.Block, [new TagEntry(Mineable, [1])])];
        object packet = configuration ? new ClientboundConfigUpdateTagsPacket(groups) : new ClientboundUpdateTagsPacket(groups);

        await harness.ApplyAsync(packet);

        Assert.True(harness.State.RegistryTags.Contains(RegistryIds.Block, Mineable, 1));
    }

    [Fact]
    public void UnknownAndKnownEmptySnapshotsAreDistinct()
    {
        var tags = new ClientRegistryTags();
        Assert.Null(tags.Contains(RegistryIds.Block, Mineable, 1));
        tags.Install([]);
        Assert.False(tags.Contains(RegistryIds.Block, Mineable, 1));
        tags.Clear();
        Assert.Null(tags.Contains(RegistryIds.Block, Mineable, 1));
    }

    [Fact]
    public void InstalledMembershipOwnsItsValuesAndScopesTagsByRegistry()
    {
        var tags = new ClientRegistryTags();
        int[] members = [1];
        var entries = new List<TagEntry> { new(Mineable, members) };
        tags.Install([new TagRegistry(RegistryIds.Block, entries), new TagRegistry(RegistryIds.Item, [new TagEntry(Mineable, [5])])]);
        members[0] = 5;
        entries.Clear();

        Assert.True(tags.Contains(RegistryIds.Block, Mineable, 1));
        Assert.False(tags.Contains(RegistryIds.Block, Mineable, 5));
        Assert.True(tags.Contains(RegistryIds.Item, Mineable, 5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSnapshotDoesNotPartiallyReplaceThePreviousMembership(bool duplicateRegistry)
    {
        var tags = new ClientRegistryTags();
        tags.Install([new TagRegistry(RegistryIds.Block, [new TagEntry(Mineable, [1])])]);
        TagRegistry[] invalid = duplicateRegistry
            ? [new(RegistryIds.Block, []), new(RegistryIds.Block, [new TagEntry(Mineable, [5])])]
            : [new(RegistryIds.Block, [new TagEntry(Mineable, [5]), new TagEntry(Mineable, [6])])];

        Assert.Throws<ProtocolViolationException>(() => tags.Install(invalid));
        Assert.True(tags.Contains(RegistryIds.Block, Mineable, 1));
        Assert.False(tags.Contains(RegistryIds.Block, Mineable, 5));
    }

    [Fact]
    public void TagsSurviveConfigurationReentryAndEndWithTheConnection()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.RegistryTags.Install([new TagRegistry(RegistryIds.Block, [new TagEntry(Mineable, [1])])]);

        state.ResetForConfigurationReentry();
        Assert.True(state.RegistryTags.Contains(RegistryIds.Block, Mineable, 1));
        state.ResetForSessionEnd();
        Assert.Null(state.RegistryTags.Contains(RegistryIds.Block, Mineable, 1));
    }
}
