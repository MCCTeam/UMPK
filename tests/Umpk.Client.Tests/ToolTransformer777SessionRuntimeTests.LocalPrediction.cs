using Umpk.Client.Actions;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed partial class ToolTransformer777SessionRuntimeTests
{
    public static IEnumerable<object[]> LocalToolPredictionCases()
    {
        foreach (string scenario in new[]
        {
            "oak-x", "poplar-z", "coarse-two-steps", "rooted-down", "path-repeat", "copper-three-steps", "wax-then-scrape", "same-state-sequence",
            "server-before-ack", "earlier-ack", "cumulative-ack", "door-neighbor", "chest-neighbor", "full-column", "unload", "untouched-section",
            "world-replacement", "reconfiguration", "disconnect", "pure-query", "cancel-before-commit", "cancel-after-write", "two-owned-actions",
        })
            yield return [scenario];
    }

    [Theory]
    [MemberData(nameof(LocalToolPredictionCases))]
    public async Task LocalToolPredictionRetainsOriginalControllerState(string scenario)
    {
        string family = scenario is "oak-x" or "poplar-z" or "copper-three-steps" or "wax-then-scrape" or "door-neighbor" or "chest-neighbor" ? "axe"
            : scenario == "path-repeat" ? "shovel" : "hoe";
        string block = scenario switch
        {
            "oak-x" => "oak_log",
            "poplar-z" => "poplar_log",
            "rooted-down" => "rooted_dirt",
            "path-repeat" => "grass_block",
            "copper-three-steps" => "oxidized_copper",
            "wax-then-scrape" => "waxed_oxidized_copper",
            "door-neighbor" => "oxidized_copper_door",
            "chest-neighbor" => "waxed_oxidized_copper_chest",
            "same-state-sequence" => "dirt",
            _ => "coarse_dirt",
        };
        await using Session session = await LocalReady(family, block);
        List<(Task Action, int Sequence)> pending = [];
        try
        {
            if (scenario is "oak-x" or "poplar-z")
                await LocalState(session, Session.Target, State(session, block, ("axis", scenario == "oak-x" ? "x" : "z")));
            if (scenario == "door-neighbor")
            {
                await LocalState(session, Session.Target, State(session, block, ("half", "lower")));
                await LocalState(session, Session.Target.Below(), State(session, "stone"));
                await LocalState(session, Session.Target.Above(), State(session, block, ("half", "upper")));
                await Secondary(session);
            }
            if (scenario == "chest-neighbor")
            {
                await LocalState(session, Session.Target, State(session, block, ("type", "left"), ("facing", "north")));
                await LocalState(session, Session.Target.Offset(Direction.East), State(session, block, ("type", "right"), ("facing", "north")));
                await Secondary(session);
            }
            int original = session.Client.State.World.GetBlock(Session.Target).StateId;
            ItemStack originalStack = session.Client.State.Inventory.GetSlot(36);
            if (scenario == "cancel-before-commit")
            {
                using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                await cancelled.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new(.5, 1, .5), Hand.Main, ct: cancelled.Token));
                Assert.Equal(original, await Current(session));
                Assert.Empty(await session.Barrier());
                return;
            }
            if (scenario == "pure-query")
            {
                Assert.Equal(BlockUseResult.Success, await LocalResult(session));
                Assert.Equal(BlockUseResult.Success, await LocalResult(session));
                Assert.Equal(original, await Current(session));
                Assert.Empty(await session.Barrier());
            }
            int firstState = scenario switch
            {
                "oak-x" => State(session, "stripped_oak_log", ("axis", "x")),
                "poplar-z" => State(session, "stripped_poplar_log", ("axis", "z")),
                "path-repeat" => State(session, "dirt_path"),
                "copper-three-steps" => State(session, "weathered_copper"),
                "wax-then-scrape" => State(session, "oxidized_copper"),
                "door-neighbor" => State(session, "weathered_copper_door", ("half", "lower")),
                "chest-neighbor" => State(session, "oxidized_copper_chest", ("type", "left"), ("facing", "north")),
                "same-state-sequence" => State(session, "farmland"),
                _ => State(session, "dirt"),
            };
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
            int first = await LocalStart(session, pending, firstState, scenario == "rooted-down" ? Direction.Down : Direction.Up, caller.Token);
            Assert.Equal(original, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(originalStack, session.Client.State.Inventory.GetSlot(36));
            switch (scenario)
            {
                case "oak-x":
                case "poplar-z":
                case "rooted-down":
                case "path-repeat":
                    Assert.Equal(BlockUseResult.Pass, await LocalResult(session, scenario == "rooted-down" ? Direction.Down : Direction.Up));
                    await LocalStart(session, pending, firstState, scenario == "rooted-down" ? Direction.Down : Direction.Up);
                    break;
                case "coarse-two-steps":
                case "earlier-ack":
                case "cumulative-ack":
                case "two-owned-actions":
                    int second = await LocalStart(session, pending, State(session, "farmland"));
                    if (scenario == "earlier-ack")
                    {
                        await LocalAck(session, first);
                        Assert.Equal(State(session, "farmland"), await Current(session));
                        Assert.False(pending[1].Action.IsCompleted);
                    }
                    await LocalAck(session, second);
                    Assert.Equal(original, await Current(session));
                    break;
                case "copper-three-steps":
                    await LocalStart(session, pending, State(session, "exposed_copper"));
                    await LocalStart(session, pending, State(session, "copper_block"));
                    Assert.Equal(BlockUseResult.Pass, await LocalResult(session));
                    break;
                case "wax-then-scrape":
                    await LocalStart(session, pending, State(session, "weathered_copper"));
                    break;
                case "same-state-sequence":
                    await LocalFarmTagIncludesItself(session);
                    await LocalStart(session, pending, State(session, "farmland"));
                    await LocalAck(session, first);
                    Assert.Equal(original, await Current(session));
                    Assert.False(pending[1].Action.IsCompleted);
                    break;
                case "server-before-ack":
                    await LocalState(session, Session.Target, State(session, "stone"));
                    Assert.Equal(firstState, await Current(session));
                    Assert.Equal(State(session, "stone"), session.Client.State.World.GetBlock(Session.Target).StateId);
                    await LocalAck(session, first);
                    Assert.Equal(State(session, "stone"), await Current(session));
                    break;
                case "door-neighbor":
                    Assert.Equal(State(session, "weathered_copper_door", ("half", "upper")), await Current(session, Session.Target.Above()));
                    Assert.Equal(State(session, block, ("half", "upper")), session.Client.State.World.GetBlock(Session.Target.Above()).StateId);
                    await LocalAck(session, first);
                    Assert.Equal(State(session, block, ("half", "upper")), await Current(session, Session.Target.Above()));
                    break;
                case "chest-neighbor":
                    Assert.Equal(State(session, "oxidized_copper_chest", ("type", "right"), ("facing", "north")), await Current(session, Session.Target.Offset(Direction.East)));
                    await LocalAck(session, first);
                    Assert.Equal(State(session, block, ("type", "right"), ("facing", "north")), await Current(session, Session.Target.Offset(Direction.East)));
                    break;
                case "full-column":
                    await session.Terrain();
                    Assert.True(session.Client.State.World.GetBlock(Session.Target).IsAir);
                    Assert.Equal(session.Client.State.World.GetBlock(Session.Target).StateId, await Current(session));
                    await LocalAck(session, first);
                    Assert.Equal(original, await Current(session));
                    break;
                case "unload":
                    await session.Forget();
                    Assert.Equal(BlockUseResult.Unknown, await LocalResult(session));
                    await LocalAck(session, first);
                    Assert.Equal(BlockUseResult.Unknown, await LocalResult(session));
                    break;
                case "untouched-section":
                    await session.Send(new ClientboundSectionBlocksUpdatePacket(5, 0, 0, [new((short)((1 << 8) | (1 << 4) | 1), State(session, "stone"))], IsLegacy: false));
                    Assert.Empty(await session.Barrier());
                    Assert.Equal(firstState, await Current(session));
                    break;
                case "world-replacement":
                    var previousWorld = session.Client.State.World;
                    await session.Respawn();
                    Assert.False(session.Client.State.JukeboxPrediction.TryProject(previousWorld, Session.Target, previousWorld.GetBlock(Session.Target), out _));
                    await LocalEnded(pending[0].Action, session.Token);
                    break;
                case "reconfiguration":
                    var retained = session.Client.State.World;
                    await session.Reconfigure(null, [NativePacket()]);
                    Assert.False(session.Client.State.JukeboxPrediction.TryProject(retained, Session.Target, retained.GetBlock(Session.Target), out _));
                    await LocalEnded(pending[0].Action, session.Token);
                    break;
                case "disconnect":
                    var disconnectedWorld = session.Client.State.World;
                    await session.Client.DisconnectAsync(session.Token);
                    await Assert.ThrowsAsync<ConnectionClosedException>(async () => { while (true) await session.Server.NextFrameAsync(session.Token); });
                    Assert.False(session.Client.State.JukeboxPrediction.TryProject(disconnectedWorld, Session.Target, disconnectedWorld.GetBlock(Session.Target), out _));
                    await LocalEnded(pending[0].Action, session.Token);
                    break;
                case "cancel-after-write":
                    await caller.CancelAsync();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending[0].Action.WaitAsync(session.Token));
                    Assert.Equal(firstState, await Current(session));
                    await LocalAck(session, first);
                    Assert.Equal(original, await Current(session));
                    break;

            }
            if (scenario is not "world-replacement" and not "reconfiguration" and not "disconnect" and not "cancel-after-write")
            {
                await LocalAck(session, pending.Max(item => item.Sequence));
                foreach (var use in pending)
                {
                    await use.Action.WaitAsync(session.Token);
                    Assert.True(use.Action.IsCompletedSuccessfully);
                }
            }
        }
        finally
        {
            session.BeforeClientWrite = null;
            if (session.Client.Status == ClientStatus.Playing && pending.Count != 0)
                await LocalAck(session, pending.Max(item => item.Sequence));
            foreach (var use in pending)
                try { await use.Action.WaitAsync(session.Token); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
        }
    }

    private static async Task<Session> LocalReady(string family, string block)
    {
        Session session = await Session.Create(777, block, physics: false, observeWrites: true, configurationPackets: [NativePacket()]);
        try
        {
            await LocalTags(session);
            await session.Hold("diamond_" + family);
            await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 45, ItemStack.Empty));
            Assert.Empty(await session.Barrier());
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    private static int State(Session session, string name, params (string Name, string Value)[] values)
    {
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value;
        if (values.Length == 0)
            return block.DefaultStateId;
        for (int candidate = block.MinStateId; candidate <= block.MaxStateId; candidate++)
        {
            bool matches = true;
            foreach (BlockPropertyDefinition property in block.Properties)
            {
                Assert.True(block.TryGetPropertyValue(block.DefaultStateId, property.Name, out string expected));
                foreach (var requested in values)
                    if (requested.Name == property.Name)
                        expected = requested.Value;
                if (!block.TryGetPropertyValue(candidate, property.Name, out string actual) || actual != expected)
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
                return candidate;
        }
        throw new InvalidOperationException("The native property combination is absent: " + name);
    }

    private static async Task LocalState(Session session, BlockPos position, int state)
    {
        await session.Send(new ClientboundBlockUpdatePacket(position, state));
        Assert.Empty(await session.Barrier());
    }

    private static Task<int> Current(Session session, BlockPos? position = null)
        => session.Client.InvokeAsync(client => client.State.JukeboxPrediction.GetBlock(client.State.World, position ?? Session.Target).StateId, session.Token);

    private static async Task<BlockUseResult> LocalResult(Session session, Direction face = Direction.Up)
    {
        BlockUsePrediction value = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, face, new(.5, face == Direction.Down ? 0 : 1, .5), Hand.Main), session.Token);
        Assert.True(value.SendPacket);
        Assert.Equal(value.Result == BlockUseResult.Success ? BlockUseSwing.Local : BlockUseSwing.None, value.Swing);
        return value.Result;
    }

    private static async Task<int> LocalStart(Session session, List<(Task Action, int Sequence)> pending, int expected, Direction face = Direction.Up, CancellationToken? caller = null)
    {
        int? atWrite = null;
        session.BeforeClientWrite = () => atWrite ??= session.Client.State.JukeboxPrediction.GetBlock(session.Client.State.World, Session.Target).StateId;
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, face, new(.5, face == Direction.Down ? 0 : 1, .5), Hand.Main, ct: caller ?? session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        session.BeforeClientWrite = null;
        List<IPacket> packets = await session.Barrier();
        var use = Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(packets));
        pending.Add((action, use.Sequence));
        Assert.Equal(pending.Count, use.Sequence);
        Assert.Equal((Session.Target, (int)face, (int)Hand.Main), (use.Position, use.Face, use.Hand));
        Assert.Equal((.5F, face == Direction.Down ? 0F : 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside || use.WorldBorderHit);
        Assert.DoesNotContain(packets, packet => packet is ServerboundSwingPacket or ServerboundPunchPacket);
        Assert.False(action.IsCompleted);
        Assert.NotNull(atWrite);
        Assert.Equal(expected, atWrite);
        Assert.Equal(expected, await Current(session));
        return use.Sequence;
    }

    private static async Task LocalAck(Session session, int sequence)
    {
        await session.Send(new ClientboundBlockChangedAckPacket(sequence));
        Assert.Empty(await session.Barrier());
    }

    private static async Task LocalEnded(Task action, CancellationToken token)
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.WaitAsync(token));
        Assert.True(action.IsCanceled);
    }

    private static async Task LocalTags(Session session)
    {
        int[] Ids(params string[] names) => names.Select(name => session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].NetworkId).ToArray();
        await session.Send(new ClientboundUpdateTagsPacket([new TagRegistry(RegistryIds.Block, [
            new TagEntry(Identifier.Minecraft("air"), Ids("air", "void_air", "cave_air")),
            new TagEntry(Identifier.Minecraft("turns_into_farmland"), Ids("grass_block", "dirt_path", "dirt")),
            new TagEntry(Identifier.Minecraft("turns_into_dirt_path"), Ids("grass_block", "dirt", "podzol", "coarse_dirt", "mycelium", "rooted_dirt")),
            new TagEntry(Identifier.Minecraft("copper_chests"), Ids("copper_chest", "exposed_copper_chest", "weathered_copper_chest", "oxidized_copper_chest", "waxed_copper_chest", "waxed_exposed_copper_chest", "waxed_weathered_copper_chest", "waxed_oxidized_copper_chest")),
        ])]));
        Assert.Empty(await session.Barrier());
    }

    private static async Task LocalFarmTagIncludesItself(Session session)
    {
        int[] Ids(params string[] names) => names.Select(name => session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].NetworkId).ToArray();
        await session.Send(new ClientboundUpdateTagsPacket([new TagRegistry(RegistryIds.Block, [
            new TagEntry(Identifier.Minecraft("air"), Ids("air", "void_air", "cave_air")),
            new TagEntry(Identifier.Minecraft("turns_into_farmland"), Ids("grass_block", "dirt_path", "dirt", "farmland")),
            new TagEntry(Identifier.Minecraft("turns_into_dirt_path"), Ids("grass_block", "dirt", "podzol", "coarse_dirt", "mycelium", "rooted_dirt")),
        ])]));
        Assert.Empty(await session.Barrier());
    }
}
