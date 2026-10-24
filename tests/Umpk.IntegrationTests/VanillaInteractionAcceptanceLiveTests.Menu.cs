using System.Buffers;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    [NightlyTheory]
    [InlineData("1.8.9", 47)]
    [InlineData("1.14", 477)]
    [InlineData("1.14.4", 498)]
    [InlineData("1.16", 735)]
    [InlineData("1.20.4", 765)]
    [InlineData("1.20.6", 766)]
    [InlineData("26.3", 777)]
    public async Task MenuUseMatchesNativeOpeningRefusalAndSwing(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        string? initialEnabledPacks = protocol is 765 or 766 ? "vanilla,update_1_21" : null;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct, initialEnabledPacks: initialEnabledPacks);
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version, includeEnabledFeatures: protocol >= 765);
        client.PacketFrameObserved += capture.Observe;
        int opens = 0;
        using IDisposable opened = client.Events.Subscribe<ContainerOpened>(_ => Interlocked.Increment(ref opens));
        bool passed = false;
        var scenarios = new List<object>();
        var closeFences = new List<object>();
        var sentinelCleanups = new List<object>();
        var observationMarkers = new List<string>();
        var counters = new Dictionary<string, int> { ["crafting_table"] = 0, ["furnace"] = 0, ["chest"] = 0 };
        int previousSequence = -1;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            if (initialEnabledPacks is not null)
            {
                CapturedFrame featureFrame = Assert.Single(capture.Frames, frame => frame.Phase == ProtocolPhase.Configuration && frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundConfigUpdateEnabledFeaturesPacket);
                var features = Assert.IsType<ClientboundConfigUpdateEnabledFeaturesPacket>(featureFrame.Packet);
                Assert.Equal(new[] { "minecraft:update_1_21", "minecraft:vanilla" }, features.Features.Select(value => value.ToString()).Order(StringComparer.Ordinal));
                string nativePacks = await server.CommandAsync("datapack list enabled", ct);
                Assert.Equal("There are 2 data pack(s) enabled: [vanilla (built-in)], [update_1_21 (feature)]", nativePacks);
                Assert.Contains("initial-enabled-packs=vanilla,update_1_21", await File.ReadAllLinesAsync(Path.Combine(server.DirectoryPath, "server.properties"), ct));
                await server.WriteAsync("menu-native-feature-selection.json", new { requestedInitialEnabledPacks = initialEnabledPacks, observedFeatures = features.Features.Select(value => value.ToString()).ToArray(), nativePacks, featureFrame.WireId, featureFrame.Phase, featureFrame.Payload, purpose = "Observe the actual native world feature set and selected pack; registration alone is insufficient." });
            }
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "menu terrain", ct);
            Assert.True(client.State.Features.Physics);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = (int)Math.Floor(spawn.X), z = (int)Math.Floor(spawn.Z);
            var target = new BlockPos(x + ((x & 15) >= 14 ? -2 : 2), 100, z);
            var floor = new BlockPos(x, 99, z);
            Direction face = target.X > x ? Direction.West : Direction.East;
            var cursor = new Vec3d(face == Direction.West ? 0 : 1, .5, .5);
            BlockPos adjacent = target.Offset(face);
            Assert.Equal((x >> 4, z >> 4), (target.X >> 4, target.Z >> 4));
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync($"tp {Username} {x + .5:R} 100 {z + .5:R}", ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && c.State.Self.OnGround && Math.Abs(c.State.Self.Position.Y - 100) < .01, "grounded menu owner", ct);
            await WaitAsync(() => capture.Frames.Any(frame => frame.Flow == PacketFlow.Clientbound && (frame.Packet is ClientboundLevelChunkPacket chunk && chunk.FullChunk && chunk.ChunkX == target.X >> 4 && chunk.ChunkZ == target.Z >> 4 || protocol == 47 && frame.Packet is ClientboundMapChunkBulkPacket bulk && bulk.Columns.Any(column => column.Position.X == target.X >> 4 && column.Position.Z == target.Z >> 4))), "actual native full-column packet", ct);
            if (protocol == 47)
                await client.InvokeAsync(c =>
                {
                    var column = c.State.World.GetColumn(target);
                    Assert.NotNull(column);
                    Assert.Same(c.State.World.Dimension, column.Dimension);
                    Assert.Equal(0, column.MinY);
                    Assert.Equal(256, column.MaxY);
                    return true;
                }, ct);
            foreach (string block in counters.Keys)
            {
                await server.ConsoleAsync($"scoreboard objectives add {Objective(block)} {Criterion(block)}", ct);
                await server.ConsoleAsync($"scoreboard players set {Username} {Objective(block)} 0", ct);
                await AssertScore(block);
            }
            await Use("crafting_table", blocked: false, sneaking: false, main: null, off: null, Hand.Main, clientSuccess: true, nativeOpen: true);
            await Use("furnace", blocked: false, sneaking: false, main: null, off: null, Hand.Main, clientSuccess: true, nativeOpen: true);
            await Use("chest", blocked: false, sneaking: false, main: null, off: null, Hand.Main, clientSuccess: true, nativeOpen: true);
            await Use("chest", blocked: true, sneaking: false, main: null, off: null, Hand.Main, clientSuccess: true, nativeOpen: false);
            await Use("crafting_table", blocked: false, sneaking: true, main: null, off: null, Hand.Main, clientSuccess: true, nativeOpen: true);
            await Use("furnace", blocked: false, sneaking: true, main: "stick", off: null, Hand.Main, clientSuccess: false, nativeOpen: false);
            await Use("crafting_table", blocked: false, sneaking: false, main: "stick", off: null, Hand.Off, clientSuccess: protocol < 766 && protocol != 47, nativeOpen: protocol < 766 && protocol != 47, rejectHand: protocol == 47);
            if (protocol != 47)
                await Use("crafting_table", blocked: false, sneaking: true, main: null, off: "stick", Hand.Main, clientSuccess: false, nativeOpen: false);
            foreach (string block in AdditionalMenuBlocks(protocol))
            {
                bool succeeds = block != "smithing_table" || protocol >= 735;
                await Use(block, blocked: false, sneaking: false, main: null, off: null, Hand.Main, clientSuccess: succeeds, nativeOpen: succeeds);
            }
            await Use("trapped_chest", blocked: true, sneaking: false, main: null, off: null, Hand.Main, clientSuccess: true, nativeOpen: false);
            await Use("ender_chest", blocked: true, sneaking: false, main: null, off: null, Hand.Main, clientSuccess: true, nativeOpen: false);
            if (protocol >= 477)
                await Use("barrel", blocked: false, sneaking: false, main: "stick", off: null, Hand.Off, clientSuccess: protocol < 766, nativeOpen: protocol < 766);
            int replayed = capture.AssertRoundTrips(client.State.Registries!);
            var replayDispositions = new List<object>();
            int menuReplayed = ReplayMenuFrames(capture.Frames, version, client.State.Registries!, observationMarkers, replayDispositions);
            await server.WriteAsync("menu-replay.json", new { exactObservationMarkers = observationMarkers, byteIdenticalFrames = menuReplayed, frames = replayDispositions, scope = "All selected frames decode with typed semantic assertions; feature frames and exact action markers require byte identity. Other chat retains decoded semantic checks, observed byte equality and explicit differences alongside the unchanged raw capture." });
            Assert.True(menuReplayed >= 10);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "menu server-observed disconnect", ct);
            await server.WriteAsync("menu-acceptance.json", new { release, protocol, physics = true, normalLauncher = true, captureStartsBeforePlay = true, initialEnabledPacks, additionalMenuKeys = AdditionalMenuBlocks(protocol), target, face, cursor, nativeOpenCounters = counters, scenarios, closeFences, sentinelCleanups, replayed, menuReplayed, peerObservedDisconnect = true });
            passed = true;

            async Task Use(string block, bool blocked, bool sneaking, string? main, string? off, Hand hand, bool clientSuccess, bool nativeOpen, bool rejectHand = false)
            {
                Assert.False(await client.InvokeAsync(c => c.State.Inventory.HasOpenContainer, ct));
                await client.Actions.Movement.SetSneakingAsync(false, ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                if (main is not null)
                    await server.ConsoleAsync($"give {Username} minecraft:{main} 1", ct);
                if (off is not null)
                    await server.ConsoleAsync(protocol >= 755 ? $"item replace entity {Username} weapon.offhand with minecraft:{off}" : $"replaceitem entity {Username} {MenuCommandSlot(protocol, "weapon.offhand")} minecraft:{off}", ct);
                await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
                await WaitAsync(client, c => Matches(c.State.Inventory.GetSlot(36), main) && (protocol == 47 || Matches(c.State.Inventory.GetSlot(45), off)), "menu selected stacks", ct);
                // Each transition must be observed before the action mark, so old setup traffic cannot stand in for the native handler's reply pair.
                await SetCell(target, "air");
                await SetCell(target, block);
                await SetCell(target.Above(), "dirt");
                await SetCell(target.Above(), blocked ? "stone" : "air");
                await SetCell(adjacent, "dirt");
                await SetCell(adjacent, "air");
                if (block is "chest" or "furnace")
                {
                    int slot = block == "chest" ? 0 : 2;
                    string item = block == "chest" ? "diamond" : "stone";
                    int count = block == "chest" ? 3 : 7;
                    await server.ConsoleAsync(protocol >= 755
                        ? $"item replace block {target.X} {target.Y} {target.Z} container.{slot} with minecraft:{item} {count}"
                        : $"replaceitem block {target.X} {target.Y} {target.Z} {MenuCommandSlot(protocol, "container." + slot)} minecraft:{item} {count}", ct);
                    await AssertStored(block);
                }
                await AssertBlockAsync(server, protocol, target, block, ct);
                await AssertEmptyAdditionalStorage(block);
                await AssertBlockAsync(server, protocol, target.Above(), blocked ? "stone" : "air", ct);
                if (sneaking)
                    await client.Actions.Movement.SetSneakingAsync(true, ct);
                await client.InvokeAsync(c =>
                {
                    Assert.Equal(sneaking, c.State.Self.Sneaking);
                    Assert.True(c.State.Self.OnGround);
                    Assert.True(Math.Abs(c.State.Self.Position.Y - 100) < .01);
                    return true;
                }, ct);
                int beforeOpens = Volatile.Read(ref opens);
                int mark = capture.Mark();
                int targetState = await client.InvokeAsync(c =>
                {
                    BlockState state = c.State.World.GetBlock(target);
                    if (block != "barrel" || !nativeOpen)
                        return state.StateId;
                    Assert.True(state.TryGetProperty("open", out string open));
                    Assert.Equal("false", open);
                    BlockDefinition definition = state.Block.Value;
                    return Assert.Single(Enumerable.Range(definition.MinStateId, definition.StateCount), candidate => state.PropertyNames.All(name => state.Source.TryGetPropertyValue(candidate, name, out string value) && (name == "open" ? value == "true" : state.TryGetProperty(name, out string previous) && value == previous)));
                }, ct);
                int adjacentState = await client.InvokeAsync(c => c.State.World.GetBlock(adjacent).StateId, ct);
                CapturedFrame? processing = null;
                if (rejectHand)
                {
                    await Assert.ThrowsAsync<ActionNotSupportedException>(() => client.Actions.Interaction.PlaceBlockAsync(target, face, cursor, hand, ct: ct));
                    Assert.DoesNotContain(capture.Since(mark), IsUseOrAnimation);
                }
                else
                {
                    await client.Actions.Interaction.PlaceBlockAsync(target, face, cursor, hand, ct: ct);
                    processing = await ProcessingWitness(mark, target, adjacent, targetState, adjacentState);
                }
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                string observation = "menu-observed-" + Guid.NewGuid().ToString("N");
                observationMarkers.Add(observation);
                await server.ConsoleAsync("say " + observation, ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => MenuChatContains(frame.Packet, observation)), "post-action client observation marker", ct);
                CapturedFrame observed = Assert.Single(capture.Since(mark), frame => MenuChatContains(frame.Packet, observation));
                if (processing is not null)
                    Assert.True(observed.Milliseconds - processing.Milliseconds >= 2000, "Keep native traffic after the specific use-processing witness.");
                CapturedFrame[] frames = capture.Since(mark);
                CapturedFrame[] actions = frames.Where(IsUseOrAnimation).ToArray();
                Assert.Equal(rejectHand ? 0 : clientSuccess && protocol != 777 ? 2 : 1, actions.Length);
                if (!rejectHand)
                    AssertMenuUse(actions[0], target, face, cursor, hand, main);
                if (clientSuccess && protocol != 777 && !rejectHand)
                    Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                ClientboundOpenScreenPacket[] windows = frames.Select(frame => frame.Packet).OfType<ClientboundOpenScreenPacket>().ToArray();
                Assert.Equal(nativeOpen ? 1 : 0, windows.Length);
                Assert.Equal(beforeOpens + (nativeOpen ? 1 : 0), Volatile.Read(ref opens));
                int? windowId = null;
                if (nativeOpen)
                {
                    ClientboundOpenScreenPacket window = Assert.Single(windows);
                    windowId = window.ContainerId;
                    Assert.True(window.ContainerId > 0);
                    (string expectedKind, string titleKey, int slots, int legacySlots) = MenuExpectation(block, protocol);
                    Assert.Equal(titleKey, Assert.IsType<TranslatableContent>(window.Title.Content).Key);
                    if (protocol == 47)
                    {
                        Assert.Equal("minecraft:" + expectedKind, window.LegacyType);
                        Assert.Equal(-1, window.MenuTypeId);
                        Assert.Equal(legacySlots, window.LegacySlotCount);
                    }
                    else
                    {
                        Assert.Null(window.LegacyType);
                        Assert.Equal(client.State.Registries!.MenuTypes[Identifier.Minecraft(expectedKind)].NetworkId, window.MenuTypeId);
                    }
                    ClientboundContainerSetContentPacket[] contents = frames.Select(frame => frame.Packet).OfType<ClientboundContainerSetContentPacket>().Where(packet => packet.ContainerId == window.ContainerId).ToArray();
                    Assert.NotEmpty(contents);
                    Assert.All(contents, packet => Assert.Equal(slots, packet.Items.Count));
                    if (block is "furnace" or "chest")
                    {
                        int slot = block == "chest" ? 0 : 2;
                        string item = block == "chest" ? "diamond" : "stone";
                        int count = block == "chest" ? 3 : 7;
                        AssertStack(contents[^1].Items[slot], item, count);
                    }
                    if (main is null && off is null && block is not ("furnace" or "chest"))
                        Assert.All(contents[^1].Items, stack => Assert.True(stack.IsEmpty));
                    await client.InvokeAsync(c =>
                    {
                        Assert.True(c.State.Inventory.HasOpenContainer);
                        Assert.Equal(window.ContainerId, c.State.Inventory.OpenWindowId);
                        Assert.Equal("minecraft:" + expectedKind, c.State.Inventory.OpenContainerMenuType?.Id.ToString());
                        Assert.Equal(titleKey, Assert.IsType<TranslatableContent>(c.State.Inventory.OpenContainerTitle!.Content).Key);
                        Assert.Equal(slots, c.State.Inventory.ContainerSlots!.Count);
                        if (block is "furnace" or "chest")
                            AssertStack(c.State.Inventory.ContainerSlots[block == "chest" ? 0 : 2], block == "chest" ? "diamond" : "stone", block == "chest" ? 3 : 7);
                        return true;
                    }, ct);
                    if (counters.ContainsKey(block))
                        counters[block]++;
                }
                else
                    Assert.False(await client.InvokeAsync(c => c.State.Inventory.HasOpenContainer, ct));
                foreach (string family in counters.Keys)
                    await AssertScore(family);
                await AssertBlockAsync(server, protocol, target, block, ct);
                if (block is "furnace" or "chest")
                    await AssertStored(block);
                await AssertEmptyAdditionalStorage(block);
                if (block == "barrel")
                    Assert.Equal("Test passed", await server.CommandAsync($"execute if block {target.X} {target.Y} {target.Z} minecraft:barrel[open={(nativeOpen ? "true" : "false")}]", ct));
                scenarios.Add(new { block, blocked, sneaking, main, off, hand, clientSuccess, nativeOpen, rejectHand, packets = actions.Length, windowId, nativeCounter = counters.TryGetValue(block, out int counter) ? (int?)counter : null, processingKind = rejectHand ? "pre-send hand rejection; no native use" : protocol >= 759 ? "matching native block-change acknowledgment" : "ordered native clicked/adjacent block-change replies", processingMilliseconds = processing?.Milliseconds, postObservationMilliseconds = observed.Milliseconds, observationRole = "post-observation marker, never the use-processing witness" });
                await server.WriteAsync("menu-scenarios.json", scenarios);
                if (nativeOpen)
                    await CloseWithNativeFence(windowId!.Value);
                if (block == "barrel" && nativeOpen)
                    await WaitAsync(client, c => c.State.World.GetBlock(target).TryGetProperty("open", out string open) && open == "false", "native barrel close recheck", ct);
                if (block is "furnace" or "chest")
                    await ClearStored(block);
            }

            async Task AssertEmptyAdditionalStorage(string block)
            {
                if (block is not ("trapped_chest" or "ender_chest" or "dispenser" or "dropper" or "hopper" or "brewing_stand" or "barrel" or "smoker" or "blast_furnace" or "crafter"))
                    return;
                string command = block == "ender_chest"
                    ? protocol == 47 ? $"testfor {Username} {{EnderItems:[]}}" : $"execute unless data entity {Username} EnderItems[0]"
                    : protocol == 47 ? $"testforblock {target.X} {target.Y} {target.Z} minecraft:{block} -1 {{Items:[]}}" : $"execute unless data block {target.X} {target.Y} {target.Z} Items[0]";
                string expected = protocol != 47 ? "Test passed" : block == "ender_chest" ? "Found " + Username : $"Successfully found the block at {target.X},{target.Y},{target.Z}.";
                Assert.Equal(expected, await ObserveStoragePredicate(command));
            }

            Task<string> ObserveStoragePredicate(string command)
            {
                // The 1.14 through 1.14.2 RCON dispatcher runs outside the world owner, where native block-entity lookup returns null. Execute the unchanged predicate through the proven console queue and retain its exact feedback and completion marker.
                bool blockEntity = command.StartsWith("execute if data block ", StringComparison.Ordinal) || command.StartsWith("execute unless data block ", StringComparison.Ordinal);
                return (protocol is 477 or 480 or 485) && blockEntity ? server.ConsoleFeedbackAsync(command, ct) : server.CommandAsync(command, ct);
            }

            async Task ClearStored(string block)
            {
                int slot = block == "chest" ? 0 : 2;
                string command = protocol >= 755
                    ? $"item replace block {target.X} {target.Y} {target.Z} container.{slot} with minecraft:air"
                    : $"replaceitem block {target.X} {target.Y} {target.Z} {MenuCommandSlot(protocol, "container." + slot)} minecraft:air";
                await server.ConsoleAsync(command, ct);
                string predicate = protocol == 47
                    ? $"testforblock {target.X} {target.Y} {target.Z} minecraft:{block} -1 {{Items:[]}}"
                    : $"execute unless data block {target.X} {target.Y} {target.Z} Items[0]";
                string expected = protocol == 47 ? $"Successfully found the block at {target.X},{target.Y},{target.Z}." : "Test passed";
                string response = await ObserveStoragePredicate(predicate);
                Assert.Equal(expected, response);
                sentinelCleanups.Add(new { block, slot, command, predicate, response, purpose = "remove the already asserted sentinel before native block replacement can drop it into later empty-hand controls" });
                await server.WriteAsync("menu-sentinel-cleanup.json", sentinelCleanups);
            }

            async Task CloseWithNativeFence(int windowId)
            {
                int mark = capture.Mark();
                await client.Actions.Inventory.CloseAsync(ct);
                Assert.False(await client.InvokeAsync(c => c.State.Inventory.HasOpenContainer, ct));
                int floorState = await client.InvokeAsync(c => c.State.World.GetBlock(floor).StateId, ct);
                int upperState = await client.InvokeAsync(c => c.State.World.GetBlock(floor.Above()).StateId, ct);
                await client.Actions.Interaction.PlaceBlockAsync(floor, Direction.Up, new Vec3d(.5, 1, .5), ct: ct);
                CapturedFrame processing = await ProcessingWitness(mark, floor, floor.Above(), floorState, upperState);
                CapturedFrame[] frames = capture.Since(mark);
                Assert.Equal(windowId, Assert.IsType<ServerboundContainerClosePacket>(Assert.Single(frames, frame => frame.Packet is ServerboundContainerClosePacket).Packet).ContainerId);
                CapturedFrame action = Assert.Single(frames, IsUseOrAnimation);
                AssertMenuUse(action, floor, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main, null, allowHeldStick: true);
                Assert.DoesNotContain(frames, frame => frame.Packet is ClientboundOpenScreenPacket);
                closeFences.Add(new { windowId, packets = 1, kind = "ordered close followed by an explicit pass use on stone; native processing reply proves the close was handled before inventory setup", processingMilliseconds = processing.Milliseconds });
                await server.WriteAsync("menu-close-fences.json", closeFences);
            }

            async Task<CapturedFrame> ProcessingWitness(int mark, BlockPos clicked, BlockPos neighbor, int clickedState, int neighborState)
            {
                await WaitAsync(() => capture.Since(mark).Any(IsUseOrAnimation), "one explicit menu use", ct);
                CapturedFrame useFrame = capture.Since(mark).First(IsUseOrAnimation);
                if (protocol >= 759)
                {
                    int sequence = Assert.IsType<ServerboundUseItemOnPacket>(useFrame.Packet).Sequence;
                    Assert.True(sequence > previousSequence);
                    previousSequence = sequence;
                    await WaitAsync(() => capture.Since(mark).Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == sequence), "matching native use acknowledgment", ct);
                    CapturedFrame acknowledgment = Assert.Single(capture.Since(mark), frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == sequence);
                    Assert.True(acknowledgment.Milliseconds >= useFrame.Milliseconds);
                    return acknowledgment;
                }
                await WaitAsync(() => ReplyPairs().Length > 0, "native consecutive clicked/adjacent responses after use", ct);
                (CapturedFrame first, CapturedFrame second) = Assert.Single(ReplyPairs());
                Assert.True(first.Milliseconds >= useFrame.Milliseconds && second.Milliseconds >= first.Milliseconds);
                return second;

                (CapturedFrame First, CapturedFrame Second)[] ReplyPairs()
                {
                    CapturedFrame[] inbound = capture.Since(mark).Where(frame => frame.Flow == PacketFlow.Clientbound).ToArray();
                    return Enumerable.Range(1, Math.Max(0, inbound.Length - 1))
                        .Where(index => inbound[index - 1].Packet is ClientboundBlockUpdatePacket first && first.Position == clicked && first.BlockStateId == clickedState && inbound[index].Packet is ClientboundBlockUpdatePacket second && second.Position == neighbor && second.BlockStateId == neighborState)
                        .Select(index => (inbound[index - 1], inbound[index])).ToArray();
                }
            }

            void AssertMenuUse(CapturedFrame frame, BlockPos clicked, Direction clickedFace, Vec3d hit, Hand hand, string? main, bool allowHeldStick = false)
            {
                if (protocol == 47)
                {
                    var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(frame.Packet);
                    Assert.Equal(clicked, use.Position);
                    Assert.Equal((int)clickedFace, use.Face);
                    Assert.True(Matches(use.HeldItem, main) || allowHeldStick && Matches(use.HeldItem, "stick"));
                    Assert.Equal((byte)(hit.X * 16), use.CursorX);
                    Assert.Equal((byte)(hit.Y * 16), use.CursorY);
                    Assert.Equal((byte)(hit.Z * 16), use.CursorZ);
                }
                else
                {
                    var use = Assert.IsType<ServerboundUseItemOnPacket>(frame.Packet);
                    Assert.Equal(clicked, use.Position);
                    Assert.Equal((int)clickedFace, use.Face);
                    Assert.Equal((int)hand, use.Hand);
                    Assert.Equal((float)hit.X, use.CursorX);
                    Assert.Equal((float)hit.Y, use.CursorY);
                    Assert.Equal((float)hit.Z, use.CursorZ);
                    Assert.False(use.Inside);
                    Assert.False(use.WorldBorderHit);
                }
            }

            async Task SetCell(BlockPos position, string block)
            {
                await server.ConsoleAsync($"setblock {position.X} {position.Y} {position.Z} minecraft:{block}", ct);
                await WaitAsync(client, c => c.State.World.GetBlock(position).Block.Id == Identifier.Minecraft(block), "native menu setup cell " + block, ct);
            }

            async Task AssertScore(string block)
            {
                int count = counters[block];
                string command = protocol == 47
                    ? $"scoreboard players test {Username} {Objective(block)} {count} {count}"
                    : $"execute if score {Username} {Objective(block)} matches {count}";
                Assert.Equal(protocol == 47 ? $"Score {count} is in range {count} to {count}" : "Test passed", await server.CommandAsync(command, ct));
            }

            async Task AssertStored(string block)
            {
                int slot = block == "chest" ? 0 : 2;
                string item = block == "chest" ? "diamond" : "stone";
                int count = block == "chest" ? 3 : 7;
                string stack = protocol >= 766 ? $"{{Slot:{slot}b,id:\"minecraft:{item}\",count:{count}}}" : $"{{Slot:{slot}b,id:\"minecraft:{item}\",Count:{count}b}}";
                string command = protocol == 47
                    ? $"testforblock {target.X} {target.Y} {target.Z} minecraft:{block} -1 {{Items:[{stack}]}}"
                    : $"execute if data block {target.X} {target.Y} {target.Z} Items[{stack}]";
                string expected = protocol == 47 ? $"Successfully found the block at {target.X},{target.Y},{target.Z}." : MenuStorageMatchFeedback(protocol);
                Assert.Equal(expected, await ObserveStoragePredicate(command));
            }

            string Criterion(string block) => protocol == 47
                ? block == "crafting_table" ? "stat.craftingTableInteraction" : block == "furnace" ? "stat.furnaceInteraction" : "stat.chestOpened"
                : "minecraft.custom:minecraft." + (block == "chest" ? "open_chest" : "interact_with_" + block);
            static string Objective(string block) => block == "crafting_table" ? "menuCraft" : block == "furnace" ? "menuFurn" : "menuChest";
            static bool Matches(ItemStack stack, string? item) => item is null ? stack.IsEmpty : !stack.IsEmpty && stack.Item.Id == Identifier.Minecraft(item) && stack.Count == 1;
            static void AssertStack(ItemStack stack, string item, int count) { Assert.False(stack.IsEmpty); Assert.Equal(Identifier.Minecraft(item), stack.Item.Id); Assert.Equal(count, stack.Count); }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "menu activation", capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static string[] AdditionalMenuBlocks(int protocol)
    {
        List<string> blocks = ["trapped_chest", "ender_chest", "dispenser", "dropper", "hopper", "brewing_stand", "enchanting_table", "anvil"];
        if (protocol >= 393)
            blocks.AddRange(["chipped_anvil", "damaged_anvil"]);
        if (protocol >= 477)
            blocks.AddRange(["barrel", "smoker", "blast_furnace", "loom", "cartography_table", "stonecutter", "grindstone", "smithing_table"]);
        if (protocol >= 765)
            blocks.Add("crafter");
        return [.. blocks];
    }

    private static (string Kind, string Title, int Slots, int LegacySlots) MenuExpectation(string block, int protocol) => block switch
    {
        "crafting_table" => (protocol == 47 ? "crafting_table" : "crafting", protocol == 47 ? "tile.workbench.name" : "container.crafting", 46, 0),
        "chest" or "trapped_chest" => (protocol == 47 ? "chest" : "generic_9x3", "container.chest", 63, 27),
        "ender_chest" => (protocol == 47 ? "container" : "generic_9x3", "container.enderchest", 63, 27),
        "barrel" => ("generic_9x3", "container.barrel", 63, 27),
        "furnace" or "smoker" or "blast_furnace" => (block, "container." + block, 39, 3),
        "dispenser" or "dropper" => (protocol == 47 ? block : "generic_3x3", "container." + block, 45, 9),
        "hopper" => ("hopper", "container.hopper", 41, 5),
        "brewing_stand" => ("brewing_stand", "container.brewing", protocol == 47 ? 40 : 41, protocol == 47 ? 4 : 5),
        "enchanting_table" => (protocol == 47 ? "enchanting_table" : "enchantment", "container.enchant", 38, 0),
        "anvil" or "chipped_anvil" or "damaged_anvil" => ("anvil", protocol == 47 ? "tile.anvil.name" : "container.repair", 39, 0),
        "loom" => ("loom", "container.loom", 40, 0),
        "cartography_table" => (protocol < 735 ? "cartography" : "cartography_table", "container.cartography_table", 39, 0),
        "stonecutter" => ("stonecutter", "container.stonecutter", 38, 0),
        "grindstone" => ("grindstone", "container.grindstone_title", 39, 0),
        "smithing_table" => ("smithing", "container.upgrade", protocol >= 763 ? 40 : 39, 0),
        "crafter" => ("crafter_3x3", "container.crafter", 46, 9),
        _ => throw new InvalidOperationException("No native menu expectation for " + block)
    };

    private static string MenuCommandSlot(int protocol, string slot) => protocol == 47 ? "slot." + slot : slot;

    private static string MenuStorageMatchFeedback(int protocol) => protocol switch
    {
        477 or 498 or 735 or 765 or 766 => "Test passed, count: 1",
        777 => "Test passed. Count: 1",
        _ => throw new InvalidOperationException("No native storage feedback binding for this protocol.")
    };

    private static bool MenuChatContains(object? packet, string marker)
    {
        Component? component = packet switch
        {
            ClientboundSystemChatPacket system => system.Content,
            ClientboundLegacyChatPacket legacy => legacy.Message,
            ClientboundDisguisedChatPacket disguised => disguised.Message,
            _ => null
        };
        return component is not null && Contains(component);
        bool Contains(Component value) => value.Content is TextContent text && text.Text.Equals(marker, StringComparison.Ordinal)
            || value.Content is TranslatableContent translated && translated.Args.Any(Contains)
            || value.Children.Any(Contains);
    }

    private static int ReplayMenuFrames(CapturedFrame[] frames, JavaVersion version, RegistryAccess registries, IReadOnlyCollection<string> observationMarkers, List<object> dispositions)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        int count = 0;
        int frameIndex = -1;
        var seenMarkers = new HashSet<string>(StringComparer.Ordinal);
        Assert.Equal(observationMarkers.Count, observationMarkers.Distinct(StringComparer.Ordinal).Count());
        foreach (CapturedFrame frame in frames)
        {
            frameIndex++;
            if (frame.Packet is not (ClientboundConfigUpdateEnabledFeaturesPacket or ClientboundOpenScreenPacket or ClientboundContainerSetContentPacket or ClientboundContainerSetSlotPacket or ClientboundContainerClosePacket or ServerboundContainerClosePacket or ClientboundBlockChangedAckPacket or ClientboundLevelChunkPacket or ClientboundMapChunkBulkPacket or ClientboundSystemChatPacket or ClientboundLegacyChatPacket or ClientboundDisguisedChatPacket))
                continue;
            PhaseRegistry registry = version.Protocol.GetRegistry(frame.Phase, frame.Flow);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            bool chat = frame.Packet is ClientboundSystemChatPacket or ClientboundLegacyChatPacket or ClientboundDisguisedChatPacket;
            string? marker = chat ? observationMarkers.FirstOrDefault(value => MenuChatContains(frame.Packet, value)) : null;
            if (chat)
                Assert.Equal(frame.Packet, decoded);
            if (marker is not null)
                Assert.True(seenMarkers.Add(marker));
            switch (frame.Packet)
            {
                case ClientboundConfigUpdateEnabledFeaturesPacket expectedFeatures:
                    var actualFeatures = Assert.IsType<ClientboundConfigUpdateEnabledFeaturesPacket>(decoded);
                    Assert.Equal(expectedFeatures.Features, actualFeatures.Features);
                    break;
                case ClientboundOpenScreenPacket expectedOpen:
                    var actualOpen = Assert.IsType<ClientboundOpenScreenPacket>(decoded);
                    Assert.Equal(expectedOpen.ContainerId, actualOpen.ContainerId);
                    Assert.Equal(expectedOpen.MenuTypeId, actualOpen.MenuTypeId);
                    Assert.Equal(expectedOpen.LegacyType, actualOpen.LegacyType);
                    Assert.Equal(expectedOpen.LegacySlotCount, actualOpen.LegacySlotCount);
                    Assert.Equal(expectedOpen.LegacyEntityId, actualOpen.LegacyEntityId);
                    Assert.Equal(expectedOpen.Title.ToPlainText(), actualOpen.Title.ToPlainText());
                    break;
                case ClientboundContainerSetContentPacket expectedContent:
                    var actualContent = Assert.IsType<ClientboundContainerSetContentPacket>(decoded);
                    Assert.Equal(expectedContent.ContainerId, actualContent.ContainerId);
                    Assert.Equal(expectedContent.StateId, actualContent.StateId);
                    Assert.Equal(expectedContent.Items, actualContent.Items);
                    Assert.Equal(expectedContent.CarriedItem, actualContent.CarriedItem);
                    break;
                case ClientboundDisguisedChatPacket expectedChat:
                    var actualChat = Assert.IsType<ClientboundDisguisedChatPacket>(decoded);
                    Assert.Equal(expectedChat.Message.ToPlainText(), actualChat.Message.ToPlainText());
                    Assert.Equal(expectedChat.ChatTypeId, actualChat.ChatTypeId);
                    Assert.Equal(expectedChat.SenderName.ToPlainText(), actualChat.SenderName.ToPlainText());
                    Assert.Equal(expectedChat.TargetName?.ToPlainText(), actualChat.TargetName?.ToPlainText());
                    break;
                case ClientboundLevelChunkPacket expectedChunk:
                    var actualChunk = Assert.IsType<ClientboundLevelChunkPacket>(decoded);
                    Assert.Equal(expectedChunk.ChunkX, actualChunk.ChunkX);
                    Assert.Equal(expectedChunk.ChunkZ, actualChunk.ChunkZ);
                    Assert.Equal(expectedChunk.FullChunk, actualChunk.FullChunk);
                    Assert.Equal(expectedChunk.RawBody, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(actualChunk)));
                    break;
                case ClientboundMapChunkBulkPacket expectedBulk:
                    var actualBulk = Assert.IsType<ClientboundMapChunkBulkPacket>(decoded);
                    Assert.Equal(expectedBulk.SkyLight, actualBulk.SkyLight);
                    Assert.Equal(expectedBulk.Columns.Count, actualBulk.Columns.Count);
                    Assert.Equal(expectedBulk.Columns.Select(column => column.Position), actualBulk.Columns.Select(column => column.Position));
                    var bulkWire = Assert.IsType<MapChunkBulkWireBody>(ChunkCodecs.DecodeStructure(actualBulk));
                    Assert.Equal(actualBulk.SkyLight, bulkWire.SkyLight);
                    Assert.Equal(actualBulk.Columns.Count, bulkWire.Chunks.Count);
                    for (int i = 0; i < bulkWire.Chunks.Count; i++)
                    {
                        var column = actualBulk.Columns[i];
                        LegacyChunkWireBody chunkWire = bulkWire.Chunks[i];
                        Assert.True(chunkWire.GroundUp);
                        Assert.Equal((chunkWire.X, chunkWire.Z), (column.Position.X, column.Position.Z));
                        Assert.Equal(0, column.MinY);
                        int expectedWireHeight = 16;
                        for (int section = 0; section < 16; section++)
                        {
                            bool present = (chunkWire.PrimaryMask & (1 << section)) != 0;
                            Assert.Equal(present, column.GetSection(section) is not null);
                            if (present)
                                expectedWireHeight = (section + 1) * 16;
                        }
                        Assert.Equal(expectedWireHeight, column.MaxY);
                    }
                    Assert.Equal(expectedBulk.RawBody, ChunkCodecs.EncodeStructural(bulkWire));
                    break;
                default:
                    Assert.Equal(frame.Packet, decoded);
                    break;
            }
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, decoded, context);
            bool byteIdentical = frame.Payload.AsSpan().SequenceEqual(output.WrittenSpan);
            bool ambientChat = chat && marker is null;
            dispositions.Add(new { frameIndex, frame.Milliseconds, frame.Phase, frame.Flow, frame.WireId, packet = decoded.GetType().Name, codec.CodecIdentity, exactObservationMarker = marker, ambientChat, typedSemanticAssertionsPassed = true, byteIdentityRequired = !ambientChat, byteIdentical, nativePayloadBytes = frame.Payload.Length, reencodedPayloadBytes = output.WrittenCount, nativePayloadDifference = ambientChat && !byteIdentical ? frame.Payload : null, reencodedPayloadDifference = ambientChat && !byteIdentical ? output.WrittenSpan.ToArray() : null });
            if (ambientChat && !byteIdentical)
                continue;
            Assert.Equal(frame.Payload, output.WrittenSpan.ToArray());
            count++;
        }
        Assert.Equal(observationMarkers.Count, seenMarkers.Count);
        return count;
    }
}
