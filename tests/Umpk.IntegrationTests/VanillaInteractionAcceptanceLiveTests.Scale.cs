using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> ScaleReleases()
    {
        string requested = Environment.GetEnvironmentVariable("UMPK_SCALE_RELEASES") ?? "1.20.5,1.21.5,26.3";
        foreach (string release in requested.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.Contains(release, new[] { "1.20.5", "1.21.5", "26.3" });
            Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
            yield return [release, version.Version.Protocol];
        }
    }

    [NightlyTheory]
    [MemberData(nameof(ScaleReleases))]
    public async Task ScalePacketsReachSelfTargetingAndPeerPlacement(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        client.PacketFrameObserved += capture.Observe;
        bool passed = false;
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "scale terrain", ct);
            Assert.True(client.State.Features.Physics);
            Vec3d initial = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = (int)Math.Floor(initial.X), z = (int)Math.Floor(initial.Z);
            var ownTarget = new BlockPos(x + 1, 100, z);
            var peerTarget = new BlockPos(x + 2, 100, z);
            Identifier rawScale = client.State.Registries!.Attributes.Single(entry => AttributeIds.Canonical(entry.Id) == Identifier.Minecraft("scale")).Id;
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobSpawning false" : "gamerule minecraft:spawn_mobs false", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobLoot false" : "gamerule minecraft:mob_drops false", ct);
            await server.ConsoleAsync($"fill {x - 5} 99 {z - 5} {x + 5} 99 {z + 5} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 5} 100 {z - 5} {x + 5} 110 {z + 5} minecraft:air", ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {x + .5:R} 100 {z + .5:R} 0 0"), ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await server.ConsoleAsync($"clear {Username}", ct);
            await server.ConsoleAsync($"give {Username} minecraft:stone 32", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && c.State.Self.GameMode == GameMode.Survival && Math.Abs(c.State.Self.Position.Y - 100) < .01 && c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("stone") && c.State.Inventory.GetSlot(36).Count == 32, "scale platform, physics and stack", ct);
            await AssertPosition(Username, new Vec3d(x + .5, 100, z + .5));
            Assert.Equal((0F, 0F), await client.InvokeAsync(c => (c.State.Self.Yaw, c.State.Self.Pitch), ct));
            capture.Start();
            int? ownedCowId = null;
            Guid? ownedCowUuid = null;
            int actorProof = 0;
            await RemoveAmbientActors();
            await AssertExactActors();
            await SelfScale(2);
            var wall = new BlockPos(x, 103, z + 2);
            await server.ConsoleAsync($"setblock {wall.X} {wall.Y} {wall.Z} minecraft:stone", ct);
            await WaitAsync(client, c => c.State.World.GetBlock(wall).Block.Id == Identifier.Minecraft("stone"), "scaled-eye wall", ct);
            await AssertBlockAsync(server, protocol, wall, "stone", ct);
            var snapshot = await client.Snapshots.SelfAsync(ct);
            var hit = await client.Snapshots.RaycastViewAsync(4.5, ct: ct);
            Assert.NotNull(hit);
            Assert.Equal(wall, hit.Value.BlockPos);
            Assert.Equal(snapshot.EyePosition.Y, hit.Value.Point.Y);
            await server.ConsoleAsync($"setblock {wall.X} {wall.Y} {wall.Z} minecraft:air", ct);
            await WaitAsync(client, c => c.State.World.GetBlock(wall).IsAir, "removed eye wall", ct);
            await Place(ownTarget, success: false, count: 32);
            await SelfScale(.5);
            await Place(ownTarget, success: true, count: 31);
            await server.ConsoleAsync($"setblock {ownTarget.X} {ownTarget.Y} {ownTarget.Z} minecraft:air", ct);
            await WaitAsync(client, c => c.State.World.GetBlock(ownTarget).IsAir, "cleared self-placement control", ct);
            await AssertBlockAsync(server, protocol, ownTarget, "air", ct);

            Guid cowUuid = Guid.NewGuid();
            int cowMark = capture.Mark();
            await server.ConsoleAsync(FormattableString.Invariant($"summon minecraft:cow {x + 1.75:R} 100 {z + .5:R} {{UUID:{ScaleNativeUuid(cowUuid)},Tags:[\"umpk_scale_test\"],Age:0,NoAI:1b,NoGravity:1b,Silent:1b,Invulnerable:1b}}"), ct);
            await WaitAsync(client, c => c.State.Entities.All.Any(entity => entity.Uuid == cowUuid), "exact owned static cow spawn", ct);
            var cow = await client.InvokeAsync(c => Assert.Single(c.State.Entities.All), ct);
            Assert.Equal(cowUuid, cow.Uuid);
            Assert.Equal(Identifier.Minecraft("cow"), cow.Type.Id);
            ownedCowId = cow.Id;
            ownedCowUuid = cowUuid;
            var cowSpawn = Assert.Single(capture.Since(cowMark).Select(frame => frame.Packet).OfType<ClientboundAddEntityPacket>(), packet => packet.Uuid == cowUuid);
            Assert.Equal(cow.Id, cowSpawn.EntityId);
            Assert.Equal(new Vec3d(x + 1.75, 100, z + .5), new Vec3d(cowSpawn.X, cowSpawn.Y, cowSpawn.Z));
            await AssertNativeUuid(cowUuid);
            await AssertExactActors();
            await PeerScale(2);
            await Place(peerTarget, success: false, count: 31);
            await PeerScale(.5);
            await Place(peerTarget, success: true, count: 30);
            int replayed = ReplayScaleFrames(capture, version, client.State.Registries!);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "scale server-observed disconnect", ct);
            await server.WriteAsync("scale-acceptance.json", new { release, protocol, clientProfile = version.Version.Name, physics = true, selfEyes = "native float 2 and .5 scale after real packets", selfObstruction = "scaled body rejects placement then shrink permits it", peerObstruction = "NoAI/NoGravity cow rejects at2 and permits at.5", independentServerBlocksAndCounts = true, replayed, peerObservedDisconnect = true, nativeLocalMovementTickWitness = false, ambientCleanup = "same-column native living probe, alive staging before kill, remove frames and ordered disguised-chat marker", exactCowUuid = cowUuid, exactCowId = ownedCowId, peerCenterOffset = 1.75 });
            passed = true;

            async Task RemoveAmbientActors()
            {
                int mark = capture.Mark();
                var initialActors = await client.InvokeAsync(c => c.State.Entities.All.Select(entity => new { entity.Id, entity.Uuid, Type = entity.Type.Id.ToString(), entity.Position }).ToArray(), ct);
                int stagingZ = z + ((z & 15) < 8 ? 4 : -4);
                var staging = new Vec3d(x + .5, 100, stagingZ + .5);
                Assert.Equal(x >> 4, (int)Math.Floor(staging.X) >> 4);
                Assert.Equal(z >> 4, (int)Math.Floor(staging.Z) >> 4);
                await WaitAsync(client, c => c.State.World.GetColumn(BlockPos.Containing(staging)) is not null && c.State.World.GetBlock(BlockPos.Containing(staging).Below()).Block.Id == Identifier.Minecraft("stone"), "same-column native staging platform", ct);
                await AssertBlockAsync(server, protocol, BlockPos.Containing(staging).Below(), "stone", ct);
                Guid probeUuid = Guid.NewGuid();
                await server.ConsoleAsync(FormattableString.Invariant($"summon minecraft:cow {staging.X:R} {staging.Y:R} {staging.Z:R} {{UUID:{ScaleNativeUuid(probeUuid)},Tags:[\"umpk_scale_staging\"],Age:0,NoAI:1b,NoGravity:1b,Silent:1b}}"), ct);
                await WaitAsync(client, c => c.State.Entities.All.Any(entity => entity.Uuid == probeUuid), "native living staging probe spawn", ct);
                int probeId = await client.InvokeAsync(c => c.State.Entities.All.Single(entity => entity.Uuid == probeUuid).Id, ct);
                var probeSpawn = Assert.Single(capture.Since(mark).Select(frame => frame.Packet).OfType<ClientboundAddEntityPacket>(), packet => packet.Uuid == probeUuid);
                Assert.Equal(probeId, probeSpawn.EntityId);
                Assert.Equal(staging, new Vec3d(probeSpawn.X, probeSpawn.Y, probeSpawn.Z));
                await AssertNativeUuid(probeUuid);
                await AssertPosition(probeUuid.ToString(), staging);
                await server.ConsoleAsync($"kill {probeUuid}", ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(probeId)), "native living death completed in staging column", ct);
                await WaitAsync(client, c => c.State.Entities.Get(probeId) is null, "staging probe actual removal applied", ct);
                CapturedFrame[] probeFrames = capture.Since(mark);
                int deathIndex = Array.FindIndex(probeFrames, frame => frame.Packet is ClientboundEntityEventPacket death && death.EntityId == probeId && death.EventId == 3);
                int finishedDeathIndex = Array.FindIndex(probeFrames, frame => frame.Packet is ClientboundEntityEventPacket death && death.EntityId == probeId && death.EventId == 60);
                int probeRemoveIndex = Array.FindIndex(probeFrames, frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(probeId));
                Assert.True(deathIndex >= 0 && finishedDeathIndex > deathIndex && probeRemoveIndex > finishedDeathIndex, "Native staging probe must complete ordered death3, death-tick60 and removal before ambient cleanup.");
                var actorsBeforeStaging = await client.InvokeAsync(c => c.State.Entities.All.Select(entity => new { entity.Id, entity.Uuid, Type = entity.Type.Id.ToString(), entity.Position }).ToArray(), ct);
                if (actorsBeforeStaging.Length != 0)
                    await server.ConsoleAsync(FormattableString.Invariant($"tp @e[type=!minecraft:player] {staging.X:R} {staging.Y:R} {staging.Z:R}"), ct);
                await server.ConsoleAsync("kill @e[type=!minecraft:player]", ct);
                await WaitAsync(() => ObservedIds().All(id => RemovedIds().Contains(id)), "real remove frames for every observed ambient actor", ct);
                await WaitAsync(client, c => !c.State.Entities.All.Any(), "zero remote entities after native ambient cleanup", ct);
                string marker = "scale-native-cleanup-" + Guid.NewGuid().ToString("N");
                await server.ConsoleAsync("say " + marker, ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundDisguisedChatPacket chat && chat.Message.ToPlainText().Contains(marker, StringComparison.Ordinal)), "ordered native disguised-chat cleanup marker", ct);
                CapturedFrame[] frames = capture.Since(mark);
                int markerIndex = Array.FindIndex(frames, frame => frame.Packet is ClientboundDisguisedChatPacket chat && chat.Message.ToPlainText().Contains(marker, StringComparison.Ordinal));
                Assert.True(markerIndex >= 0);
                int[] requiredIds = ObservedIds();
                foreach (int id in requiredIds)
                    Assert.Contains(frames.Take(markerIndex), frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(id));
                Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.Select(entity => entity.Id).ToArray(), ct));
                Assert.False(capture.Overflowed);
                await server.WriteAsync("scale-native-cleanup.json", new { initialActors, actorsBeforeStaging, staging, probeUuid, probeId, deathIndex, finishedDeathIndex, probeRemoveIndex, requiredIds, removedIds = RemovedIds(), marker, markerIndex, remainingIds = Array.Empty<int>(), nativeLivingStagingTickProvenByRemoval = true, measuredActionsStartAfterCleanup = true });

                int[] ObservedIds() => initialActors.Select(entity => entity.Id).Concat(actorsBeforeStaging.Select(entity => entity.Id)).Concat(capture.Since(mark).Select(frame => frame.Packet).OfType<ClientboundAddEntityPacket>().Select(packet => packet.EntityId)).Distinct().Order().ToArray();
                int[] RemovedIds() => capture.Since(mark).Select(frame => frame.Packet).OfType<ClientboundRemoveEntitiesPacket>().SelectMany(packet => packet.EntityIds).Distinct().Order().ToArray();
            }
            async Task AssertExactActors()
            {
                var actors = await client.InvokeAsync(c => c.State.Entities.All.Select(entity => new { entity.Id, entity.Uuid, Type = entity.Type.Id.ToString(), entity.Position, entity.Velocity }).ToArray(), ct);
                await server.WriteAsync($"scale-actors-{actorProof++:D2}.json", actors);
                if (ownedCowId is null)
                    Assert.Empty(actors);
                else
                {
                    var actor = Assert.Single(actors);
                    Assert.Equal(ownedCowId.Value, actor.Id);
                    Assert.Equal(ownedCowUuid!.Value, actor.Uuid);
                    Assert.Equal("minecraft:cow", actor.Type);
                }
            }
            async Task AssertNativeUuid(Guid uuid)
            {
                string response = await server.CommandAsync($"data get entity {uuid} UUID", ct);
                string array = response[(response.LastIndexOf('['))..];
                int[] actual = Regex.Matches(array, @"-?\d+").Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
                Assert.Equal(ScaleUuidWords(uuid), actual);
            }

            async Task SelfScale(double scale)
            {
                await AssertExactActors();
                await server.ConsoleAsync(FormattableString.Invariant($"attribute {Username} {rawScale} base set {scale:R}"), ct);
                await AssertScale(Username, scale);
                double eye = (double)(1.62F * (float)scale);
                await WaitAsync(async () => (await client.Snapshots.SelfAsync(ct)).EyeHeight == eye, "framed self scale and cached eye phase", ct);
                Assert.Equal(scale, await client.InvokeAsync(c => c.State.Self.Attributes.Value(Identifier.Minecraft("scale"), double.NaN), ct));
                await AssertPosition(Username, new Vec3d(x + .5, 100, z + .5));
            }
            async Task PeerScale(double scale)
            {
                await AssertExactActors();
                string selector = ownedCowUuid!.Value.ToString();
                await server.ConsoleAsync(FormattableString.Invariant($"attribute {selector} {rawScale} base set {scale:R}"), ct);
                await AssertScale(selector, scale);
                await WaitAsync(client, c => c.State.Entities.Get(ownedCowId!.Value)?.Attributes.GetValueOrDefault(Identifier.Minecraft("scale"), double.NaN) == scale, "real cow scale attribute", ct);
                long before = await client.InvokeAsync(c => c.State.SessionTick, ct);
                await WaitAsync(client, c => c.State.SessionTick > before, "cow living scale phase", ct);
                await AssertPosition(selector, new Vec3d(x + 1.75, 100, z + .5));
                await AssertExactActors();
            }
            async Task AssertScale(string selector, double scale)
            {
                string response = await server.CommandAsync($"attribute {selector} {rawScale} base get", ct);
                Assert.Equal(scale, double.Parse(response[(response.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture));
            }
            async Task AssertPosition(string selector, Vec3d expected)
            {
                string response = await server.CommandAsync($"data get entity {selector} Pos", ct);
                double[] values = Regex.Matches(response, @"(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)[dD]").Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
                Assert.Equal(3, values.Length);
                Assert.True(Math.Abs(expected.X - values[0]) < .01 && Math.Abs(expected.Y - values[1]) < .01 && Math.Abs(expected.Z - values[2]) < .01, response);
            }
            async Task Place(BlockPos target, bool success, int count)
            {
                await AssertExactActors();
                await AssertPosition(Username, new Vec3d(x + .5, 100, z + .5));
                int beforeCount = count + (success ? 1 : 0);
                await WaitAsync(client, c => c.State.Self.OnGround && c.State.Self.HeldSlot == 0 && c.State.World.GetBlock(target.Below()).Block.Id == Identifier.Minecraft("stone") && c.State.World.GetBlock(target).IsAir && c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("stone") && c.State.Inventory.GetSlot(36).Count == beforeCount, "known placement support, destination, grounded owner and held stack", ct);
                await AssertBlockAsync(server, protocol, target.Below(), "stone", ct);
                await AssertBlockAsync(server, protocol, target, "air", ct);
                await AssertCountAsync(server, protocol, beforeCount, ct);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(target.Below(), Direction.Up, new Vec3d(.5, 1, .5), ct: ct);
                if (success)
                    await WaitAsync(client, c => c.State.World.GetBlock(target).Block.Id == Identifier.Minecraft("stone") && c.State.Inventory.GetSlot(36).Count == count, "scaled placement result and stack", ct);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                CapturedFrame[] actions = capture.Since(mark).Where(IsUseOrAnimation).ToArray();
                Assert.Equal(success && protocol != 777 ? 2 : 1, actions.Length);
                AssertUsePacket(actions[0], protocol, target.Below());
                if (actions.Length == 2)
                    Assert.IsType<ServerboundSwingPacket>(actions[1].Packet);
                await AssertBlockAsync(server, protocol, target, success ? "stone" : "air", ct);
                await AssertCountAsync(server, protocol, count, ct);
                await AssertExactActors();
                await AssertPosition(Username, new Vec3d(x + .5, 100, z + .5));
            }
        }
        finally
        {
            capture.Stop();
            string source = Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.IntegrationTests", "VanillaInteractionAcceptanceLiveTests.Scale.cs");
            await server.WriteAsync("scale-source.json", new { path = source, sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source, CancellationToken.None))) });
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "scale self targeting and peer placement", capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static int[] ScaleUuidWords(Guid uuid)
    {
        string hex = uuid.ToString("N");
        return Enumerable.Range(0, 4).Select(index => unchecked((int)uint.Parse(hex.AsSpan(index * 8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture))).ToArray();
    }

    private static string ScaleNativeUuid(Guid uuid) => "[I;" + string.Join(',', ScaleUuidWords(uuid)) + "]";

    private static int ReplayScaleFrames(Capture capture, JavaVersion version, Umpk.Game.Registries.RegistryAccess registries)
    {
        Assert.False(capture.Overflowed);
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        int replayed = 0;
        foreach (CapturedFrame frame in capture.Frames)
        {
            if (frame.Packet is not (ClientboundAddEntityPacket or ClientboundUpdateAttributesPacket or ClientboundBlockUpdatePacket or ClientboundEntityEventPacket or ClientboundRemoveEntitiesPacket or ClientboundDisguisedChatPacket or ServerboundUseItemOnPacket or ServerboundSwingPacket))
                continue;
            Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, frame.Flow).TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            if (frame.Packet is ClientboundUpdateAttributesPacket expectedAttributes)
            {
                var actual = Assert.IsType<ClientboundUpdateAttributesPacket>(decoded);
                Assert.Equal(expectedAttributes.EntityId, actual.EntityId);
                Assert.Equal(expectedAttributes.Attributes.Count, actual.Attributes.Count);
                for (int i = 0; i < actual.Attributes.Count; i++)
                {
                    AttributeSnapshot expected = expectedAttributes.Attributes[i], observed = actual.Attributes[i];
                    Assert.Equal(expected.ModernId, observed.ModernId);
                    Assert.Equal(expected.BaseValue, observed.BaseValue);
                    Assert.Equal(expected.Modifiers.ToArray(), observed.Modifiers.ToArray());
                }
            }
            else if (frame.Packet is ClientboundAddEntityPacket expectedSpawn)
            {
                var actual = Assert.IsType<ClientboundAddEntityPacket>(decoded);
                Assert.Equal(expectedSpawn.ModernVelocityRaw ?? [], actual.ModernVelocityRaw ?? []);
                Assert.Equal(expectedSpawn with { ModernVelocityRaw = actual.ModernVelocityRaw }, actual);
            }
            else if (frame.Packet is ClientboundRemoveEntitiesPacket expectedRemoval)
                Assert.Equal(expectedRemoval.EntityIds.ToArray(), Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds.ToArray());
            else if (frame.Packet is ClientboundDisguisedChatPacket expectedChat)
            {
                var actual = Assert.IsType<ClientboundDisguisedChatPacket>(decoded);
                Assert.Equal(expectedChat.Message.ToPlainText(), actual.Message.ToPlainText());
                Assert.Equal(expectedChat.ChatTypeId, actual.ChatTypeId);
                Assert.Equal(expectedChat.SenderName.ToPlainText(), actual.SenderName.ToPlainText());
                Assert.Equal(expectedChat.TargetName?.ToPlainText(), actual.TargetName?.ToPlainText());
            }
            else
                Assert.Equal(frame.Packet, decoded);
            var encoded = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(encoded);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(frame.Payload, encoded.WrittenSpan.ToArray());
            replayed++;
        }
        Assert.True(replayed >= 12);
        return replayed;
    }
}
