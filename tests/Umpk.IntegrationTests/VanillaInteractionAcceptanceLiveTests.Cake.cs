using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> CakeUseReleases()
    {
        string[] names = (Environment.GetEnvironmentVariable("UMPK_CAKE_USE_RELEASES") ?? "1.8.9,1.11,1.15,1.20.4,1.20.5,26.3").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        foreach (string name in names)
        {
            Assert.Contains(name, new[] { "1.8.9", "1.11", "1.15", "1.20.4", "1.20.5", "26.3" });
            Assert.True(JavaVersions.TryGetByName(name, out JavaVersion version));
            yield return [name, version.Version.Protocol];
        }
    }

    [NightlyTheory]
    [MemberData(nameof(CakeUseReleases))]
    public async Task CakeUseHasNativeNutritionBitesStacksAndSingleActionPackets(string release, int protocol)
    {
        await VerifyCakeUsePrerequisites();
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        var writes = new List<CakePublicNutritionAtWrite>();
        var writeGate = new Lock();
        client.PacketFrameObserved += capture.Observe;
        client.PacketFrameObserved += observation =>
        {
            if (observation.Phase != ProtocolPhase.Play || observation.Flow != PacketFlow.Serverbound || observation.DecodedPacket is not (ServerboundLegacyBlockPlacePacket or ServerboundUseItemOnPacket))
                return;
            var self = client.State.Self;
            var row = new CakePublicNutritionAtWrite(self.Food, BitConverter.SingleToInt32Bits(self.Saturation), self.HealthObserved, self.Invulnerable, self.InstantBuild, self.GameMode, self.OnGround, self.Position);
            lock (writeGate)
                writes.Add(row);
        };
        CakeNativeScene[] scenes = NativeCakeScenes(protocol);
        var scenarios = new List<object>();
        bool passed = false;
        int previousSequence = -1;
        capture.Start();
        try
        {
            await server.ConsoleAsync("difficulty hard", ct);
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "initial native Cake terrain", ct);
            Assert.True(client.State.Features.Physics);
            Vec3d initial = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(initial.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(initial.Z) >> 4) * 16 + 8;
            Vec3d ownerPosition = new(x + .5, 100, z + .5);
            BlockPos target = new(x + 2, 100, z);
            await WaitAsync(() => capture.Frames.Any(frame => frame.Packet is ClientboundChangeDifficultyPacket { Difficulty: 3 }), "actual native Hard difficulty packet", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobSpawning false" : "gamerule minecraft:spawn_mobs false", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule naturalRegeneration false" : "gamerule minecraft:natural_health_regeneration false", ct);
            string naturalRegeneration = await server.CommandAsync(protocol < 774 ? "gamerule naturalRegeneration" : "gamerule minecraft:natural_health_regeneration", ct);
            Assert.EndsWith("false", naturalRegeneration, StringComparison.Ordinal);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {ownerPosition.X:R} 100 {ownerPosition.Z:R}"), ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && Near(c.State.Self.Position, ownerPosition), "grounded real physics before Cake use", ct);
            await WaitAsync(() => capture.Frames.Any(frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == target.X >> 4 && chunk.ChunkZ == target.Z >> 4 || frame.Packet is ClientboundMapChunkBulkPacket bulk && bulk.Columns.Any(column => column.Position == new ChunkPos(target.X >> 4, target.Z >> 4))), "actual native full query column including protocol47 bulk terrain", ct);
            Assert.True(await client.InvokeAsync(c => c.State.World.GetColumn(target) is not null && c.State.World.GetBlock(target).IsValid, ct));

            foreach (CakeNativeScene scene in scenes)
            {
                Vec3d cursor = new(scene.Bites == 6 ? .875 : .5, .5, .5);
                Assert.True(cursor.X > (1 + 2 * scene.Bites) / 16.0 && cursor.X < 15.0 / 16 && cursor.Y == .5 && cursor.Z > 1.0 / 16 && cursor.Z < 15.0 / 16, "The explicit Up hit must lie on the native Cake outline top.");
                int nutritionMark = capture.Mark();
                await server.ConsoleAsync($"gamemode creative {Username}", ct);
                await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Creative && c.State.Self.Invulnerable && c.State.Self.InstantBuild, "native creative preparation abilities", ct);
                await server.ConsoleAsync($"gamemode survival {Username}", ct);
                await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && !c.State.Self.Invulnerable && !c.State.Self.InstantBuild, "native survival preparation abilities", ct);
                await ClearEffects();
                await Effect("instant_health", 6, 1, 5);
                await Effect("hunger", 17, 120, 255);
                await WaitAsync(client, c => c.State.Self.HealthObserved && c.State.Self.Food is >= 1 and <= 18 && c.State.Self.Saturation == 0, "real hunger drains saturation and changes native food", ct);
                await ClearEffects();
                await WaitAsync(client, c => c.State.Self.ActiveEffects.Count == 0, "native hunger removal applied", ct);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                if (scene.FullFood)
                {
                    int refillMark = capture.Mark();
                    await Effect("saturation", 23, 1, 255);
                    await WaitAsync(() => capture.Since(refillMark).Any(frame => frame.Packet is ClientboundSetHealthPacket { Food: 20, Saturation: 20 }), "real refill food transition produces a fresh native health packet", ct);
                    await ClearEffects();
                    await WaitAsync(client, c => c.State.Self.ActiveEffects.Count == 0 && c.State.Self.Food == 20 && c.State.Self.Saturation == 20, "native full food and saturation applied", ct);
                }
                if (scene.Mode == GameMode.Creative)
                    await server.ConsoleAsync($"gamemode creative {Username}", ct);
                await WaitAsync(client, c => c.State.Self.GameMode == scene.Mode && c.State.Self.Invulnerable == (scene.Mode == GameMode.Creative) && c.State.Self.InstantBuild == (scene.Mode == GameMode.Creative) && c.State.Self.OnGround && Near(c.State.Self.Position, ownerPosition), "actual scene abilities and grounded physics", ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                if (scene.Item is not null)
                {
                    string slot = scene.Hand == Hand.Main ? "hotbar.0" : "weapon.offhand";
                    string command = protocol >= 755 ? $"item replace entity {Username} {slot} with minecraft:{scene.Item} 1" : $"replaceitem entity {Username} {(protocol < 393 ? "slot." : "")}{slot} minecraft:{scene.Item} 1";
                    await server.ConsoleAsync(command, ct);
                }
                await WaitAsync(client, c => CakeNativeHandsMatch(c, protocol, scene.Hand, scene.Item), "closed player inventory with measured default stack or empty hands", ct);
                string cake = protocol < 393 ? $"minecraft:cake {scene.Bites} replace" : $"minecraft:cake[bites={scene.Bites}]";
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} {cake}", ct);
                await WaitAsync(client, c => CakeBites(c.State.World.GetBlock(target), protocol) == scene.Bites, "native Cake setup bites", ct);
                await AssertNativeBites(scene.Bites);
                await AssertNativeStack(scene);
                await client.Actions.Movement.LookAtAsync(new(target.X + cursor.X, target.Y + cursor.Y, target.Z + cursor.Z), ct);
                var snapshot = await client.Snapshots.SelfAsync(ct);
                Assert.True(snapshot.EyePosition.Subtract(new Vec3d(target.X + cursor.X, target.Y + cursor.Y, target.Z + cursor.Z)).LengthSqr() < 4.5 * 4.5);
                var before = await client.InvokeAsync(c => (c.State.Self.Food, c.State.Self.Saturation, c.State.Self.Health, c.State.Self.HealthObserved, c.State.Self.Invulnerable, c.State.Self.InstantBuild, c.State.Self.GameMode, c.State.Self.OnGround, c.State.Self.Position, Target: c.State.World.GetBlock(target), Neighbor: c.State.World.GetBlock(target.Above())), ct);
                Assert.True(before.HealthObserved && before.OnGround && Near(before.Position, ownerPosition));
                Assert.Equal(20, before.Health);
                Assert.Equal(scene.Mode, before.GameMode);
                Assert.Equal(scene.Mode == GameMode.Creative, before.Invulnerable);
                Assert.Equal(scene.Mode == GameMode.Creative, before.InstantBuild);
                Assert.True(scene.FullFood ? before.Food == 20 : before.Food is >= 1 and <= 18);
                Assert.Equal(scene.FullFood ? 20 : 0, before.Saturation);
                ClientboundSetHealthPacket freshHealth = Assert.IsType<ClientboundSetHealthPacket>(capture.Since(nutritionMark).Last(frame => frame.Packet is ClientboundSetHealthPacket).Packet);
                Assert.Equal((before.Food, BitConverter.SingleToInt32Bits(before.Saturation)), (freshHealth.Food, BitConverter.SingleToInt32Bits(freshHealth.Saturation)));
                ClientboundPlayerAbilitiesPacket nativeAbilities = Assert.IsType<ClientboundPlayerAbilitiesPacket>(capture.Since(nutritionMark).Last(frame => frame.Packet is ClientboundPlayerAbilitiesPacket).Packet);
                Assert.Equal((before.Invulnerable, before.InstantBuild), ((nativeAbilities.Flags & 1) != 0, (nativeAbilities.Flags & 8) != 0));
                await AssertNativeNutrition(before.Food, before.Saturation);
                CakeNativeSavedPlayer nativeBefore = await ReadNativePlayer(scene.Name + "-before", scene);
                Assert.Equal((before.Food, BitConverter.SingleToInt32Bits(before.Saturation), BitConverter.SingleToInt32Bits(before.Health)), (nativeBefore.Food, nativeBefore.SaturationBits, nativeBefore.HealthBits));
                string positionTag = FormattableString.Invariant($"{{OnGround:1b,Pos:[{ownerPosition.X:R}d,100.0d,{ownerPosition.Z:R}d]}}");
                await AssertNativePlayer(positionTag);
                var expected = ExpectedNativeCakeNutrition(before.Food, before.Saturation, scene.Eats);
                int expectedBites = scene.Eats ? scene.Bites == 6 ? -1 : scene.Bites + 1 : scene.Bites;
                int expectedTarget = NativeCakeExpectedState(before.Target, expectedBites);
                int writeMark;
                lock (writeGate)
                    writeMark = writes.Count;
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(target, Direction.Up, cursor, scene.Hand, insideBlock: false, ct: ct);
                var processing = await NativeCakeProcessingWitnessAsync(capture, mark, protocol, target, Direction.Up, expectedTarget, before.Neighbor.StateId, previousSequence, ct);
                if (processing.Sequence is int sequence)
                    previousSequence = sequence;
                CakePublicNutritionAtWrite atWrite;
                lock (writeGate)
                    atWrite = Assert.Single(writes.Skip(writeMark));
                Assert.Equal((expected.Food, expected.SaturationBits), (atWrite.Food, atWrite.SaturationBits));
                Assert.Equal((before.Invulnerable, before.InstantBuild, before.GameMode), (atWrite.Invulnerable, atWrite.InstantBuild, atWrite.GameMode));
                Assert.True(atWrite.HealthObserved && atWrite.OnGround && Near(atWrite.Position, ownerPosition));
                await WaitAsync(client, c => CakeBites(c.State.World.GetBlock(target), protocol) == expectedBites && c.State.Self.Food == expected.Food && BitConverter.SingleToInt32Bits(c.State.Self.Saturation) == expected.SaturationBits, "authoritative native Cake bites/removal and nutrition", ct);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                await AssertNativeBites(expectedBites);
                await AssertNativeNutrition(expected.Food, BitConverter.Int32BitsToSingle(expected.SaturationBits));
                await AssertNativeStack(scene);
                CakeNativeSavedPlayer nativeAfter = await ReadNativePlayer(scene.Name + "-after", scene);
                Assert.Equal((expected.Food, expected.SaturationBits), (nativeAfter.Food, nativeAfter.SaturationBits));
                Assert.True(await client.InvokeAsync(c => CakeNativeHandsMatch(c, protocol, scene.Hand, scene.Item), ct));
                CapturedFrame[] segment = capture.Since(mark);
                CapturedFrame[] actions = segment.Where(IsUseOrAnimation).ToArray();
                Assert.Equal(1 + scene.PacketSwings, actions.Length);
                if (protocol == 47)
                {
                    var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(actions[0].Packet);
                    Assert.Equal((target, (int)Direction.Up, (byte)(cursor.X * 16), (byte)8, (byte)8), (use.Position, use.Face, use.CursorX, use.CursorY, use.CursorZ));
                    Assert.True(CakeDefaultStack(use.HeldItem, scene.Item));
                }
                else
                {
                    var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
                    Assert.Equal((target, (int)Direction.Up, (int)scene.Hand, (float)cursor.X, .5f, .5f), (use.Position, use.Face, use.Hand, use.CursorX, use.CursorY, use.CursorZ));
                    Assert.False(use.Inside || use.WorldBorderHit);
                }
                if (scene.PacketSwings == 1)
                    Assert.Equal((int)scene.Hand, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                Assert.DoesNotContain(segment, frame => frame.Packet is ServerboundPunchPacket or ServerboundPlayerActionPacket or ServerboundInteractPacket or ServerboundAttackPacket);
                Assert.True(segment[^1].Milliseconds - processing.Witness.Milliseconds >= 2000, "Retain actual native post-processing traffic under the unchanged capture cap.");
                Assert.False(capture.Overflowed);
                scenarios.Add(new { scene.Name, scene.Hand, scene.Mode, scene.FullFood, scene.Item, beforeBites = scene.Bites, afterBites = expectedBites, expectedLegalHitFromNativeShape = cursor, expectedFromNativeSource = new { result = scene.ExpectedResultFromNativeSource, swing = scene.ExpectedSwingFromNativeSource, scene.Eats }, actualBefore = new { before.Food, before.Saturation, saturationBits = BitConverter.SingleToInt32Bits(before.Saturation), before.Health, before.Invulnerable, before.InstantBuild, before.GameMode, before.OnGround, before.Position }, actualNutritionAtUseWrite = atWrite, actualAfter = await client.InvokeAsync(c => new { c.State.Self.Food, c.State.Self.Saturation, saturationBits = BitConverter.SingleToInt32Bits(c.State.Self.Saturation), targetState = c.State.World.GetBlock(target).StateId }, ct), freshHealth = new { freshHealth.Health, freshHealth.Food, freshHealth.Saturation }, nativeAbilities = new { nativeAbilities.Flags, nativeAbilities.FlyingSpeed, nativeAbilities.WalkingSpeed }, nativeBefore, nativeAfter, processingMilliseconds = processing.Witness.Milliseconds, processing.Sequence, packetOrder = actions.Select(frame => new { frame.Milliseconds, frame.Flow, frame.WireId, packet = frame.Packet!.GetType().Name, frame.Payload }), actualPacketSwings = actions.Count(frame => frame.Packet is ServerboundSwingPacket), localGraphicalSwingObserved = false, wireResultLimitation = "Consume/Pass/Unknown may share wire observations. The pinned111 typed session proof distinguishes native results; this fixture does not observe an internal result." });
                await server.WriteAsync("cake-use-scenarios.json", scenarios);
            }
            Assert.Equal(scenes.Length, scenarios.Count);
            Assert.Equal(scenes.Length, writes.Count);
            Assert.False(capture.Overflowed);
            Assert.True(capture.PayloadBytes <= 20 * 1024 * 1024);
            int actionReplay = capture.AssertRoundTrips(client.State.Registries!);
            int stateReplay = ReplayAdventureFrames(capture.Frames.Where(frame => protocol != 47 || frame.Packet is not ServerboundSwingPacket).ToArray(), [], version, client.State.Registries!);
            var dispositions = new List<object>();
            int causalReplay = ReplayAdventureCausalFrames(capture.Frames, version, client.State.Registries!, [], dispositions);
            int healthAndAbilitiesReplay = NativeCakeHealthAndAbilitiesRoundTrips(capture, version, client.State.Registries!);
            await server.WriteAsync("cake-use-causal-replay.json", new { actionReplay, stateReplay, causalReplay, healthAndAbilitiesReplay, dispositions, protocol47SwingReplay = "Original47 swings are byte-verified by Capture.AssertRoundTrips with its existing absent-hand normalization; the state replay receives every other original frame unchanged." });
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "native Cake peer EOF", ct);
            await server.WriteAsync("cake-use-acceptance.json", new { release, protocol, physics = true, scenarios, uses = scenes.Length, packetSwings = scenes.Sum(scene => scene.PacketSwings), capture.PayloadBytes, recordingCap = 20 * 1024 * 1024, nativeFoodAndSaturationAndBitesAndStackVerified = true, peerObservedDisconnect = true, originalMinecraftClientMethodOrInputExecuted = false, scope = "Closed-menu default empty/stick Cake nutrition only; no candle attachment, custom stack, graphical swing, fresh constructor parity, or negative nutrition input claim." });
            passed = true;

            async Task<CakeNativeSavedPlayer> ReadNativePlayer(string sceneName, CakeNativeScene scene)
            {
                var login = await client.InvokeAsync(c => (c.State.Self.Uuid, c.State.Self.Username), ct);
                Assert.NotEqual(Guid.Empty, login.Uuid);
                Assert.Equal(Username, login.Username);
                string path = protocol == 777
                    ? Path.Combine(server.DirectoryPath, "world", "players", "data", login.Uuid.ToString("D") + ".dat")
                    : Path.Combine(server.DirectoryPath, "world", "playerdata", login.Uuid.ToString("D") + ".dat");
                Assert.False(File.Exists(path + ".tmp"));
                DateTime? previousWriteUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
                int logStart = server.ServerLog.Length;
                await server.ConsoleAsync("save-all", ct);
                string feedback = server.ServerLog[logStart..];
                Assert.DoesNotContain("Saving failed:", feedback, StringComparison.Ordinal);
                Assert.DoesNotContain("Failed to save player data", feedback, StringComparison.Ordinal);
                var saved = new FileInfo(path);
                Assert.True(saved.Exists && (previousWriteUtc is null || saved.LastWriteTimeUtc > previousWriteUtc.Value), "Require a fresh owned native player save.");
                Assert.InRange(saved.Length, 1, 2 * 1024 * 1024);
                byte[] compressed = await File.ReadAllBytesAsync(path, ct);
                using var input = new MemoryStream(compressed, writable: false);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var expanded = new MemoryStream();
                byte[] buffer = new byte[8192];
                int read;
                while ((read = await gzip.ReadAsync(buffer, ct)) != 0)
                {
                    Assert.True(expanded.Length + read <= 2 * 1024 * 1024, "The native player NBT exceeds this bounded fixture.");
                    await expanded.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                NbtCompound data = Assert.IsType<NbtCompound>(NbtReader.Read(expanded.ToArray(), NbtWireFormat.JavaNamedRoot));
                byte[] uuid = login.Uuid.ToByteArray(bigEndian: true);
                if (data.ContainsKey("UUID"))
                {
                    int[] expectedUuid = Enumerable.Range(0, 4).Select(i => BinaryPrimitives.ReadInt32BigEndian(uuid.AsSpan(i * 4, 4))).ToArray();
                    Assert.Equal(expectedUuid, Assert.IsType<NbtIntArray>(data["UUID"]).Value);
                }
                else
                {
                    Assert.Equal(BinaryPrimitives.ReadInt64BigEndian(uuid.AsSpan(0, 8)), Assert.IsType<NbtLong>(data["UUIDMost"]).Value);
                    Assert.Equal(BinaryPrimitives.ReadInt64BigEndian(uuid.AsSpan(8, 8)), Assert.IsType<NbtLong>(data["UUIDLeast"]).Value);
                }
                NbtList positions = Assert.IsType<NbtList>(data["Pos"]);
                Assert.Equal(NbtTagType.Double, positions.ElementType);
                Assert.Equal(3, positions.Count);
                Vec3d nativePosition = new(Assert.IsType<NbtDouble>(positions[0]).Value, Assert.IsType<NbtDouble>(positions[1]).Value, Assert.IsType<NbtDouble>(positions[2]).Value);
                Assert.Equal(ownerPosition, nativePosition);
                Assert.Equal((sbyte)1, Assert.IsType<NbtByte>(data["OnGround"]).Value);
                int food = Assert.IsType<NbtInt>(data["foodLevel"]).Value;
                float saturation = Assert.IsType<NbtFloat>(data["foodSaturationLevel"]).Value;
                float health = Assert.IsType<NbtFloat>(data[protocol == 47 ? "HealF" : "Health"]).Value;
                if (protocol == 47)
                    Assert.Equal((short)Math.Ceiling(health), Assert.IsType<NbtShort>(data["Health"]).Value);
                float exhaustion = Assert.IsType<NbtFloat>(data["foodExhaustionLevel"]).Value;
                Assert.True(float.IsFinite(exhaustion) && exhaustion >= 0 && exhaustion <= 4, "Native exhaustion must have settled below its next food/saturation spend.");
                Assert.Equal(20, health);
                NbtCompound abilities = Assert.IsType<NbtCompound>(data["abilities"]);
                bool invulnerable = Assert.IsType<NbtByte>(abilities["invulnerable"]).Value != 0;
                bool instantBuild = Assert.IsType<NbtByte>(abilities["instabuild"]).Value != 0;
                Assert.Equal((scene.Mode == GameMode.Creative, scene.Mode == GameMode.Creative), (invulnerable, instantBuild));
                NbtList inventory = Assert.IsType<NbtList>(data["Inventory"]);
                Assert.Equal(scene.Item is null ? 0 : 1, inventory.Count);
                var afterRead = new FileInfo(path);
                Assert.Equal(saved.LastWriteTimeUtc, afterRead.LastWriteTimeUtc);
                Assert.Equal(saved.Length, afterRead.Length);
                Assert.False(File.Exists(path + ".tmp"));
                string snapshotPath = Path.Combine(server.DirectoryPath, sceneName + "-playerdata.dat");
                await File.WriteAllBytesAsync(snapshotPath, compressed, ct);
                var observed = new CakeNativeSavedPlayer(food, BitConverter.SingleToInt32Bits(saturation), BitConverter.SingleToInt32Bits(health), BitConverter.SingleToInt32Bits(exhaustion), invulnerable, instantBuild, inventory.Count, nativePosition, login.Uuid, snapshotPath, Convert.ToHexStringLower(SHA256.HashData(compressed)), compressed.Length, saved.LastWriteTimeUtc);
                await server.WriteAsync(sceneName + "-native-player.json", observed);
                return observed;
            }

            async Task ClearEffects() => await server.ConsoleAsync(protocol < 393 ? $"effect {Username} clear" : $"effect clear {Username}", ct);
            async Task Effect(string name, int legacyId, int seconds, int amplifier) => await server.ConsoleAsync(protocol < 393 ? $"effect {Username} {legacyId} {seconds} {amplifier} true" : $"effect give {Username} minecraft:{name} {seconds} {amplifier} true", ct);
            async Task AssertNativeNutrition(int food, float saturation) => Assert.Equal(NativeCakePredicateSuccess(protocol), await server.CommandAsync(NativeCakeNutritionPredicate(protocol, food, saturation), ct));
            async Task AssertNativePlayer(string tag) => Assert.Equal(NativeCakePredicateSuccess(protocol), await server.CommandAsync(protocol < 393 ? $"testfor @a[name={Username}] {tag}" : $"execute if entity @a[name={Username},nbt={tag}]", ct));
            async Task AssertNativeBites(int bites) => await AssertBlockAsync(server, protocol, target, bites < 0 ? "air" : protocol < 393 ? "cake" : $"cake[bites={bites}]", ct, protocol < 393 && bites >= 0 ? bites : null);
            async Task AssertNativeStack(CakeNativeScene scene)
            {
                if (scene.Item is null)
                    await AssertNativePlayer("{Inventory:[]}");
                else
                {
                    string command = protocol < 393 ? $"clear {Username} minecraft:stick -1 0" : $"clear {Username} minecraft:stick 0";
                    string response = await server.CommandAsync(command, ct);
                    Assert.Contains(response, protocol < 393 ? new[] { $"{Username} has 1 items that match the criteria" } : new[] { $"Found 1 matching items on player {Username}", $"Found 1 matching item(s) on player {Username}" });
                }
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("cake-use-capture.json", capture.Frames);
            await server.WriteAsync("cake-use-result.json", new { passed, release, protocol, completedScenes = scenarios.Count, capture.PayloadBytes, capture.Overflowed, sourceAndFramedRuntimeImplemented = true, liveUseOnExercised = scenarios.Count != 0, originalMinecraftClientMethodOrInputExecuted = false });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private sealed record CakeNativeSavedPlayer(int Food, int SaturationBits, int HealthBits, int ExhaustionBits, bool Invulnerable, bool InstantBuild, int InventoryEntries, Vec3d Position, Guid Uuid, string SnapshotPath, string Sha256, int CompressedBytes, DateTime SavedWriteUtc);

    private sealed record CakePublicNutritionAtWrite(int Food, int SaturationBits, bool HealthObserved, bool Invulnerable, bool InstantBuild, GameMode GameMode, bool OnGround, Vec3d Position);

    private static bool CakeNativeHandsMatch(UmpkClient client, int protocol, Hand hand, string? item)
    {
        var inventory = client.State.Inventory;
        var slots = inventory.PlayerSlots;
        if (inventory.HasOpenContainer)
            return false;
        if (protocol == 47)
            return hand == Hand.Main && slots.Count == 45 && CakeDefaultStack(slots[36], item);
        if (protocol < 107 || slots.Count != 46 || hand is not Hand.Main and not Hand.Off)
            return false;
        return CakeDefaultStack(slots[hand == Hand.Main ? 36 : 45], item) && slots[hand == Hand.Main ? 45 : 36].IsEmpty;
    }

    private static bool CakeDefaultStack(ItemStack stack, string? item) => item is null ? stack.IsEmpty : !stack.IsEmpty && stack.Item.Id == Identifier.Minecraft(item) && stack.Count == 1 && stack.Components.HasNoPatch;

    private static int CakeBites(BlockState state, int protocol) => !state.IsValid ? -2 : state.IsAir ? -1 : state.Block.Id != Identifier.Minecraft("cake") ? -2 : protocol < 393 ? state.LegacyMeta : state.TryGetProperty("bites", out string value) ? int.Parse(value, CultureInfo.InvariantCulture) : -2;

    private static int NativeCakeExpectedState(BlockState before, int bites)
    {
        if (bites < 0)
            return before.Source.GetDefaultStateId(before.Source.Blocks[Identifier.Minecraft("air")].NetworkId);
        if (before.Source.IsLegacy)
            return before.Source.EncodeLegacy(before.LegacyId, bites);
        var definition = before.Block.Value;
        return Assert.Single(Enumerable.Range(definition.MinStateId, definition.MaxStateId - definition.MinStateId + 1), id => before.Source.TryGetPropertyValue(id, "bites", out string value) && value == bites.ToString(CultureInfo.InvariantCulture));
    }

    private static string NativeCakePredicateSuccess(int protocol) => protocol < 393 ? "Found " + Username : protocol >= 773 ? "Test passed. Count: 1" : "Test passed, count: 1";

    private static async Task VerifyCakeUsePrerequisites()
    {
        string path = Environment.GetEnvironmentVariable("UMPK_CAKE_USE_PREREQUISITE_RECEIPT") ?? throw new InvalidOperationException("Supply the reviewed Cake producer and fixture receipt before native Cake acceptance.");
        byte[] bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_CAKE_USE_PREREQUISITE_SHA256"), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement receipt = document.RootElement;
        Assert.Equal("ready for bounded native Cake nutrition acceptance", receipt.GetProperty("status").GetString());
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_INTERACTION_PRODUCER"), receipt.GetProperty("producer_identity").GetString());
        Assert.Equal(111, receipt.GetProperty("executed_passing_cake_typed_session_cases").GetInt32());
        Assert.Equal("current-full35611-same-full-Cake111-v1", receipt.GetProperty("producer_proof_schema").GetString());
        Assert.Equal(35611, receipt.GetProperty("expected_final_client_cases").GetInt32());
        Assert.True(receipt.GetProperty("aot_bytes").GetInt32() < 25_165_824);
        await AssertTrx("final_client_full_trx", 35611);
        await AssertTrx("final_aot_smoke_trx", 1);
        var actual = Directory.GetFiles(AppContext.BaseDirectory, "Umpk.*").Where(file => Path.GetExtension(file) is ".dll" or ".pdb").ToDictionary(file => Path.GetFileName(file), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        JsonElement binaries = receipt.GetProperty("integration_binaries");
        Assert.Equal(actual.Keys.Order(StringComparer.Ordinal), binaries.EnumerateObject().Select(binary => binary.Name).Order(StringComparer.Ordinal));
        foreach (var binary in actual)
            Assert.Equal(binary.Value, binaries.GetProperty(binary.Key!).GetString());
        foreach (JsonProperty source in receipt.GetProperty("harness_sources").EnumerateObject())
            Assert.Equal(source.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(FixturePaths.RepoRoot(), source.Name)))));

        async Task AssertTrx(string key, int expected)
        {
            JsonElement row = receipt.GetProperty(key);
            byte[] trx = await File.ReadAllBytesAsync(row.GetProperty("path").GetString()!);
            Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(trx)));
            using var stream = new MemoryStream(trx, writable: false);
            XDocument result = XDocument.Load(stream);
            XNamespace ns = result.Root!.Name.Namespace;
            XElement counters = result.Descendants(ns + "Counters").Single();
            Assert.Equal(expected, (int)counters.Attribute("total")!);
            Assert.Equal(expected, (int)counters.Attribute("executed")!);
            Assert.Equal(expected, (int)counters.Attribute("passed")!);
            Assert.Equal(0, (int)counters.Attribute("failed")!);
            XElement[] rows = result.Descendants(ns + "UnitTestResult").ToArray();
            Assert.Equal(expected, rows.Length);
            Assert.All(rows, row => Assert.Equal("Passed", (string?)row.Attribute("outcome")));
            if (key == "final_client_full_trx")
            {
                const string prefix = "Umpk.Client.Tests.CakeActivationSessionRuntimeTests.";
                JsonElement subset = receipt.GetProperty("cake_typed_session_same_full_subset");
                Assert.False(subset.GetProperty("separate_execution").GetBoolean());
                Assert.Equal(111, subset.GetProperty("executed_passing_cases").GetInt32());
                JsonElement counterPin = subset.GetProperty("counter");
                byte[] counterBytes = await File.ReadAllBytesAsync(counterPin.GetProperty("path").GetString()!);
                Assert.Equal(counterPin.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(counterBytes)));
                using JsonDocument counterDocument = JsonDocument.Parse(counterBytes);
                var expectedCounter = counterDocument.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt32(), StringComparer.Ordinal);
                var actualCounter = rows.Select(row => (string)row.Attribute("testName")!).Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).GroupBy(name => name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
                Assert.Equal(111, expectedCounter.Values.Sum());
                Assert.Equal(expectedCounter.Keys.Order(StringComparer.Ordinal), actualCounter.Keys.Order(StringComparer.Ordinal));
                foreach (var entry in expectedCounter)
                    Assert.Equal(entry.Value, actualCounter[entry.Key]);
            }
        }
    }
}
