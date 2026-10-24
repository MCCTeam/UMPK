using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> LecternReleaseCases()
    {
        (string Release, int Protocol)[] rows = [("1.14", 477), ("1.15", 573), ("1.20.4", 765), ("1.20.5", 766), ("1.21.2", 768), ("26.2", 776), ("26.3", 777)];
        string? requested = Environment.GetEnvironmentVariable("UMPK_LECTERN_RELEASE");
        Assert.True(requested is null || rows.Any(row => row.Release == requested), "UMPK_LECTERN_RELEASE must name one of the seven bound native releases.");
        foreach ((string release, int protocol) in rows)
            if (requested is null || release == requested)
                yield return [release, protocol];
    }

    [NightlyTheory]
    [MemberData(nameof(LecternReleaseCases))]
    public async Task FilledLecternUseMatchesNativeMenuHandAndSwingBoundaries(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct);
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version, includeEnabledFeatures: true);
        client.PacketFrameObserved += capture.Observe;
        int opens = 0;
        using IDisposable opened = client.Events.Subscribe<ContainerOpened>(_ => Interlocked.Increment(ref opens));
        bool passed = false;
        int score = 0;
        int previousSequence = -1;
        var scenarios = new List<object>();
        var markers = new List<string>();
        var closeFences = new List<object>();
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "lectern initial terrain", ct);
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
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobSpawning false" : "gamerule minecraft:spawn_mobs false", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync($"tp {Username} {x + .5:R} 100 {z + .5:R}", ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await WaitAsync(client, c => c.State.Self.OnGround && c.State.Self.GameMode == GameMode.Survival && Math.Abs(c.State.Self.Position.Y - 100) < .01, "grounded lectern physics owner", ct);
            await WaitAsync(() => capture.Frames.Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundLevelChunkPacket chunk && chunk.FullChunk && chunk.ChunkX == target.X >> 4 && chunk.ChunkZ == target.Z >> 4), "actual native lectern full column", ct);
            await server.ConsoleAsync("scoreboard objectives add lecternUse minecraft.custom:minecraft.interact_with_lectern", ct);
            await server.ConsoleAsync($"scoreboard players set {Username} lecternUse 0", ct);
            // Copy a vanilla-created stack to avoid inventing either the old NBT or modern component representation.
            await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:lectern[facing=north,powered=false,has_book=true]", ct);
            await server.ConsoleAsync($"clear {Username}", ct);
            await server.ConsoleAsync($"give {Username} minecraft:writable_book 1", ct);
            await NativePredicate($"execute if data entity {Username} Inventory[{{Slot:0b,id:\"minecraft:writable_book\"}}]", "native setup book");
            await server.ConsoleAsync($"data modify block {target.X} {target.Y} {target.Z} Book set from entity {Username} Inventory[{{Slot:0b,id:\"minecraft:writable_book\"}}]", ct);
            await NativeBook();
            await server.ConsoleAsync($"clear {Username}", ct);
            await WaitAsync(client, c => c.State.World.GetBlock(target).Block.Id == Identifier.Minecraft("lectern") && c.State.World.GetBlock(target).TryGetProperty("has_book", out string hasBook) && hasBook == "true" && c.State.Inventory.GetSlot(36).IsEmpty && c.State.Inventory.GetSlot(45).IsEmpty, "real filled lectern and empty hands", ct);
            await Use("main-empty", Hand.Main, false, null, null, false, true);
            await Use("off-empty", Hand.Off, false, null, null, false, protocol < 766);
            if (protocol is 477 or 766 or 777)
            {
                await Use("standing-stick", Hand.Main, false, "stick", null, false, true);
                await Use("sneaking-empty", Hand.Main, true, null, null, false, true);
                await Use("sneaking-main-stick", Hand.Main, true, "stick", null, false, false);
                await Use("sneaking-off-stick", Hand.Main, true, null, "stick", false, false);
                await Use("adventure-stick", Hand.Main, false, "stick", null, true, true);
            }
            Assert.Equal(protocol is 477 or 766 or 777 ? 7 : 2, scenarios.Count);
            int replayed = capture.AssertRoundTrips(client.State.Registries!);
            var dispositions = new List<object>();
            int menuReplayed = ReplayMenuFrames(capture.Frames, version, client.State.Registries!, markers, dispositions);
            await server.WriteAsync("lectern-replay.json", new { replayed, menuReplayed, exactMarkers = markers, frames = dispositions });
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "lectern server-observed disconnect", ct);
            await server.WriteAsync("lectern-acceptance.json", new { release, protocol, physics = true, actualNativeFullColumn = true, target, face, cursor, scenarios, closeFences, finalNativeStat = score, replayed, menuReplayed, peerObservedDisconnect = true, scope = "Filled lectern only; local-only777 policy is corroborated by framed/source evidence, while this headless leg proves wire absence and native server outcomes." });
            passed = true;

            async Task Use(string scene, Hand hand, bool sneaking, string? main, string? off, bool adventure, bool success)
            {
                Assert.False(await client.InvokeAsync(c => c.State.Inventory.HasOpenContainer, ct));
                await client.Actions.Movement.SetSneakingAsync(false, ct);
                await server.ConsoleAsync($"gamemode {(adventure ? "adventure" : "survival")} {Username}", ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                if (main is not null)
                    await server.ConsoleAsync($"give {Username} minecraft:{main} 1", ct);
                if (off is not null)
                    await server.ConsoleAsync(protocol >= 755 ? $"item replace entity {Username} weapon.offhand with minecraft:{off}" : $"replaceitem entity {Username} weapon.offhand minecraft:{off}", ct);
                await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
                await WaitAsync(client, c => Matches(c.State.Inventory.GetSlot(36), main) && Matches(c.State.Inventory.GetSlot(45), off) && c.State.Self.GameMode == (adventure ? GameMode.Adventure : GameMode.Survival), "native lectern hand/mode setup", ct);
                await client.Actions.Movement.SetSneakingAsync(sneaking, ct);
                await NativeHands(main, off);
                await NativeBook();
                await NativePredicate($"execute if score {Username} lecternUse matches {score}", "native stat before action");
                await client.InvokeAsync(c =>
                {
                    Assert.Equal(sneaking, c.State.Self.Sneaking);
                    Assert.True(c.State.Self.OnGround);
                    Assert.True(Math.Abs(c.State.Self.Position.Y - 100) < .01);
                    Assert.True(c.State.World.GetBlock(target).TryGetProperty("has_book", out string hasBook));
                    Assert.Equal("true", hasBook);
                    return true;
                }, ct);
                int mark = capture.Mark();
                int beforeOpens = Volatile.Read(ref opens);
                int state = await client.InvokeAsync(c => c.State.World.GetBlock(target).StateId, ct);
                int neighbor = await client.InvokeAsync(c => c.State.World.GetBlock(adjacent).StateId, ct);
                await client.Actions.Interaction.PlaceBlockAsync(target, face, cursor, hand, ct: ct);
                CapturedFrame processed = await ProcessingWitness(mark, target, adjacent, state, neighbor);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                CapturedFrame observed = await NativePredicate("execute", "lectern post-action observation");
                Assert.True(observed.Milliseconds - processed.Milliseconds >= 2000);
                CapturedFrame[] frames = capture.Since(mark);
                CapturedFrame[] actions = frames.Where(IsUseOrAnimation).ToArray();
                Assert.Equal(success && protocol != 777 ? 2 : 1, actions.Length);
                AssertUse(actions[0], target, face, cursor, hand);
                if (success && protocol != 777)
                    Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                Assert.DoesNotContain(frames, frame => frame.Packet is ServerboundPlayerActionPacket or ServerboundPunchPacket);
                ClientboundOpenScreenPacket[] windows = frames.Select(frame => frame.Packet).OfType<ClientboundOpenScreenPacket>().ToArray();
                Assert.Equal(success ? 1 : 0, windows.Length);
                Assert.Equal(beforeOpens + (success ? 1 : 0), Volatile.Read(ref opens));
                int? windowId = null;
                if (success)
                {
                    ClientboundOpenScreenPacket window = Assert.Single(windows);
                    windowId = window.ContainerId;
                    Assert.True(window.ContainerId > 0);
                    Assert.Equal(client.State.Registries!.MenuTypes[Identifier.Minecraft("lectern")].NetworkId, window.MenuTypeId);
                    Assert.Equal("container.lectern", Assert.IsType<TranslatableContent>(window.Title.Content).Key);
                    ClientboundContainerSetContentPacket[] contents = frames.Select(frame => frame.Packet).OfType<ClientboundContainerSetContentPacket>().Where(packet => packet.ContainerId == window.ContainerId).ToArray();
                    Assert.NotEmpty(contents);
                    Assert.All(contents, packet => AssertBook(Assert.Single(packet.Items)));
                    await client.InvokeAsync(c =>
                    {
                        Assert.True(c.State.Inventory.HasOpenContainer);
                        Assert.Equal(window.ContainerId, c.State.Inventory.OpenWindowId);
                        Assert.Equal("minecraft:lectern", c.State.Inventory.OpenContainerMenuType?.Id.ToString());
                        AssertBook(Assert.Single(c.State.Inventory.ContainerSlots!));
                        return true;
                    }, ct);
                    score++;
                }
                else
                    Assert.False(await client.InvokeAsync(c => c.State.Inventory.HasOpenContainer, ct));
                await NativePredicate($"execute if score {Username} lecternUse matches {score}", "native stat after action");
                await NativeBook();
                await NativeHands(main, off);
                Assert.Equal(state, await client.InvokeAsync(c => c.State.World.GetBlock(target).StateId, ct));
                Assert.Equal(neighbor, await client.InvokeAsync(c => c.State.World.GetBlock(adjacent).StateId, ct));
                scenarios.Add(new { scene, hand, sneaking, main, off, adventure, expectedLocalResult = success ? "Success" : "Pass", nativeOpen = success, actionPackets = actions.Length, swing = success ? protocol == 777 ? "local policy; no wire swing" : "packet" : "none", windowId, nativeStat = score, processingMilliseconds = processed.Milliseconds, postObservationMilliseconds = observed.Milliseconds });
                await server.WriteAsync("lectern-scenarios.json", scenarios);
                if (windowId is { } id)
                    await CloseWithNativeFence(id);
            }

            async Task<CapturedFrame> NativePredicate(string command, string purpose)
            {
                string marker = "lectern-" + Guid.NewGuid().ToString("N");
                markers.Add(marker);
                int mark = capture.Mark();
                await server.ConsoleAsync(command + " run say " + marker, ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => MenuChatContains(frame.Packet, marker)), purpose, ct);
                return Assert.Single(capture.Since(mark), frame => MenuChatContains(frame.Packet, marker));
            }

            Task<CapturedFrame> NativeBook() => NativePredicate($"execute if block {target.X} {target.Y} {target.Z} minecraft:lectern[has_book=true,powered=false] if block {adjacent.X} {adjacent.Y} {adjacent.Z} minecraft:air if block {floor.X} {floor.Y} {floor.Z} minecraft:stone if data block {target.X} {target.Y} {target.Z} Book{{id:\"minecraft:writable_book\",{CountTag()}}}", "native unchanged filled lectern book");
            Task<CapturedFrame> NativeHands(string? main, string? off)
            {
                string offhandPath = protocol >= 770
                    ? off is null ? "equipment.offhand" : $"equipment.offhand{{id:\"minecraft:{off}\",{CountTag()}}}"
                    : $"Inventory[{{Slot:-106b{StackTag(off)}}}]";
                return NativePredicate($"execute {(main is null ? "unless" : "if")} data entity {Username} Inventory[{{Slot:0b{StackTag(main)}}}] {(off is null ? "unless" : "if")} data entity {Username} {offhandPath}", "native exact hand stacks");
            }
            string CountTag() => protocol >= 766 ? "count:1" : "Count:1b";
            string StackTag(string? item) => item is null ? string.Empty : $",id:\"minecraft:{item}\",{CountTag()}";

            async Task CloseWithNativeFence(int windowId)
            {
                int mark = capture.Mark();
                await client.Actions.Inventory.CloseAsync(ct);
                int state = await client.InvokeAsync(c => c.State.World.GetBlock(floor).StateId, ct);
                int neighbor = await client.InvokeAsync(c => c.State.World.GetBlock(floor.Above()).StateId, ct);
                await client.Actions.Interaction.PlaceBlockAsync(floor, Direction.Up, new Vec3d(.5, 1, .5), ct: ct);
                CapturedFrame processed = await ProcessingWitness(mark, floor, floor.Above(), state, neighbor);
                CapturedFrame[] frames = capture.Since(mark);
                Assert.Equal(windowId, Assert.IsType<ServerboundContainerClosePacket>(Assert.Single(frames, frame => frame.Packet is ServerboundContainerClosePacket).Packet).ContainerId);
                AssertUse(Assert.Single(frames, IsUseOrAnimation), floor, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main);
                Assert.DoesNotContain(frames, frame => frame.Packet is ClientboundOpenScreenPacket);
                Assert.False(await client.InvokeAsync(c => c.State.Inventory.HasOpenContainer, ct));
                closeFences.Add(new { windowId, processingMilliseconds = processed.Milliseconds });
            }

            async Task<CapturedFrame> ProcessingWitness(int mark, BlockPos clicked, BlockPos neighbor, int clickedState, int neighborState)
            {
                await WaitAsync(() => capture.Since(mark).Any(IsUseOrAnimation), "one explicit lectern use", ct);
                CapturedFrame use = capture.Since(mark).First(IsUseOrAnimation);
                if (protocol >= 759)
                {
                    int sequence = Assert.IsType<ServerboundUseItemOnPacket>(use.Packet).Sequence;
                    Assert.True(sequence > previousSequence);
                    previousSequence = sequence;
                    await WaitAsync(() => capture.Since(mark).Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == sequence), "matching native lectern use acknowledgment", ct);
                    CapturedFrame acknowledged = Assert.Single(capture.Since(mark), frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == sequence);
                    Assert.True(acknowledged.Milliseconds >= use.Milliseconds);
                    return acknowledged;
                }
                await WaitAsync(() => ReplyPairs().Length != 0, "native consecutive lectern clicked/adjacent replies", ct);
                (CapturedFrame first, CapturedFrame second) = Assert.Single(ReplyPairs());
                Assert.True(first.Milliseconds >= use.Milliseconds && second.Milliseconds >= first.Milliseconds);
                return second;
                (CapturedFrame First, CapturedFrame Second)[] ReplyPairs()
                {
                    CapturedFrame[] inbound = capture.Since(mark).Where(frame => frame.Flow == PacketFlow.Clientbound).ToArray();
                    return Enumerable.Range(1, Math.Max(0, inbound.Length - 1)).Where(index => inbound[index - 1].Packet is ClientboundBlockUpdatePacket a && a.Position == clicked && a.BlockStateId == clickedState && inbound[index].Packet is ClientboundBlockUpdatePacket b && b.Position == neighbor && b.BlockStateId == neighborState).Select(index => (inbound[index - 1], inbound[index])).ToArray();
                }
            }

            static void AssertUse(CapturedFrame frame, BlockPos position, Direction face, Vec3d cursor, Hand hand)
            {
                var packet = Assert.IsType<ServerboundUseItemOnPacket>(frame.Packet);
                Assert.Equal((position, (int)face, (int)hand), (packet.Position, packet.Face, packet.Hand));
                Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (packet.CursorX, packet.CursorY, packet.CursorZ));
                Assert.False(packet.Inside);
                Assert.False(packet.WorldBorderHit);
            }
            static bool Matches(ItemStack stack, string? item) => item is null ? stack.IsEmpty : !stack.IsEmpty && stack.Item.Id == Identifier.Minecraft(item) && stack.Count == 1;
            static void AssertBook(ItemStack stack) { Assert.False(stack.IsEmpty); Assert.Equal(Identifier.Minecraft("writable_book"), stack.Item.Id); Assert.Equal(1, stack.Count); }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "filled lectern activation", capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }
}
