using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

public sealed class ComponentCurrentRangeMiningSessionTests
{
    public static IEnumerable<object[]> CurrentRanges()
    {
        foreach (int protocol in new[] { 766, 767, 768, 769 })
            foreach (string family in new[] { "enum", "boolean", "integer" })
                foreach (string bounds in new[] { "neither", "minimum", "maximum", "both" })
                    yield return [protocol, family, bounds];
    }

    [Theory]
    [MemberData(nameof(CurrentRanges))]
    public async Task InclusiveCurrentValueBoundsPermitFramedAdventureMining(int protocol, string family, string bounds)
    {
        (string block, string property, string actual) = family switch
        {
            "enum" => ("oak_log", "axis", "y"),
            "boolean" => ("oak_button", "powered", "true"),
            "integer" => ("oak_leaves", "distance", "5"),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.State(block, property, actual);
        var matcher = new StatePropertyMatcher(property, false,
            MinValue: bounds is "minimum" or "both" ? actual : null,
            MaxValue: bounds is "maximum" or "both" ? actual : null);
        await session.HoldPredicates([Predicate(session, block, matcher)]);
        await AssertRequest(session, allowed: true, unknown: false);
    }

    public static IEnumerable<object[]> ContextControls()
    {
        foreach (int protocol in new[] { 766, 767, 768, 769 })
            foreach (string condition in new[] { "unproven-order", "missing-name", "full-nbt", "survival" })
                yield return [protocol, condition];
    }

    [Theory]
    [MemberData(nameof(ContextControls))]
    public async Task RangePermissionRetainsNativeContextAndGameModeBoundaries(int protocol, string condition)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(condition == "survival" ? GameMode.Survival : GameMode.Adventure, instantBuild: false);
        await session.State("oak_log", "axis", "y");
        StatePropertyMatcher matcher = condition switch
        {
            "missing-name" => new("facing", false),
            "full-nbt" => new("axis", false, MinValue: "y", MaxValue: "y"),
            _ => new("axis", false, MinValue: "x", MaxValue: "z"),
        };
        await session.HoldPredicates([Predicate(session, "oak_log", matcher, condition is "full-nbt" or "survival" ? new NbtCompound() : null)]);
        await AssertRequest(session, allowed: condition == "survival", unknown: condition is "unproven-order" or "full-nbt");
    }

    private static BlockPredicateEntry Predicate(Session session, string block, StatePropertyMatcher matcher, NbtCompound? nbt = null)
        => new(new HolderSetRef(null, [session.Client.State.Registries!.Blocks[Identifier.Minecraft(block)].NetworkId]), [matcher], nbt);

    private static async Task AssertRequest(Session session, bool allowed, bool unknown)
    {
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        bool bodyPassed = false;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await session.Tick();
            if (unknown)
            {
                Assert.Empty(sent);
                InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
                Assert.Equal("Umpk.Client.Actions.InteractionActions+MiningUnavailableException", error.GetType().FullName);
                Assert.Contains("requires unavailable", error.Message, StringComparison.Ordinal);
                Assert.Empty(await session.Tick());
            }
            else
            {
                if (allowed)
                    Assert.Collection(sent, packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), session.Protocol, 0), AssertSwing, AssertSwing);
                else
                {
                    AssertSwing(Assert.Single(sent));
                    Assert.Empty(await session.Tick());
                }
                Assert.False(action.IsCompleted);
            }
            Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
            bodyPassed = true;
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (unknown || !bodyPassed) { }
        }
    }

    private static void AssertSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.Equal(0, swing.Hand);
        Assert.True(swing.HasHand);
    }
}
