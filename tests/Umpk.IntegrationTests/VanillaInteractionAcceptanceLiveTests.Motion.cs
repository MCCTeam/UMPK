using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    private const string MotionPeer = "MotionPeer";

    public static IEnumerable<object[]> MotionReleases()
    {
        string requested = Environment.GetEnvironmentVariable("UMPK_MOTION_RELEASES") ?? "1.21.5,26.3";
        string[] releases = requested == "all"
            ? ["1.21.4", "1.21.5", "1.21.6", "1.21.8", "1.21.10", "1.21.11", "26.1", "26.2", "26.3"]
            : requested.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (string release in releases)
        {
            Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version), $"Unknown motion release {release}.");
            Assert.InRange(version.Version.Protocol, 769, 777);
            yield return [release, version.Version.Protocol];
        }
    }

    [NightlyTheory]
    [MemberData(nameof(MotionReleases))]
    public async Task RemotePlayerMotionChangesPlacementAndUsesNativeCollisionContext(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct, viewDistance: 8, flatWorldSettings: """{"layers":[{"height":164,"block":"minecraft:stone"}],"biome":"minecraft:the_void","features":false,"lakes":false,"structure_overrides":[]}""");
        output.WriteLine($"Motion evidence: {server.DirectoryPath}");
        var ticks = new MotionTickSource();
        await using UmpkClient observer = MotionClient(version, Username, ticks);
        await using UmpkClient peer = MotionClient(version, MotionPeer, null);
        var capture = new Capture(version);
        var terrain = new MotionTerrainReceipts();
        observer.PacketFrameObserved += capture.Observe;
        observer.PacketFrameObserved += terrain.Observe;
        var scenarios = new List<object>();
        bool passed = false;
        int peerId = -1;
        try
        {
            var endpoint = new ServerEndpoint("127.0.0.1", (ushort)server.Port);
            await observer.ConnectAsync(endpoint, ct);
            await peer.ConnectAsync(endpoint, ct);
            await DriveUntilAsync(observer, ticks, c => c.State.Self.HasSpawned && c.State.IsLocalChunkLoaded, "observer native spawn and chunk", ct);
            Assert.True(await peer.Spawned.WaitAsync(ct));
            peerId = await peer.InvokeAsync(c => c.State.Self.EntityId, ct);
            await MotionWaitAsync(observer, c => c.State.Entities.Get(peerId) is not null, "native peer spawn", ct);
            Assert.True(observer.State.Features.Physics && observer.State.Features.Terrain && observer.State.Features.Entities);
            Assert.True(peer.State.Features.Physics && peer.State.Features.Terrain && peer.State.Features.Entities);
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobSpawning false" : "gamerule minecraft:spawn_mobs false", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobLoot false" : "gamerule minecraft:mob_drops false", ct);
            await PrepareNativeMotionFixtureAsync(server, ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await server.ConsoleAsync($"gamemode survival {MotionPeer}", ct);
            await server.ConsoleAsync($"tp {Username} 2.5 100 2.5", ct);
            await server.ConsoleAsync($"tp {MotionPeer} 0.5 100 1.5", ct);
            await DriveUntilAsync(observer, ticks, c => c.State.Entities.Get(peerId) is { } initialPeer && c.State.Self.OnGround && Near(c.State.Self.Position, new Vec3d(2.5, 100, 2.5)) && Near(initialPeer.Position, new Vec3d(.5, 100, 1.5)), "grounded observer and actual tracked initial peer", ct);
            await MotionWaitAsync(peer, c => c.State.Self.OnGround && Near(c.State.Self.Position, new Vec3d(.5, 100, 1.5)), "grounded physics peer", ct);
            for (int i = 0; i < 5; i++)
                await ticks.FireAsync(ct);
            var support = new BlockPos(1, 99, 1);
            var destination = support.Above();
            await AssertNativeColumnAsync(observer, terrain, new ChunkPos(0, 0), ct);
            await server.ConsoleAsync($"clear {Username}", ct);
            capture.Start();
            int giveMark = capture.Mark();
            await server.ConsoleAsync($"give {Username} minecraft:stone 32", ct);
            await observer.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await MotionWaitAsync(observer, c => c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("stone") && c.State.Inventory.GetSlot(36).Count == 32, "observer placement stack", ct);
            if (protocol is 775 or 776)
                await AwaitNativeGiveVisualItemAsync(observer, server, capture, giveMark, ct);
            await PrepareControlledMotionActorsAsync(observer, server, capture, peerId, "ordinary-initial", ct);

            Vec3d before = await RemotePositionAsync();
            int movementMark = capture.Mark();
            await peer.Actions.Movement.MoveToAsync(new Vec3d(1.5, 100, 1.5), ct);
            await MotionWaitAsync(() => capture.Since(movementMark).Any(frame => IsPositionFrame(frame, peerId)), "native ordinary movement packet", ct);
            Vec3d entered = Vec3d.Zero;
            await MotionWaitAsync(async () =>
            {
                entered = await ServerPositionAsync(server, MotionPeer, ct);
                return BlockPos.Containing(entered) == new BlockPos(1, 100, 1);
            }, "server-confirmed public move destination block", ct);
            Assert.Equal(new BlockPos(1, 100, 1), BlockPos.Containing(entered));
            await server.WriteAsync("ordinary-peer-block-entry.json", new { entered, caller = "public MoveToAsync enters the destination block; the same-block request below approaches its point" });
            await peer.Actions.Movement.MoveToAsync(new Vec3d(1.5, 100, 1.5), ct);
            await Task.Delay(150, ct);
            Vec3d serverPeer = await ServerPositionAsync(server, MotionPeer, ct);
            Assert.InRange(serverPeer.X, 1.2, 1.8);
            Assert.InRange(serverPeer.Y, 99.99, 100.01);
            Assert.InRange(serverPeer.Z, 1.2, 1.8);
            Assert.Equal(before, await RemotePositionAsync());
            await server.WriteAsync("ordinary-before-tick.json", await observer.InvokeAsync(c => VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, support, destination), ct));
            int clearUseMark = capture.Mark();
            await observer.Actions.Interaction.PlaceBlockAsync(support, Direction.Up, new Vec3d(.5, 1, .5), ct: ct);
            await MotionTailAsync(capture, server, clearUseMark, ct);
            AssertPlacement(capture.Since(clearUseMark), protocol, support);
            await AssertBlockAsync(server, protocol, destination, "air", ct);
            await AssertCountAsync(server, protocol, 32, ct);

            // The visible peer and server peer deliberately differ until this client's existing clock advances.
            int phaseMark = capture.Mark();
            var ordinaryPhases = new List<Vec3d>();
            Vec3d animated = before;
            for (int phase = 0; phase < 80 && !AabbForStandingPlayer(animated).Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)); phase++)
            {
                await ticks.FireAsync(ct);
                animated = await RemotePositionAsync();
                ordinaryPhases.Add(animated);
            }
            Assert.InRange(animated.X, .701, 1.81);
            Assert.Equal(100, animated.Y, 5);
            Assert.Equal(AabbForStandingPlayer(animated), await observer.InvokeAsync(c => StoredBounds(c, peerId), ct));
            Assert.True(AabbForStandingPlayer(animated).Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)), "Native movement must put the animated player box across the destination.");
            await server.WriteAsync("ordinary-after-tick.json", await observer.InvokeAsync(c => VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, support, destination), ct));
            int blockedUseMark = capture.Mark();
            await observer.Actions.Interaction.PlaceBlockAsync(support, Direction.Up, new Vec3d(.5, 1, .5), ct: ct);
            await MotionTailAsync(capture, server, blockedUseMark, ct);
            AssertUsePacket(Assert.Single(capture.Since(blockedUseMark), IsUseOrAnimation), protocol, support);
            await AssertBlockAsync(server, protocol, destination, "air", ct);
            await AssertCountAsync(server, protocol, 32, ct);
            await peer.Actions.Movement.MoveToAsync(new Vec3d(4.5, 100, 1.5), ct);
            await DriveUntilAsync(observer, ticks, c => c.State.Entities.Get(peerId)!.Position.X > 3.8, "animated peer away from placement", ct);
            await ServerPositionNearAsync(server, MotionPeer, new Vec3d(4.5, 100, 1.5), .4, ct);
            int successfulUseMark = capture.Mark();
            await observer.Actions.Interaction.PlaceBlockAsync(support, Direction.Up, new Vec3d(.5, 1, .5), ct: ct);
            await MotionWaitAsync(observer, c => c.State.World.GetBlock(destination).Block.Id == Identifier.Minecraft("stone") && c.State.Inventory.GetSlot(36).Count == 31, "placement after animated peer clears", ct);
            await AssertBlockAsync(server, protocol, destination, "stone", ct);
            await AssertCountAsync(server, protocol, 31, ct);
            await MotionTailAsync(capture, server, successfulUseMark, ct);
            AssertPlacement(capture.Since(successfulUseMark), protocol, support);
            scenarios.Add(new { name = "ordinary-motion-placement", before, entered, serverPeer, animated, ordinaryPhases, phaseMark, oldVisibleBounds = "use and native success policy; server rejects actual occupied destination", newVisibleBounds = "use without predicted packet swing", cleared = "server stone and stack32-to31", packetSwingObservable = protocol != 777 });

            await server.ConsoleAsync($"tp {Username} 100.5 100 2.5", ct);
            await DriveUntilAsync(observer, ticks, c => c.State.Self.OnGround && Near(c.State.Self.Position, new Vec3d(100.5, 100, 2.5)), "observer far-query platform", ct);
            int queryX = protocol == 770 ? 202 : 101;
            int queryY = protocol == 770 ? 200 : 100;
            var queryCell = new BlockPos(queryX, queryY, 0);
            await AssertNativeColumnAsync(observer, terrain, new ChunkPos(queryX >> 4, 0), ct);
            await AssertNativeColumnAsync(observer, terrain, new ChunkPos(queryX >> 4, -1), ct);
            foreach (string scenario in new[] { "empty", "stone", "cow", "shulker", "border", "unknown-boat" })
            {
                if (scenario == "unknown-boat" && protocol == 769)
                    continue;
                await server.ConsoleAsync("worldborder center 0.0 0.0", ct);
                await server.ConsoleAsync("worldborder set 59999968", ct);
                await PrepareControlledMotionActorsAsync(observer, server, capture, peerId, "context-" + scenario, ct);
                await server.ConsoleAsync($"fill {queryX - 2} {queryY} -2 {queryX + 2} {queryY + 3} 2 minecraft:air", ct);
                await server.ConsoleAsync($"setblock {queryX} {queryY - 1} 0 minecraft:stone", ct);
                await DriveUntilAsync(observer, ticks, c => c.State.Entities.Get(peerId) is not null && !StoredInterpolationActive(c, peerId), $"{scenario}: drain every prior native path before explicit reset", ct);
                await server.ConsoleAsync($"tp {MotionPeer} 0.5 100 0.5", ct);
                await DriveUntilAsync(observer, ticks, c => c.State.Entities.Get(peerId) is { } initialPeer && Near(initialPeer.Position, new Vec3d(.5, 100, .5)) && !StoredInterpolationActive(c, peerId), $"{scenario}: actual tracked initial remote pose and drained prior native path", ct);
                for (int i = 0; i < 5; i++)
                    await ticks.FireAsync(ct);
                await ServerPositionNearAsync(server, MotionPeer, new Vec3d(.5, 100, .5), .01, ct);
                if (scenario == "stone")
                {
                    await server.ConsoleAsync($"setblock {queryX} {queryY} 0 minecraft:stone", ct);
                    await MotionWaitAsync(observer, c => c.State.World.GetBlock(queryCell).Block.Id == Identifier.Minecraft("stone"), "native collision stone update", ct);
                    await AssertBlockAsync(server, protocol, queryCell, "stone", ct);
                }
                else if (scenario is "cow" or "shulker")
                {
                    await server.ConsoleAsync($"summon minecraft:{scenario} {queryX + .5:R} {queryY} 0.5 {{NoAI:1b,NoGravity:1b,Silent:1b,Invulnerable:1b,Tags:[\"motion-context\"]}}", ct);
                    await MotionWaitAsync(observer, c => c.State.Entities.All.Any(entity => entity.Type.Id == Identifier.Minecraft(scenario)), "native collision actor spawn", ct);
                    await ServerPositionNearAsync(server, "@e[tag=motion-context,limit=1]", new Vec3d(queryX + .5, queryY, .5), .01, ct);
                }
                await MotionWaitAsync(observer, c => c.State.World.GetBlock(queryCell).Block.Id == Identifier.Minecraft(scenario == "stone" ? "stone" : "air"), $"{scenario}: known native query cell", ct);
                await MotionWaitAsync(peer, c => c.State.Self.OnGround && c.State.Self.Velocity.X == 0 && c.State.Self.Velocity.Z == 0 && Near(c.State.Self.Position, new Vec3d(.5, 100, .5)), $"{scenario}: grounded settled public movement owner before relative witness", ct);
                await MotionWaitAsync(async () => (await server.CommandAsync($"data get entity {MotionPeer} OnGround", ct)).EndsWith(": 1b", StringComparison.Ordinal)
                    && PositionFrameOnGround(capture.Frames.Last(frame => IsPositionFrame(frame, peerId))), $"{scenario}: native ground and tracker ground agree before relative witness", ct);
                await peer.Actions.Movement.SetRotationAsync(-90, 0, ct);
                await DriveUntilAsync(observer, ticks, c => c.State.Entities.Get(peerId) is { } resetPeer && Near(resetPeer.Position, new Vec3d(.5, 100, .5)) && resetPeer.Yaw == -90 && resetPeer.Pitch == 0 && !StoredInterpolationActive(c, peerId), $"{scenario}: drain the final native reset target and public rotation before the fresh near packet", ct);
                Assert.False(await observer.InvokeAsync(c => StoredInterpolationActive(c, peerId), ct), $"Harness precondition {scenario}: a prior native path must be inactive before the fresh near packet.");
                Assert.Equal(new Vec3d(.5, 100, .5), await RemotePositionAsync());
                float initialYaw = await observer.InvokeAsync(c => c.State.Entities.Get(peerId)!.Yaw, ct);
                float initialPitch = await observer.InvokeAsync(c => c.State.Entities.Get(peerId)!.Pitch, ct);
                int mark = capture.Mark();
                await peer.Actions.Movement.MoveToAsync(new Vec3d(1.5, 100, .5), ct);
                await MotionWaitAsync(async () => BlockPos.Containing(await ServerPositionAsync(server, MotionPeer, ct)) == new BlockPos(1, 100, 0), $"{scenario}: native public move enters the requested block", ct);
                await peer.Actions.Movement.MoveToAsync(new Vec3d(1.5, 100, .5), ct);
                await MotionWaitAsync(peer, c => c.State.Self.OnGround && c.State.Self.Velocity.X == 0 && c.State.Self.Velocity.Z == 0, $"{scenario}: actual public movement stops before captured trajectory", ct);
                Vec3d nearNativePosition = Vec3d.Zero;
                Vec3d nearWirePosition = Vec3d.Zero;
                await MotionWaitAsync(async () =>
                {
                    nearNativePosition = await ServerPositionAsync(server, MotionPeer, ct);
                    CapturedFrame[] relativeFrames = capture.Since(mark).Where(frame => IsPositionFrame(frame, peerId)).ToArray();
                    Assert.All(relativeFrames, frame => Assert.True(frame.Packet is ClientboundMoveEntityPosPacket or ClientboundMoveEntityPosRotPacket, $"Harness precondition {scenario}: actual grounded public motion requires relative packets, got {frame.Packet?.GetType().Name}."));
                    nearWirePosition = relativeFrames.Aggregate(new Vec3d(.5, 100, .5), (position, frame) => position.Add(RelativeDelta(frame.Packet!, protocol)));
                    return relativeFrames.Length != 0 && nearWirePosition == NativeQuantizedPosition(nearNativePosition);
                }, $"{scenario}: every actual relative delta reaches the independently read native endpoint on its exact 1/4096 grid", ct);
                Assert.InRange(nearNativePosition.X, 1.2, 1.8);
                Assert.Equal(100, nearNativePosition.Y);
                Assert.Equal(.5, nearNativePosition.Z);
                Assert.Equal(new Vec3d(.5, 100, .5), await RemotePositionAsync());
                CapturedFrame near = capture.Since(mark).Last(frame => IsPositionFrame(frame, peerId));
                Assert.Equal(NativeQuantizedPosition(nearNativePosition), nearWirePosition);
                AssertNoPeerReplacement(capture.Since(mark), peerId, scenario);
                await server.WriteAsync($"near-{scenario}-public-movement.json", new { requested = new Vec3d(1.5, 100, .5), nearNativePosition, nearWirePosition, initialPosition = new Vec3d(.5, 100, .5), initialYaw, initialPitch, ground = true, caller = "two public MoveToAsync calls: enter block, then approach its point; settled native endpoint is independently quantized using original VecDeltaCodec arithmetic", packets = capture.Since(mark).Where(frame => IsPositionFrame(frame, peerId) || frame.Packet is ClientboundMoveEntityRotPacket rotation && rotation.EntityId == peerId).Select(frame => new { kind = ((IPacket)frame.Packet!).Type.Id.Path, bodyHex = Convert.ToHexStringLower(frame.Payload) }).ToArray() });
                await MotionWaitAsync(peer, c => c.State.Self.OnGround && c.State.Self.Velocity.X == 0 && c.State.Self.Velocity.Z == 0, $"{scenario}: settled public owner before native far teleport", ct);
                string nearGround = string.Empty;
                await MotionWaitAsync(async () => (nearGround = await server.CommandAsync($"data get entity {MotionPeer} OnGround", ct)).EndsWith(": 1b", StringComparison.Ordinal)
                    && PositionFrameOnGround(capture.Frames.Last(frame => IsPositionFrame(frame, peerId))), $"{scenario}: independent native and received near ground before far teleport", ct);
                await server.WriteAsync($"pre-far-{scenario}-native-ground.json", new { nearNativePosition, nearWirePosition, nativeGround = nearGround, receivedGround = true, physics = true });
                if (scenario == "border")
                {
                    int borderMark = capture.Mark();
                    await server.ConsoleAsync("worldborder center 96.0 0.0", ct);
                    await server.ConsoleAsync("worldborder set 10", ct);
                    await MotionWaitAsync(observer, c => c.State.World.Border.CenterX == 96 && c.State.World.Border.CenterZ == 0 && c.State.World.Border.Size == 10
                        && capture.Since(borderMark).Any(frame => frame.Packet is ClientboundSetBorderCenterPacket center && center.CenterX == 96 && center.CenterZ == 0)
                        && capture.Since(borderMark).Any(frame => frame.Packet is ClientboundSetBorderSizePacket size && size.Size == 10), "exact native decimal border packets and applied world context", ct);
                    CapturedFrame centerFrame = Assert.Single(capture.Since(borderMark), frame => frame.Packet is ClientboundSetBorderCenterPacket);
                    CapturedFrame sizeFrame = Assert.Single(capture.Since(borderMark), frame => frame.Packet is ClientboundSetBorderSizePacket);
                    Assert.Equal(new ClientboundSetBorderCenterPacket(96, 0), centerFrame.Packet);
                    Assert.Equal(new ClientboundSetBorderSizePacket(10), sizeFrame.Packet);
                    var border = await observer.InvokeAsync(c => new { c.State.World.Border.CenterX, c.State.World.Border.CenterZ, c.State.World.Border.Size, c.State.World.Border.MinX, c.State.World.Border.MaxX, c.State.World.Border.MinZ, c.State.World.Border.MaxZ }, ct);
                    Assert.Equal(91, border.MinX);
                    Assert.Equal(101, border.MaxX);
                    Assert.Equal(-5, border.MinZ);
                    Assert.Equal(5, border.MaxZ);
                    await server.WriteAsync("border-context-native-packets.json", new { border, center = new { kind = ((IPacket)centerFrame.Packet!).Type.Id.Path, bodyHex = Convert.ToHexStringLower(centerFrame.Payload) }, size = new { kind = ((IPacket)sizeFrame.Packet!).Type.Id.Path, bodyHex = Convert.ToHexStringLower(sizeFrame.Payload) }, originalNativeComparisonContext = "center96,0; size10; exact original codec/listener replay and border collision rounding; decimal coordinates prevent native integer centering" });
                }
                if (scenario == "unknown-boat")
                {
                    await server.ConsoleAsync("summon minecraft:oak_boat 125.5 100 0.5 {NoGravity:1b,Invulnerable:1b,Tags:[\"motion-context\"]}", ct);
                    await MotionWaitAsync(observer, c => c.State.Entities.All.Any(entity => entity.Type.Id == Identifier.Minecraft("oak_boat")), "native unowned hard boat spawn", ct);
                    await ServerPositionNearAsync(server, "@e[tag=motion-context,limit=1]", new Vec3d(125.5, 100, .5), .01, ct);
                }
                int farMark = capture.Mark();
                await server.ConsoleAsync($"tp {MotionPeer} 100.5 100 0.5", ct);
                await MotionWaitAsync(() => capture.Since(farMark).Any(frame => frame.Packet is ClientboundEntityPositionSyncPacket sync && sync.EntityId == peerId), $"{scenario}: native far position sync", ct);
                CapturedFrame far = capture.Since(farMark).First(frame => frame.Packet is ClientboundEntityPositionSyncPacket sync && sync.EntityId == peerId);
                var sync = Assert.IsType<ClientboundEntityPositionSyncPacket>(far.Packet);
                Assert.Equal(new Vec3d(100.5, 100, .5), sync.Values.Position);
                if (protocol == 777)
                {
                    Assert.NotNull(sync.Path);
                    Vec3d pathEndpoint = sync.Path is EntityPositionPath.Linear linear ? linear.Position : Assert.IsType<EntityPositionPath.Stepped>(sync.Path).Steps[^1].Position;
                    Assert.Equal(sync.Values.Position, pathEndpoint);
                }
                await MotionWaitAsync(observer, c => c.State.Entities.Get(peerId)!.Position == sync.Values.Position, "native far sync applied before controlled ticks", ct);
                Assert.Equal(sync.Values.Position, await RemotePositionAsync());
                CapturedFrame[] preTickCapture = capture.Frames;
                int controlledMark = preTickCapture.Length;
                CapturedFrame[] preTickPackets = preTickCapture.Skip(mark).Where(frame => IsPositionFrame(frame, peerId) || frame.Packet is ClientboundMoveEntityRotPacket rotation && rotation.EntityId == peerId).ToArray();
                Assert.True(preTickPackets.Length >= 2, $"Harness precondition {scenario}: retain every actual public near and native far update before controlled ticks.");
                Assert.Contains(preTickPackets, frame => ReferenceEquals(frame, far));
                var phases = new List<Vec3d>();
                var phaseBounds = new List<double[]>();
                for (int phase = 0; phase < 3; phase++)
                {
                    await ticks.FireAsync(ct);
                    Vec3d actual = await RemotePositionAsync();
                    phases.Add(actual);
                    Aabb cached = await observer.InvokeAsync(c => StoredBounds(c, peerId), ct);
                    phaseBounds.Add([cached.MinX, cached.MinY, cached.MinZ, cached.MaxX, cached.MaxY, cached.MaxZ]);
                    if (scenario != "unknown-boat")
                        Assert.Equal(AabbForStandingPlayer(actual), cached);
                    AssertNoRetarget(capture.Since(controlledMark), peerId, scenario);
                    Assert.True(double.IsFinite(actual.X) && actual.Y == 100 && actual.Z == .5, "Every live phase requires the exact original-method packet replay below; no relative frame or 777 path knot may be replaced with a guessed linear path.");
                    if (scenario == "unknown-boat")
                        Assert.Equal(new Vec3d(100.5, 100, .5), actual);
                }
                Vec3d nativePosition = await ServerPositionAsync(server, MotionPeer, ct);
                Assert.True(Near(nativePosition, new Vec3d(100.5, 100, .5)), $"Native peer moved unexpectedly in {scenario}: {nativePosition}.");
                await server.WriteAsync($"far-{scenario}-diagnostic.json", await observer.InvokeAsync(c => VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, support, destination), ct));
                if (scenario == "unknown-boat")
                    Assert.False(await observer.InvokeAsync(c => StoredPositionKnown(c, peerId), ct));
                else
                    Assert.True(await observer.InvokeAsync(c => StoredPositionKnown(c, peerId), ct));
                AssertNoPeerReplacement(capture.Since(mark), peerId, scenario);
                scenarios.Add(new { name = "far-sync-" + scenario, queryCell, near = near.Packet!.GetType().Name, nearNativePosition, nearWirePosition, far = far.Packet!.GetType().Name, phases, phaseBounds, nativePosition, expected = "required exact original-method replay from every actual received relative/rotation/path frame and the far sync", nativeQueryColumnReceived = true, conservativeUnknown = scenario == "unknown-boat", pre770Control = protocol == 769 });
                await server.WriteAsync($"far-{scenario}-native-input.json", new
                {
                    release,
                    protocol,
                    scenario,
                    initialPosition = new[] { .5, 100, .5 },
                    initialYaw,
                    initialPitch,
                    captureStartIndex = mark,
                    captureBeforeTickIndex = controlledMark,
                    firstFarCaptureIndex = Array.FindIndex(preTickCapture, frame => ReferenceEquals(frame, far)),
                    everyPreTickPositionFrameIncluded = true,
                    packets = preTickPackets.Select(frame => new { kind = ((IPacket)frame.Packet!).Type.Id.Path, bodyHex = Convert.ToHexStringLower(frame.Payload) }).ToArray(),
                    phases = phases.Select(position => new[] { position.X, position.Y, position.Z }).ToArray(),
                    phaseBounds,
                    nativeComparisonRequired = scenario != "unknown-boat",
                    unknownReason = scenario == "unknown-boat" ? "tracked boat bounds are deliberately not owned;native would proceed but runtime remains explicitly unknown" : null,
                });
                await server.ConsoleAsync("worldborder set 59999968", ct);
            }
            await MotionTailAsync(capture, server, null, ct);
            RegistryAccess registries = observer.State.Registries!;
            await peer.DisconnectAsync(ct);
            await MotionWaitAsync(observer, c => c.State.Entities.Get(peerId) is null, "native peer removal", ct);
            await observer.DisconnectAsync(ct);
            await MotionWaitAsync(() => server.ServerLog.Contains(MotionPeer + " lost connection", StringComparison.Ordinal) && server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "both native peer disconnect observations", ct);
            int replayed = AssertMotionRoundTrips(version, capture.Frames.Concat(terrain.Frames).ToArray(), registries);
            long recordedBytes = capture.PayloadBytes + terrain.PayloadBytes;
            Assert.False(capture.Overflowed);
            Assert.True(recordedBytes <= 20 * 1024 * 1024, $"Combined motion and terrain receipts exceeded the unchanged 20 MiB cap: {recordedBytes}.");
            await server.WriteAsync("motion-acceptance.json", new { release, protocol, physics = true, manualObserverClock = true, serverViewDistance = 8, realNativePackets = true, originalNativeReplayStatus = "required external comparison; this test result alone does not complete acceptance", replayed, recordedBytes, scenarios, peerRemoval = true, peerObservedDisconnect = true, limits = "standing unmounted default-scale remote player; native server plus original-method witness, not graphical-client acceptance;777 local-only swing is not independently observable" });
            passed = true;
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("motion-capture.json", capture.Frames);
            await server.WriteAsync("motion-terrain-receipts.json", terrain.Frames);
            await server.WriteAsync("motion-progress.json", scenarios);
            await server.WriteAsync("motion-result.json", new { passed, release, protocol, peerId, capture.PayloadBytes, terrainBytes = terrain.PayloadBytes, capture.Overflowed });
            foreach (UmpkClient client in new[] { peer, observer })
                if (client.Status != ClientStatus.Disconnected)
                {
                    using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await client.DisconnectAsync(cleanupDeadline.Token);
                }
        }

        Task<Vec3d> RemotePositionAsync() => observer.InvokeAsync(c => c.State.Entities.Get(peerId)!.Position, ct);
    }

    private static async Task AwaitNativeGiveVisualItemAsync(UmpkClient observer, VanillaInteractionAcceptanceHarness server, Capture capture, int giveMark, CancellationToken ct)
    {
        int itemType = observer.State.Registries!.EntityTypes[Identifier.Minecraft("item")].NetworkId;
        await MotionWaitAsync(() => capture.Since(giveMark).Any(frame => frame.Packet is ClientboundAddEntityPacket added && added.TypeId == itemType), "the native give command's actual visual item add", ct);
        CapturedFrame addFrame = Assert.Single(capture.Since(giveMark), frame => frame.Packet is ClientboundAddEntityPacket added && added.TypeId == itemType);
        int itemId = Assert.IsType<ClientboundAddEntityPacket>(addFrame.Packet).EntityId;
        await MotionWaitAsync(() => capture.Since(giveMark).Any(frame => frame.Packet is ClientboundSetEntityDataPacket metadata && metadata.EntityId == itemId && metadata.Metadata.Entries.Any(entry => entry.Index == 8 && entry.Value.Kind == MetadataValueKind.Slot && entry.Value.AsSlot() is ItemStack item && item.Item.Id == Identifier.Minecraft("stone") && item.Count == 1)), "native give visual item metadata: one stone", ct);
        await MotionWaitAsync(() => capture.Since(giveMark).Any(frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(itemId)), "the same native give visual item's actual removal", ct);
        await MotionWaitAsync(observer, client => client.State.Entities.Get(itemId) is null, "native give visual item removed from the client owner", ct);
        string nativeAbsence = await server.CommandAsync("execute unless entity @e[type=minecraft:item]", ct);
        Assert.Equal("Test passed", nativeAbsence);
        CapturedFrame[] itemFrames = capture.Since(giveMark).Where(frame => frame.Packet is ClientboundAddEntityPacket added && added.EntityId == itemId || frame.Packet is ClientboundSetEntityDataPacket metadata && metadata.EntityId == itemId || frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(itemId)).ToArray();
        Assert.Single(itemFrames, frame => frame.Packet is ClientboundAddEntityPacket);
        Assert.True(itemFrames[0].Packet is ClientboundAddEntityPacket);
        Assert.True(itemFrames[^1].Packet is ClientboundRemoveEntitiesPacket);
        await server.WriteAsync("native-give-visual-item-fence.json", new { giveMark, itemId, nativeAbsence, actualClientRemoval = true, packetFrames = itemFrames.Select(frame => new { frame.Milliseconds, frame.WireId, kind = ((IPacket)frame.Packet!).Type.Id.Path, bodyHex = Convert.ToHexStringLower(frame.Payload) }), source = "GiveCommand creates one fake item; makeFakeItem sets age5999 and native tick discards at6000; no guessed delay or synthetic removal", unchangedAmbientCleanup = true });
    }

    private static async Task PrepareControlledMotionActorsAsync(UmpkClient observer, VanillaInteractionAcceptanceHarness server, Capture capture, int peerId, string purpose, CancellationToken ct)
    {
        var passes = new List<object>();
        bool settled = false;
        for (int pass = 0; pass < 8; pass++)
        {
            int mark = capture.Mark();
            int[] ambientIds = await observer.InvokeAsync(c => c.State.Entities.All.Where(entity => entity.Type.Id != Identifier.Minecraft("player")).Select(entity => entity.Id).ToArray(), ct);
            await server.WriteAsync($"{purpose}-native-cleanup-pass-{pass}-start.json", new { ambientIds, peerId, mark });
            string nativeBefore = await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct);
            string? nativeStaging = null;
            if (!NativeActorsEmpty(nativeBefore))
            {
                nativeStaging = await server.CommandAsync("tp @e[type=!minecraft:player] 8.5 100 4.5", ct);
                Assert.StartsWith("Teleported ", nativeStaging, StringComparison.Ordinal);
            }
            await server.ConsoleAsync("kill @e[type=!minecraft:player]", ct);
            string cleanupMarker = "motion-native-cleanup-" + Guid.NewGuid().ToString("N");
            await server.ConsoleAsync("say " + cleanupMarker, ct);
            await MotionWaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundDisguisedChatPacket chat && chat.Message.ToPlainText().Contains(cleanupMarker, StringComparison.Ordinal)), $"cleanup pass{pass}: ordered native marker", ct);
            await MotionWaitAsync(() => ambientIds.All(id => capture.Since(mark).Any(frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(id))), $"cleanup pass{pass}: actual removal of every snapshotted ambient actor; dead or missing IDs cannot be skipped", ct);
            int[] remaining = await observer.InvokeAsync(c => c.State.Entities.All.Select(entity => entity.Id).ToArray(), ct);
            string nativeAfter = await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct);
            int[] quietArrivals = [];
            string? nativeQuiet = null;
            if (remaining.Length == 1 && remaining[0] == peerId && NativeActorsEmpty(nativeAfter))
            {
                int quietMark = capture.Mark();
                await MotionTailAsync(capture, server, null, ct);
                quietArrivals = capture.Since(quietMark).Where(frame => frame.Packet is ClientboundAddEntityPacket added && added.EntityId != peerId).Select(frame => ((ClientboundAddEntityPacket)frame.Packet!).EntityId).ToArray();
                remaining = await observer.InvokeAsync(c => c.State.Entities.All.Select(entity => entity.Id).ToArray(), ct);
                nativeQuiet = await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct);
                settled = remaining.Length == 1 && remaining[0] == peerId && quietArrivals.Length == 0 && NativeActorsEmpty(nativeQuiet);
            }
            int[] removedIds = capture.Since(mark).Where(frame => frame.Packet is ClientboundRemoveEntitiesPacket).SelectMany(frame => ((ClientboundRemoveEntitiesPacket)frame.Packet!).EntityIds).Distinct().Order().ToArray();
            passes.Add(new { pass, ambientIds, removedIds, cleanupMarker, nativeStaging, nativeBefore, nativeAfter, nativeQuiet, remaining, quietArrivals, settled });
            await server.WriteAsync($"{purpose}-native-cleanup-passes.json", passes);
            if (settled)
                break;
        }
        Assert.True(settled, "Harness precondition: controlled two-player fixture must survive a measured native traffic tail with no ambient arrivals within eight cleanup passes.");
        string nativePlayers = await server.CommandAsync("list", ct);
        Assert.Contains(Username, nativePlayers, StringComparison.Ordinal);
        Assert.Contains(MotionPeer, nativePlayers, StringComparison.Ordinal);
        Assert.StartsWith("There are 2 of a max of", nativePlayers, StringComparison.Ordinal);
        Assert.Equal(Identifier.Minecraft("player"), await observer.InvokeAsync(c => c.State.Entities.Get(peerId)!.Type.Id, ct));
        await server.WriteAsync($"{purpose}-native-cleanup.json", new { passes, nativePlayers, remainingIds = await observer.InvokeAsync(c => c.State.Entities.All.Select(entity => entity.Id).ToArray(), ct), measuredIntervalStartsAfterCleanup = true, unchangedTrafficTailSeconds = 2.1 });

        static bool NativeActorsEmpty(string response)
        {
            if (response == "Test passed")
                return true;
            Assert.Matches(@"^Test failed(?:, count: |\. Count: )[1-9][0-9]*$", response);
            return false;
        }
    }

    private static async Task PrepareNativeMotionFixtureAsync(VanillaInteractionAcceptanceHarness server, CancellationToken ct)
    {
        string forced = await server.CommandAsync("forceload add -16 -16 207 15", ct);
        Assert.StartsWith("Marked 28 chunks", forced, StringComparison.Ordinal);
        var availability = new List<object>();
        for (int x = -1; x <= 12; x++)
            for (int z = -1; z <= 0; z++)
            {
                var responses = new List<string>();
                await MotionWaitAsync(async () =>
                {
                    string response = await server.CommandAsync($"execute if loaded {x * 16} 100 {z * 16}", ct);
                    responses.Add(response);
                    Assert.True(response is "Test passed" or "Test failed", $"Unexpected native loaded predicate: {response}");
                    return response == "Test passed";
                }, $"native entity-ticking chunk and loaded entities at {x},{z}", ct);
                Assert.Equal("Test passed", responses[^1]);
                availability.Add(new { x, z, responses });
            }

        var generatorChecks = new Dictionary<string, string>();
        foreach (string command in new[] { "execute if biome 0 100 0 minecraft:the_void", "execute if block 0 -64 0 minecraft:stone", "execute if block 0 99 0 minecraft:stone", "execute if block 0 100 0 minecraft:air", "execute if block 202 99 0 minecraft:stone" })
        {
            string response = await server.CommandAsync(command, ct);
            Assert.Equal("Test passed", response);
            generatorChecks.Add(command, response);
        }
        await server.WriteAsync("native-flat-generator-checks.json", new { levelType = "minecraft:flat", layers = 164, block = "minecraft:stone", biome = "minecraft:the_void", minY = -64, nativeFloorY = 99, features = false, lakes = false, structures = false, generatorChecks, substitution = "declared isolated vanilla generator before process startup; original JAR and shared seeds unchanged" });

        // A staging cell makes each fill report an actual successful write even when its volume already has the desired material.
        string floorStage = await server.CommandAsync("setblock -4 99 -4 minecraft:barrier", ct);
        Assert.StartsWith("Changed the block at", floorStage, StringComparison.Ordinal);
        string floor = await server.CommandAsync("fill -4 99 -4 110 99 6 minecraft:stone", ct);
        Assert.StartsWith("Successfully filled ", floor, StringComparison.Ordinal);
        Assert.Equal("Test passed", await server.CommandAsync("execute if block -4 99 -4 minecraft:stone", ct));
        string airStage = await server.CommandAsync("setblock -4 100 -4 minecraft:barrier", ct);
        Assert.StartsWith("Changed the block at", airStage, StringComparison.Ordinal);
        string air = await server.CommandAsync("fill -4 100 -4 110 104 6 minecraft:air", ct);
        Assert.StartsWith("Successfully filled ", air, StringComparison.Ordinal);
        Assert.Equal("Test passed", await server.CommandAsync("execute if block -4 100 -4 minecraft:air", ct));
        await server.WriteAsync("native-fixture-availability.json", new { forced, availability, floorStage, floor, airStage, air, clientFullChunkReceiptStillRequired = true });
    }

    private static UmpkClient MotionClient(JavaVersion version, string name, ITickSource? ticks)
    {
        var builder = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), name)).UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
            .ConfigureFeatures(features => { features.Terrain = true; features.Entities = true; features.Physics = true; features.Pathfinding = false; });
        if (ticks is not null)
            builder.UseTickSource(ticks);
        return builder.Build();
    }

    private static async Task MotionTailAsync(Capture capture, VanillaInteractionAcceptanceHarness server, int? mark, CancellationToken ct)
    {
        if (mark is { } actionMark)
            await MotionWaitAsync(() => capture.Since(actionMark).Length != 0, "actual action frame before capture tail", ct);
        double after = capture.Frames[^1].Milliseconds;
        await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
        await server.ConsoleAsync("say motion-tail-" + Guid.NewGuid().ToString("N"), ct);
        await MotionWaitAsync(() => capture.Frames.Any(frame => frame.Milliseconds >= after + 2000), "fresh traffic after the 2.1 second capture tail", ct);
    }

    private static async Task DriveUntilAsync(UmpkClient client, MotionTickSource ticks, Func<UmpkClient, bool> condition, string description, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!await client.InvokeAsync(condition, deadline.Token))
            {
                await ticks.FireAsync(deadline.Token);
                await Task.Delay(25, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException($"Harness precondition {description} did not hold within 30 seconds.");
        }
    }

    private static Task MotionWaitAsync(UmpkClient client, Func<UmpkClient, bool> condition, string description, CancellationToken ct)
        => MotionWaitAsync(async () => await client.InvokeAsync(condition, ct), description, ct);

    private static Task MotionWaitAsync(Func<bool> condition, string description, CancellationToken ct)
        => MotionWaitAsync(() => Task.FromResult(condition()), description, ct);

    private static async Task MotionWaitAsync(Func<Task<bool>> condition, string description, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!await condition())
                await Task.Delay(25, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException($"Harness precondition {description} did not hold within 30 seconds.");
        }
    }

    private static async Task AssertNativeColumnAsync(UmpkClient observer, MotionTerrainReceipts terrain, ChunkPos column, CancellationToken ct)
    {
        await MotionWaitAsync(observer, c => terrain.Has(column) && c.State.World.GetColumn(new BlockPos(column.X * 16, 100, column.Z * 16)) is not null, $"native full chunk {column.X},{column.Z}; a block update is insufficient", ct);
        var packet = Assert.IsType<ClientboundLevelChunkPacket>(terrain.Frames.Single(frame => frame.Packet is ClientboundLevelChunkPacket chunk && chunk.ChunkX == column.X && chunk.ChunkZ == column.Z).Packet);
        Assert.True(packet.FullChunk);
        Assert.True(packet.Column.SectionCount > 0);
        Assert.NotEmpty(packet.RawBody!);
    }

    private static async Task<Vec3d> ServerPositionAsync(VanillaInteractionAcceptanceHarness server, string selector, CancellationToken ct)
    {
        string response = await server.CommandAsync($"data get entity {selector} Pos", ct);
        MatchCollection values = Regex.Matches(response[(response.IndexOf(':') + 1)..], @"[-+]?(?:\d+\.\d+|\d+)(?:[eE][-+]?\d+)?d");
        Assert.Equal(3, values.Count);
        double Read(int index) => double.Parse(values[index].Value.TrimEnd('d'), CultureInfo.InvariantCulture);
        return new Vec3d(Read(0), Read(1), Read(2));
    }

    private static async Task ServerPositionNearAsync(VanillaInteractionAcceptanceHarness server, string selector, Vec3d position, double tolerance, CancellationToken ct)
    {
        Vec3d actual = await ServerPositionAsync(server, selector, ct);
        Assert.True(Near(actual, position, tolerance), $"Independent native position for {selector}: expected {position}, got {actual}.");
    }

    private static bool Near(Vec3d actual, Vec3d expected, double tolerance = .01) => Math.Abs(actual.X - expected.X) < tolerance && Math.Abs(actual.Y - expected.Y) < tolerance && Math.Abs(actual.Z - expected.Z) < tolerance;

    private static Aabb AabbForStandingPlayer(Vec3d position) => new(position.X - (double)(.6f / 2f), position.Y, position.Z - (double)(.6f / 2f), position.X + (double)(.6f / 2f), position.Y + (double)1.8f, position.Z + (double)(.6f / 2f));

    private static bool IsPositionFrame(CapturedFrame frame, int id) => frame.Flow == PacketFlow.Clientbound && frame.Packet switch
    {
        ClientboundMoveEntityPosPacket packet => packet.EntityId == id,
        ClientboundMoveEntityPosRotPacket packet => packet.EntityId == id,
        ClientboundEntityPositionSyncPacket packet => packet.EntityId == id,
        ClientboundTeleportEntityPacket packet => packet.EntityId == id,
        _ => false,
    };

    private static Vec3d RelativeDelta(object packet, int protocol)
    {
        IReadOnlyList<EntityMoveStep> steps;
        short x, y, z;
        if (packet is ClientboundMoveEntityPosPacket pos)
            (x, y, z, steps) = (pos.DeltaX, pos.DeltaY, pos.DeltaZ, pos.Steps);
        else
        {
            var both = Assert.IsType<ClientboundMoveEntityPosRotPacket>(packet);
            (x, y, z, steps) = (both.DeltaX, both.DeltaY, both.DeltaZ, both.Steps);
        }
        if (protocol == 777 && steps.Count != 0)
        {
            Assert.All(steps, step => Assert.True(step.Ticks >= 0));
            return new Vec3d(steps.Sum(step => (double)step.DeltaX) / 4096.0, steps.Sum(step => (double)step.DeltaY) / 4096.0, steps.Sum(step => (double)step.DeltaZ) / 4096.0);
        }
        Assert.Empty(steps);
        return new Vec3d(x / 4096.0, y / 4096.0, z / 4096.0);
    }

    private static Vec3d NativeQuantizedPosition(Vec3d position)
        => new(Math.Floor(position.X * 4096.0 + .5) / 4096.0, Math.Floor(position.Y * 4096.0 + .5) / 4096.0, Math.Floor(position.Z * 4096.0 + .5) / 4096.0);

    private static bool PositionFrameOnGround(CapturedFrame frame) => frame.Packet switch
    {
        ClientboundMoveEntityPosPacket packet => packet.OnGround,
        ClientboundMoveEntityPosRotPacket packet => packet.OnGround,
        ClientboundEntityPositionSyncPacket packet => packet.OnGround,
        ClientboundTeleportEntityPacket packet => packet.OnGround,
        _ => false,
    };

    private static void AssertNoPeerReplacement(CapturedFrame[] frames, int peerId, string scenario)
        => Assert.DoesNotContain(frames, frame => frame.Packet is ClientboundAddEntityPacket added && added.EntityId == peerId || frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(peerId));

    private static void AssertNoRetarget(CapturedFrame[] frames, int peerId, string scenario)
    {
        CapturedFrame[] retargets = frames.Where(frame => IsPositionFrame(frame, peerId) || frame.Packet is ClientboundMoveEntityRotPacket rot && rot.EntityId == peerId).ToArray();
        Assert.True(retargets.Length == 0, $"Harness precondition {scenario}: native retarget arrived during controlled phases: {string.Join(',', retargets.Select(frame => frame.Packet!.GetType().Name))}.");
    }

    private static bool StoredInterpolationActive(UmpkClient client, int entityId)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object motion = typeof(ClientState).GetProperty("EntityMotion", flags)!.GetValue(client.State)!;
        object table = motion.GetType().GetField("_remotePlayers", flags)!.GetValue(motion)!;
        object?[] arguments = [client.State.Entities.Get(entityId)!, null];
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments)!, "Harness precondition: the live peer must have its production motion owner.");
        object? position = arguments[1]!.GetType().GetProperty("Position", flags)!.GetValue(arguments[1]);
        Assert.NotNull(position);
        return (bool)position.GetType().GetProperty("HasActiveInterpolation", flags)!.GetValue(position)!;
    }

    private static object StoredGeometry(UmpkClient client, int entityId)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object geometry = typeof(ClientState).GetProperty("EntityGeometry", flags)!.GetValue(client.State)!;
        object table = geometry.GetType().GetField("_entities", flags)!.GetValue(geometry)!;
        object?[] arguments = [client.State.Entities.Get(entityId)!, null];
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments)!);
        return arguments[1]!;
    }

    private static bool StoredPositionKnown(UmpkClient client, int entityId)
    {
        object geometry = StoredGeometry(client, entityId);
        return (bool)geometry.GetType().GetProperty("PositionKnown")!.GetValue(geometry)!;
    }

    private static Aabb StoredBounds(UmpkClient client, int entityId)
    {
        object geometry = StoredGeometry(client, entityId);
        object bounds = geometry.GetType().GetProperty("Bounds")!.GetValue(geometry)!;
        return (Aabb)bounds.GetType().GetProperty("Box")!.GetValue(bounds)!;
    }

    private static int AssertMotionRoundTrips(JavaVersion version, CapturedFrame[] frames, RegistryAccess registries)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        int count = 0;
        foreach (CapturedFrame frame in frames)
        {
            if (frame.Packet is not (ClientboundDisguisedChatPacket or ClientboundMoveEntityPosPacket or ClientboundMoveEntityPosRotPacket or ClientboundMoveEntityRotPacket or ClientboundEntityPositionSyncPacket or ClientboundTeleportEntityPacket or ClientboundAddEntityPacket or ClientboundSetEntityDataPacket or ClientboundRemoveEntitiesPacket or ClientboundLevelChunkPacket or ClientboundBlockUpdatePacket or ClientboundInitializeBorderPacket or ClientboundSetBorderCenterPacket or ClientboundSetBorderSizePacket or ServerboundUseItemOnPacket or ServerboundSwingPacket or ServerboundPunchPacket))
                continue;
            Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, frame.Flow).TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            Assert.Equal(frame.Packet.GetType(), decoded.GetType());
            if (decoded is ClientboundMoveEntityPosPacket move)
            {
                var original = Assert.IsType<ClientboundMoveEntityPosPacket>(frame.Packet);
                Assert.Equal(original.EntityId, move.EntityId);
                Assert.Equal(original.DeltaX, move.DeltaX);
                Assert.Equal(original.DeltaY, move.DeltaY);
                Assert.Equal(original.DeltaZ, move.DeltaZ);
                Assert.Equal(original.Steps, move.Steps);
                Assert.Equal(original.OnGround, move.OnGround);
            }
            else if (decoded is ClientboundMoveEntityPosRotPacket both)
            {
                var original = Assert.IsType<ClientboundMoveEntityPosRotPacket>(frame.Packet);
                Assert.Equal(original.EntityId, both.EntityId);
                Assert.Equal(original.Steps, both.Steps);
                Assert.Equal(original with { Steps = [] }, both with { Steps = [] });
            }
            else if (decoded is ClientboundEntityPositionSyncPacket sync)
            {
                var original = Assert.IsType<ClientboundEntityPositionSyncPacket>(frame.Packet);
                Assert.Equal(original.EntityId, sync.EntityId);
                Assert.Equal(original.Values, sync.Values);
                Assert.Equal(original.OnGround, sync.OnGround);
                if (original.Path is EntityPositionPath.Stepped steps)
                    Assert.Equal(steps.Steps, Assert.IsType<EntityPositionPath.Stepped>(sync.Path).Steps);
                else
                    Assert.Equal(original.Path, sync.Path);
            }
            else if (decoded is ClientboundLevelChunkPacket chunk)
            {
                var original = Assert.IsType<ClientboundLevelChunkPacket>(frame.Packet);
                Assert.Equal(original.ChunkX, chunk.ChunkX);
                Assert.Equal(original.ChunkZ, chunk.ChunkZ);
                Assert.Equal(original.FullChunk, chunk.FullChunk);
                Assert.Equal(original.RawBody, chunk.RawBody);
                Assert.Equal(original.Column.SectionCount, chunk.Column.SectionCount);
            }
            else if (decoded is ClientboundAddEntityPacket actor)
            {
                var original = Assert.IsType<ClientboundAddEntityPacket>(frame.Packet);
                Assert.Equal(original with { ModernVelocityRaw = null }, actor with { ModernVelocityRaw = null });
                Assert.Equal(original.ModernVelocityRaw, actor.ModernVelocityRaw);
            }
            else if (decoded is ClientboundSetEntityDataPacket metadata)
            {
                var original = Assert.IsType<ClientboundSetEntityDataPacket>(frame.Packet);
                Assert.Equal(original.EntityId, metadata.EntityId);
                Assert.Equal(original.Metadata.Entries, metadata.Metadata.Entries);
                Assert.Equal(original.Metadata.RawTail, metadata.Metadata.RawTail);
            }
            else if (decoded is ClientboundRemoveEntitiesPacket removed)
                Assert.Equal(Assert.IsType<ClientboundRemoveEntitiesPacket>(frame.Packet).EntityIds, removed.EntityIds);
            else if (decoded is ServerboundSwingPacket swing)
                Assert.Equal(Assert.IsType<ServerboundSwingPacket>(frame.Packet) with { HasHand = true }, swing);
            else
                Assert.Equal(frame.Packet, decoded);
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(frame.Payload, buffer.WrittenSpan.ToArray());
            count++;
        }
        Assert.True(count >= 20, "The dedicated leg must replay actual motion, context and action frames.");
        return count;
    }

    private sealed class MotionTerrainReceipts
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<ChunkPos, CapturedFrame> _frames = [];
        private readonly HashSet<ChunkPos> _loaded = [];
        public CapturedFrame[] Frames { get { lock (_gate) return _frames.Values.ToArray(); } }
        public long PayloadBytes { get { lock (_gate) return _frames.Values.Sum(frame => (long)frame.Payload.Length); } }
        public bool Has(ChunkPos column) { lock (_gate) return _loaded.Contains(column); }

        public void Observe(PacketObservation observation)
        {
            if (observation.DecodedPacket is ClientboundForgetLevelChunkPacket forget)
            {
                lock (_gate)
                    _loaded.Remove(forget.Chunk);
                return;
            }
            if (observation.Phase != ProtocolPhase.Play || observation.Flow != PacketFlow.Clientbound || observation.DecodedPacket is not ClientboundLevelChunkPacket { FullChunk: true } chunk || chunk.ChunkX is not (0 or 6 or 12) || chunk.ChunkZ is not (-1 or 0))
                return;
            lock (_gate)
            {
                var column = new ChunkPos(chunk.ChunkX, chunk.ChunkZ);
                _frames[column] = new CapturedFrame(0, observation.Flow, observation.WireId, observation.CopyPayload()) { Packet = chunk };
                _loaded.Add(column);
            }
        }
    }

    private sealed class MotionTickSource : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        public async Task FireAsync(CancellationToken ct)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completed, ct);
            await completed.Task.WaitAsync(ct);
        }
        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            await foreach (TaskCompletionSource completed in _ticks.Reader.ReadAllAsync(ct))
            {
                yield return ++sequence;
                completed.TrySetResult();
            }
        }
    }
}
