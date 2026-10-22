using System.Buffers;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Data.Java;
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
    [InlineData("1.14.4", 498)]
    [InlineData("1.20.6", 766)]
    [InlineData("26.3", 777)]
    public async Task MenuUseMatchesNativeOpeningRefusalAndSwing(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct);
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        client.PacketFrameObserved += capture.Observe;
        int opens = 0;
        using IDisposable opened = client.Events.Subscribe<ContainerOpened>(_ => Interlocked.Increment(ref opens));
        bool passed = false;
        var scenarios = new List<object>();
        var closeFences = new List<object>();
        var sentinelCleanups = new List<object>();
        var counters = new Dictionary<string, int> { ["crafting_table"] = 0, ["furnace"] = 0, ["chest"] = 0 };
        int previousSequence = -1;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
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
            int replayed = capture.AssertRoundTrips(client.State.Registries!);
            int menuReplayed = ReplayMenuFrames(capture.Frames, version, client.State.Registries!);
            Assert.True(menuReplayed >= 10);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "menu server-observed disconnect", ct);
            await server.WriteAsync("menu-acceptance.json", new { release, protocol, physics = true, normalLauncher = true, captureStartsBeforePlay = true, target, face, cursor, nativeOpenCounters = counters, scenarios, closeFences, sentinelCleanups, replayed, menuReplayed, peerObservedDisconnect = true });
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
                int targetState = await client.InvokeAsync(c => c.State.World.GetBlock(target).StateId, ct);
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
                    string expectedKind = protocol == 47 ? block : block == "chest" ? "generic_9x3" : block == "crafting_table" ? "crafting" : "furnace";
                    string titleKey = block == "crafting_table" ? protocol == 47 ? "tile.workbench.name" : "container.crafting" : "container." + block;
                    Assert.Equal(titleKey, Assert.IsType<TranslatableContent>(window.Title.Content).Key);
                    if (protocol == 47)
                    {
                        Assert.Equal("minecraft:" + block, window.LegacyType);
                        Assert.Equal(-1, window.MenuTypeId);
                        Assert.Equal(block == "chest" ? 27 : block == "furnace" ? 3 : 0, window.LegacySlotCount);
                    }
                    else
                    {
                        Assert.Null(window.LegacyType);
                        Assert.Equal(client.State.Registries!.MenuTypes[Identifier.Minecraft(expectedKind)].NetworkId, window.MenuTypeId);
                    }
                    int slots = block == "crafting_table" ? 46 : block == "furnace" ? 39 : 63;
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
                    counters[block]++;
                }
                else
                    Assert.False(await client.InvokeAsync(c => c.State.Inventory.HasOpenContainer, ct));
                foreach (string family in counters.Keys)
                    await AssertScore(family);
                await AssertBlockAsync(server, protocol, target, block, ct);
                if (block is "furnace" or "chest")
                    await AssertStored(block);
                scenarios.Add(new { block, blocked, sneaking, main, off, hand, clientSuccess, nativeOpen, rejectHand, packets = actions.Length, windowId, nativeCounter = counters[block], processingKind = rejectHand ? "pre-send hand rejection; no native use" : protocol >= 759 ? "matching native block-change acknowledgment" : "ordered native clicked/adjacent block-change replies", processingMilliseconds = processing?.Milliseconds, postObservationMilliseconds = observed.Milliseconds, observationRole = "post-observation marker, never the use-processing witness" });
                await server.WriteAsync("menu-scenarios.json", scenarios);
                if (nativeOpen)
                    await CloseWithNativeFence(windowId!.Value);
                if (block is "furnace" or "chest")
                    await ClearStored(block);
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
                string response = await server.CommandAsync(predicate, ct);
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
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockUpdatePacket update && update.Position == neighbor && update.BlockStateId == neighborState), "native adjacent-cell response after use", ct);
                CapturedFrame first = Assert.Single(capture.Since(mark), frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockUpdatePacket update && update.Position == clicked && update.BlockStateId == clickedState);
                CapturedFrame second = Assert.Single(capture.Since(mark), frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockUpdatePacket update && update.Position == neighbor && update.BlockStateId == neighborState);
                Assert.True(first.Milliseconds >= useFrame.Milliseconds && second.Milliseconds >= first.Milliseconds);
                return second;
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
                string expected = protocol == 47 ? $"Successfully found the block at {target.X},{target.Y},{target.Z}." : "Test passed";
                Assert.Equal(expected, await server.CommandAsync(command, ct));
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

    private static string MenuCommandSlot(int protocol, string slot) => protocol == 47 ? "slot." + slot : slot;

    private static bool MenuChatContains(object? packet, string marker)
    {
        Component? component = packet is ClientboundSystemChatPacket system ? system.Content : packet is ClientboundLegacyChatPacket legacy ? legacy.Message : null;
        return component is not null && Contains(component);
        bool Contains(Component value) => value.Content is TextContent text && text.Text.Contains(marker, StringComparison.Ordinal)
            || value.Content is TranslatableContent translated && translated.Args.Any(Contains)
            || value.Children.Any(Contains);
    }

    private static int ReplayMenuFrames(CapturedFrame[] frames, JavaVersion version, RegistryAccess registries)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        int count = 0;
        foreach (CapturedFrame frame in frames)
        {
            if (frame.Packet is not (ClientboundOpenScreenPacket or ClientboundContainerSetContentPacket or ClientboundContainerSetSlotPacket or ClientboundContainerClosePacket or ServerboundContainerClosePacket or ClientboundBlockChangedAckPacket or ClientboundLevelChunkPacket or ClientboundMapChunkBulkPacket or ClientboundSystemChatPacket or ClientboundLegacyChatPacket))
                continue;
            PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, frame.Flow);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            switch (frame.Packet)
            {
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
            Assert.Equal(frame.Payload, output.WrittenSpan.ToArray());
            count++;
        }
        return count;
    }
}
