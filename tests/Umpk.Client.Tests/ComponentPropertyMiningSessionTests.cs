using Umpk.Game.Items;
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

public sealed class ComponentPropertyMiningSessionTests
{
    private enum Permission { Allowed, Denied, Unknown }
    private sealed record Case(string Block, string Property, string Actual, StatePropertyMatcher Matcher, Permission Expected);

    public static IEnumerable<object[]> PropertyCases()
    {
        foreach (int protocol in new[] { 766, 767, 768, 769 })
            foreach (string condition in new[] { "enum-match", "enum-mismatch", "missing-name", "name-case", "enum-case", "enum-invalid", "boolean-match", "boolean-mismatch", "boolean-case", "integer-match", "integer-mismatch", "integer-alias", "integer-unicode-alias", "integer-overflow", "integer-whitespace", "enum-range", "boolean-range", "integer-range", "missing-range" })
                yield return [protocol, condition];
    }

    // Known decisions follow the pinned native sources; unowned property-kind/range facts retain Unknown. State and component values arrive through bound packet codecs, and this fixture calls the public mining action.
    private static Case PropertyCase(string condition) => condition switch
    {
        "enum-match" => new("oak_log", "axis", "y", new("axis", true, "y"), Permission.Allowed),
        "enum-mismatch" => new("oak_log", "axis", "y", new("axis", true, "x"), Permission.Denied),
        "missing-name" => new("oak_log", "axis", "y", new("facing", true, "north"), Permission.Denied),
        "name-case" => new("oak_log", "axis", "y", new("Axis", true, "y"), Permission.Denied),
        "enum-case" => new("oak_log", "axis", "y", new("axis", true, "Y"), Permission.Denied),
        "enum-invalid" => new("oak_log", "axis", "y", new("axis", true, "unknown"), Permission.Denied),
        "boolean-match" => new("oak_button", "powered", "true", new("powered", true, "true"), Permission.Allowed),
        "boolean-mismatch" => new("oak_button", "powered", "true", new("powered", true, "false"), Permission.Denied),
        "boolean-case" => new("oak_button", "powered", "true", new("powered", true, "True"), Permission.Denied),
        "integer-match" => new("oak_leaves", "distance", "5", new("distance", true, "5"), Permission.Allowed),
        "integer-mismatch" => new("oak_leaves", "distance", "5", new("distance", true, "6"), Permission.Denied),
        "integer-alias" => new("oak_leaves", "distance", "5", new("distance", true, "+05"), Permission.Unknown),
        "integer-unicode-alias" => new("oak_leaves", "distance", "5", new("distance", true, "٠٥"), Permission.Unknown),
        "integer-overflow" => new("oak_leaves", "distance", "5", new("distance", true, "2147483648"), Permission.Denied),
        "integer-whitespace" => new("oak_leaves", "distance", "5", new("distance", true, " 5"), Permission.Denied),
        "enum-range" => new("oak_log", "axis", "y", new("axis", false, MinValue: "x", MaxValue: "z"), Permission.Unknown),
        "boolean-range" => new("oak_button", "powered", "true", new("powered", false, MinValue: "false", MaxValue: "true"), Permission.Unknown),
        "integer-range" => new("oak_leaves", "distance", "5", new("distance", false, MinValue: "1", MaxValue: "6"), Permission.Unknown),
        "missing-range" => new("oak_log", "axis", "y", new("facing", false, MinValue: "north"), Permission.Denied),
        _ => throw new ArgumentOutOfRangeException(nameof(condition)),
    };

    [Theory]
    [MemberData(nameof(PropertyCases))]
    public async Task FramedPropertiesDriveTheActualAdventureRequest(int protocol, string condition)
    {
        Case row = PropertyCase(condition);
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.State(row.Block, row.Property, row.Actual);
        await session.HoldPredicates([Predicate(session, row.Block, [row.Matcher])]);
        await AssertRequest(session, row.Expected);
    }

    public static IEnumerable<object[]> OrderingCases()
    {
        foreach (int protocol in new[] { 766, 767, 768, 769 })
            foreach (string condition in new[] { "mismatch-before-nbt", "match-before-nbt", "mismatch-nbt-then-allow", "possible-nbt-then-allow", "absent-tag-mismatch-nbt-then-allow", "absent-tag-match", "unknown-range-then-mismatch", "mismatch-then-unknown-range", "duplicate-match", "duplicate-mismatch", "property-only", "empty-properties", "unknown-entry-then-allow" })
                yield return [protocol, condition];
    }

    [Theory]
    [MemberData(nameof(OrderingCases))]
    public async Task NativeStateShortCircuitAndNbtCacheOwnershipStayDistinct(int protocol, string condition)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.State("oak_log", "axis", "y");
        var match = new StatePropertyMatcher("axis", true, "y");
        var mismatch = new StatePropertyMatcher("axis", true, "x");
        var range = new StatePropertyMatcher("axis", false, MinValue: "x", MaxValue: "z");
        NbtCompound nbt = new() { ["unowned"] = new NbtInt(1) };
        BlockPredicateEntry allowed = Predicate(session, "oak_log", [match]);
        BlockPredicateEntry[] entries = condition switch
        {
            "mismatch-before-nbt" => [Predicate(session, "oak_log", [mismatch], nbt)],
            "match-before-nbt" => [Predicate(session, "oak_log", [match], nbt)],
            "mismatch-nbt-then-allow" => [Predicate(session, "oak_log", [mismatch], nbt), allowed],
            "possible-nbt-then-allow" => [Predicate(session, "oak_log", [match], nbt), allowed],
            "absent-tag-mismatch-nbt-then-allow" => [new(new HolderSetRef(Identifier.Minecraft("unreceived"), []), [mismatch], nbt), allowed],
            "absent-tag-match" => [new(new HolderSetRef(Identifier.Minecraft("unreceived"), []), [match], null)],
            "unknown-range-then-mismatch" => [Predicate(session, "oak_log", [range, mismatch], nbt)],
            "mismatch-then-unknown-range" => [Predicate(session, "oak_log", [mismatch, range], nbt)],
            "duplicate-match" => [Predicate(session, "oak_log", [match, match])],
            "duplicate-mismatch" => [Predicate(session, "oak_log", [match, mismatch])],
            "property-only" => [new(null, [match], null)],
            "empty-properties" => [Predicate(session, "oak_log", [])],
            "unknown-entry-then-allow" => [Predicate(session, "oak_log", [range]), allowed],
            _ => throw new ArgumentOutOfRangeException(nameof(condition)),
        };
        Permission expected = condition switch
        {
            "mismatch-before-nbt" or "unknown-range-then-mismatch" or "mismatch-then-unknown-range" or "duplicate-mismatch" or "absent-tag-match" => Permission.Denied,
            "match-before-nbt" or "possible-nbt-then-allow" => Permission.Unknown,
            _ => Permission.Allowed,
        };
        await session.HoldPredicates(entries);
        await AssertRequest(session, expected);
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    public async Task HeldDeniedRequestReevaluatesAfterAReceivedStateChange(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.State("oak_log", "axis", "z");
        await session.HoldPredicates([Predicate(session, "oak_log", [new("axis", true, "x")])]);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            AssertSwing(Assert.Single(await session.Tick()));
            Assert.Empty(await session.Tick());
            Assert.False(action.IsCompleted);
            await session.State("oak_log", "axis", "x");
            List<IPacket> sent = await session.Tick();
            Assert.Collection(sent, packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), protocol, 0), AssertSwing);
            Assert.False(action.IsCompleted);
            Assert.True(session.Client.State.World.GetBlock(Session.Target).TryGetProperty("axis", out string actual));
            Assert.Equal("x", actual);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
        }
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    public async Task SurvivalDoesNotEvaluateAnUnsupportedNbtPropertyPredicate(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.State("oak_log", "axis", "y");
        await session.HoldPredicates([Predicate(session, "oak_log", [new("axis", false, MinValue: "x")], new NbtCompound())]);
        await AssertRequest(session, Permission.Allowed);
    }

    [Theory]
    [InlineData(766, false)]
    [InlineData(767, false)]
    [InlineData(768, false)]
    [InlineData(769, false)]
    [InlineData(766, true)]
    [InlineData(767, true)]
    [InlineData(768, true)]
    [InlineData(769, true)]
    public async Task FreshEquivalentPropertyComponentsPreserveProgressAfterOptionalDenial(int protocol, bool deniedReplacement)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.State("oak_log", "axis", "y");
        BlockPredicateEntry allowed = Predicate(session, "oak_log", [new("axis", true, "y")]);
        await session.HoldPredicates([allowed]);
        AdventureModePredicateComponent original = session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak)!;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Collection(await session.Tick(), packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), protocol, 0), AssertSwing, AssertSwing);
            if (deniedReplacement)
            {
                await session.HoldPredicates([Predicate(session, "oak_log", [new("axis", true, "x")])]);
                Assert.Empty(await session.Tick());
                Assert.False(action.IsCompleted);
            }
            await session.HoldPredicates([allowed]);
            Assert.NotSame(original, session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak));
            AssertSwing(Assert.Single(await session.Tick()));
            Assert.False(action.IsCompleted);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
        }
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    public async Task ExactPropertyPermissionCompletesOnlyAfterStopAcknowledgment(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.State("oak_button", "powered", "true");
        int originalState = session.Client.State.World.GetBlock(Session.Target).StateId;
        await session.HoldPredicates([Predicate(session, "oak_button", [new("powered", true, "true")])]);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Collection(await session.Tick(), packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), protocol, 0), AssertSwing, AssertSwing);
            ServerboundPlayerActionPacket? stop = null;
            for (int tick = 0; tick < 128 && stop is null; tick++)
            {
                List<IPacket> sent = await session.Tick();
                AssertSwing(Assert.Single(sent.OfType<ServerboundSwingPacket>()));
                if (sent.Count == 2)
                {
                    stop = Assert.IsType<ServerboundPlayerActionPacket>(sent[0]);
                    ComponentAdventureMiningSessionTests.AssertAction(stop, protocol, 2);
                    AssertSwing(sent[1]);
                }
                else
                    AssertSwing(Assert.Single(sent));
            }
            Assert.NotNull(stop);
            Assert.False(action.IsCompleted);
            Assert.Equal(originalState, session.Client.State.World.GetBlock(Session.Target).StateId);
            await session.Complete(action, stop);
            Assert.True(action.IsCompletedSuccessfully);
            Assert.Equal(originalState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
        }
    }

    private static BlockPredicateEntry Predicate(Session session, string name, IReadOnlyList<StatePropertyMatcher>? properties, NbtCompound? nbt = null)
        => new(new HolderSetRef(null, [session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].NetworkId]), properties, nbt);

    private static async Task AssertRequest(Session session, Permission expected)
    {
        int stateId = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await session.Tick();
            if (expected == Permission.Unknown)
            {
                Assert.Empty(sent);
                Exception? error = await Record.ExceptionAsync(async () => await action.WaitAsync(session.Token));
                Assert.NotNull(error);
                Assert.IsAssignableFrom<InvalidOperationException>(error);
                Assert.Equal("Umpk.Client.Actions.InteractionActions+MiningUnavailableException", error.GetType().FullName);
                Assert.Contains("requires unavailable", error.Message, StringComparison.Ordinal);
                Assert.Empty(await session.Tick());
            }
            else
            {
                if (expected == Permission.Allowed)
                    Assert.Collection(sent, packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), session.Protocol, 0), AssertSwing, AssertSwing);
                else
                {
                    AssertSwing(Assert.Single(sent));
                    Assert.Empty(await session.Tick());
                }
                Assert.False(action.IsCompleted);
            }
            Assert.Equal(stateId, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (expected == Permission.Unknown || !action.IsCompletedSuccessfully) { }
        }
    }

    private static void AssertSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.Equal(0, swing.Hand);
        Assert.True(swing.HasHand);
    }
}
