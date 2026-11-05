using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Data.Java;
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
    public static IEnumerable<object[]> EndCrystalUseReleases()
    {
        string[] names = (Environment.GetEnvironmentVariable("UMPK_CRYSTAL_USE_RELEASES") ?? "1.9,1.12.2,1.13,26.2,26.3").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        foreach (string name in names)
        {
            Assert.Contains(name, new[] { "1.9", "1.12.2", "1.13", "26.2", "26.3" });
            Assert.True(JavaVersions.TryGetByName(name, out JavaVersion version));
            yield return [name, version.Version.Protocol];
        }
    }

    [NightlyTheory]
    [MemberData(nameof(EndCrystalUseReleases))]
    public async Task EndCrystalItemUseHasNativeEntitiesStacksAndSingleActionPackets(string release, int protocol)
    {
        await VerifyCrystalUsePrerequisites();
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        client.PacketFrameObserved += capture.Observe;
        var scenarios = new List<object>();
        bool passed = false;
        int previousSequence = -1;
        string nativeEntity = protocol == 107 ? "EnderCrystal" : protocol == 340 ? "minecraft:ender_crystal" : "minecraft:end_crystal";
        Identifier observedEntity = Identifier.Minecraft(protocol < 393 ? "ender_crystal" : "end_crystal");
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "initial native crystal-use terrain", ct);
            Assert.True(client.State.Features.Physics);
            Vec3d initial = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(initial.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(initial.Z) >> 4) * 16 + 8;
            Vec3d self = new(x + .5, 100, z + .5);
            BlockPos target = new(x + 2, 99, z);
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobSpawning false" : "gamerule minecraft:spawn_mobs false", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobLoot false" : "gamerule minecraft:mob_drops false", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} 100 {self.Z:R}"), ct);
            await server.ConsoleAsync("scoreboard objectives add crystalUse dummy", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && Near(c.State.Self.Position, self), "grounded real physics before crystal use", ct);
            await RequireColumn(target);

            await Prepare("obsidian", Hand.Main);
            await Use("obsidian-main", target, Hand.Main, "Success", success: true, countBefore: 32, crystalsBefore: 0);
            await Use("existing-crystal-body", target, Hand.Main, protocol >= 769 ? "Fail" : "Unknown", success: false, countBefore: 31, crystalsBefore: 1);
            await Prepare("bedrock", Hand.Off);
            await Use("bedrock-offhand", target, Hand.Off, "Success", success: true, countBefore: 32, crystalsBefore: 0);
            await Prepare("stone", Hand.Main);
            await Use("wrong-base", target, Hand.Main, "Fail", success: false, countBefore: 32, crystalsBefore: 0);
            await Prepare("obsidian", Hand.Main);
            await Set(target.Above(), "stone");
            await Use("first-cell-solid", target, Hand.Main, "Fail", success: false, countBefore: 32, crystalsBefore: 0);
            await Prepare("obsidian", Hand.Main);
            await Set(target.Above().Above(), "stone");
            await Use("second-cell-solid", target, Hand.Main, protocol < 393 ? "Fail" : "Success", success: protocol >= 393, countBefore: 32, crystalsBefore: 0);
            await Prepare("obsidian", Hand.Main);
            await server.ConsoleAsync($"gamemode adventure {Username}", ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Adventure && !c.State.Self.InstantBuild, "native Adventure default crystal stack", ct);
            await Use("default-adventure", target, Hand.Main, protocol < 393 ? "Fail" : "Pass", success: false, countBefore: 32, crystalsBefore: 0);
            await Prepare("obsidian", Hand.Main);
            BlockPos selfFloor = BlockPos.Containing(self).Below();
            await Set(selfFloor, "obsidian");
            await RequireColumn(selfFloor);
            await Use("local-body-query", selfFloor, Hand.Main, protocol < 755 ? "Unknown" : "Fail", success: false, countBefore: 32, crystalsBefore: 0);

            Assert.Equal(8, scenarios.Count);
            Assert.False(capture.Overflowed);
            Assert.True(capture.PayloadBytes <= 20 * 1024 * 1024);
            int actionReplay = capture.AssertRoundTrips(client.State.Registries!);
            int stateReplay = ReplayAdventureFrames(capture.Frames, [], version, client.State.Registries!);
            int entityReplay = AssertMotionRoundTrips(version, capture.Frames, client.State.Registries!);
            var dispositions = new List<object>();
            int causalReplay = ReplayAdventureCausalFrames(capture.Frames, version, client.State.Registries!, [], dispositions);
            await server.WriteAsync("crystal-use-causal-replay.json", new { actionReplay, stateReplay, entityReplay, causalReplay, dispositions });
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "native crystal-use peer EOF", ct);
            await server.WriteAsync("crystal-use-acceptance.json", new { release, protocol, physics = true, scenarios, sourceAndFramedRuntimeImplemented = true, liveUseOnExercised = true, originalMinecraftClientMethodOrInputExecuted = false, originalClientWitness = "not run", legacyUnknownBodyControls = protocol < 755, recordingCap = 20 * 1024 * 1024, capture.PayloadBytes, peerObservedDisconnect = true });
            passed = true;

            async Task RequireColumn(BlockPos cell)
            {
                await WaitAsync(() => capture.Frames.Any(frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == cell.X >> 4 && chunk.ChunkZ == cell.Z >> 4), "actual full query-column frame", ct);
                for (int y = 99; y <= 102; y++)
                    Assert.True(await client.InvokeAsync(c => c.State.World.GetColumn(new BlockPos(cell.X, y, cell.Z)) is not null && c.State.World.GetBlock(new(cell.X, y, cell.Z)).IsValid, ct));
            }

            async Task Set(BlockPos cell, string block)
            {
                await server.ConsoleAsync($"setblock {cell.X} {cell.Y} {cell.Z} minecraft:{block}", ct);
                await WaitAsync(client, c => c.State.World.GetBlock(cell).Block.Id == Identifier.Minecraft(block), "applied native crystal setup " + block, ct);
                await AssertBlockAsync(server, protocol, cell, block, ct);
            }

            async Task Prepare(string block, Hand hand)
            {
                int[] initialEntities = await client.InvokeAsync(c => c.State.Entities.All.Select(entity => entity.Id).ToArray(), ct);
                await server.ConsoleAsync(protocol < 393 ? "kill @e[type=!Player]" : "kill @e[type=!minecraft:player]", ct);
                await WaitAsync(client, c => initialEntities.All(id => c.State.Entities.Get(id) is null), "initial native ambient removals applied before clearing container drops", ct);
                await server.ConsoleAsync(protocol < 393 ? "kill @e[type=!Player]" : "kill @e[type=!minecraft:player]", ct);
                await WaitAsync(client, c => !c.State.Entities.All.Any(), "all native ambient removals applied", ct);
                await server.ConsoleAsync($"gamemode survival {Username}", ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                string slot = hand == Hand.Main ? "hotbar.0" : "weapon.offhand";
                string inventoryCommand = protocol >= 755 ? $"item replace entity {Username} {slot} with minecraft:end_crystal 32" : $"replaceitem entity {Username} {(protocol < 393 ? "slot." : "")}{slot} minecraft:end_crystal 32";
                await server.ConsoleAsync(inventoryCommand, ct);
                await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && !c.State.Self.InstantBuild && c.State.Self.OnGround && Near(c.State.Self.Position, self) && CrystalStack(c.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45), 32) && c.State.Inventory.GetSlot(hand == Hand.Main ? 45 : 36).IsEmpty, "native default crystal hand and physics", ct);
                await Set(target, block);
                await Set(target.Above(), "air");
                await Set(target.Above().Above(), "air");
                await AssertNativeCount(32);
                await AssertNativeCrystals(0);
            }

            async Task Use(string scene, BlockPos clicked, Hand hand, string expectedPrediction, bool success, int countBefore, int crystalsBefore)
            {
                Assert.True(await client.InvokeAsync(c => c.State.Self.OnGround && Near(c.State.Self.Position, self), ct));
                Vec3d cursor = new(.5, 1, .5);
                await client.Actions.Movement.LookAtAsync(new(clicked.X + .5, clicked.Y + 1, clicked.Z + .5), ct);
                var snapshot = await client.Snapshots.SelfAsync(ct);
                Assert.True(snapshot.EyePosition.Subtract(new Vec3d(clicked.X + .5, clicked.Y + 1, clicked.Z + .5)).LengthSqr() < 4.5 * 4.5);
                CrystalUsePrediction prediction = await client.InvokeAsync(c => ReadCrystalUsePrediction(c, clicked, hand), ct);
                Assert.True(prediction.SendPacket);
                Assert.Equal(expectedPrediction, prediction.Result);
                Assert.Equal(success ? protocol == 777 ? "Local" : "Packet" : "None", prediction.Swing);
                await AssertNativeCount(countBefore);
                await AssertNativeCrystals(crystalsBefore);
                if (protocol < 393)
                    await AssertSavedLegacyPlayerPosition(scene);
                else
                    await ServerPositionNearAsync(server, Username, self, 1e-9, ct);
                const string groundTag = "{OnGround:1b}";
                string ownerCommand = protocol < 393 ? $"testfor @a[name={Username}] {groundTag}" : $"execute if entity @a[name={Username},nbt={groundTag}]";
                Assert.Equal(protocol < 393 ? "Found " + Username : protocol >= 773 ? "Test passed. Count: 1" : "Test passed, count: 1", await server.CommandAsync(ownerCommand, ct));
                int beforeState = await client.InvokeAsync(c => c.State.World.GetBlock(clicked).StateId, ct);
                int neighborState = await client.InvokeAsync(c => c.State.World.GetBlock(clicked.Above()).StateId, ct);
                await server.WriteAsync(scene + "-inputs.json", await client.InvokeAsync(c => VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, clicked, clicked.Above()), ct));
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(clicked, Direction.Up, cursor, hand, insideBlock: false, ct: ct);
                CapturedFrame processed = await ProcessingWitness(mark, clicked, beforeState, neighborState);
                int countAfter = countBefore - (success ? 1 : 0);
                int crystalsAfter = crystalsBefore + (success ? 1 : 0);
                await WaitAsync(client, c => CrystalStack(c.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45), countAfter) && c.State.Entities.All.Count(entity => entity.Type.Id == observedEntity) == crystalsAfter, "native crystal spawn/refusal and authoritative stack", ct);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                await AssertNativeCount(countAfter);
                await AssertNativeCrystals(crystalsAfter);
                if (crystalsAfter == 1)
                {
                    Vec3d expectedPosition = new(clicked.X + .5, clicked.Y + 1, clicked.Z + .5);
                    var actor = await client.InvokeAsync(c => Assert.Single(c.State.Entities.All, entity => entity.Type.Id == observedEntity), ct);
                    Assert.Equal(expectedPosition, actor.Position);
                    if (protocol < 393)
                    {
                        Assert.NotEqual(Guid.Empty, actor.Uuid);
                        string snapshotText = await server.ConsoleFeedbackAsync($"entitydata {actor.Uuid:D} {{}}", ct);
                        const string unchanged = "The data tag did not change: ";
                        Assert.StartsWith(unchanged, snapshotText, StringComparison.Ordinal);
                        MatchCollection positions = Regex.Matches(snapshotText[unchanged.Length..], @"(?:^|[,{])\s*Pos:\s*\[(?<pos>[^\]]*)\]", RegexOptions.CultureInvariant);
                        string positionText = Assert.Single(positions.Cast<Match>()).Groups["pos"].Value;
                        MatchCollection values = Regex.Matches(positionText, @"[-+]?(?:\d+\.\d+|\d+)(?:[eE][-+]?\d+)?[dD]", RegexOptions.CultureInvariant);
                        Assert.Equal(3, values.Count);
                        double Read(int index) => double.Parse(values[index].Value.TrimEnd('d', 'D'), NumberStyles.Float, CultureInfo.InvariantCulture);
                        Assert.Equal(expectedPosition, new Vec3d(Read(0), Read(1), Read(2)));
                    }
                    else
                        await ServerPositionNearAsync(server, $"@e[type={nativeEntity},limit=1]", expectedPosition, 1e-9, ct);
                }
                await AssertBlockAsync(server, protocol, clicked, await client.InvokeAsync(c => c.State.World.GetBlock(clicked).Block.Id.Path, ct), ct);
                Assert.Equal(beforeState, await client.InvokeAsync(c => c.State.World.GetBlock(clicked).StateId, ct));
                Assert.Equal(neighborState, await client.InvokeAsync(c => c.State.World.GetBlock(clicked.Above()).StateId, ct));
                CapturedFrame[] segment = capture.Since(mark);
                CapturedFrame[] actions = segment.Where(IsUseOrAnimation).ToArray();
                Assert.Equal(success && protocol != 777 ? 2 : 1, actions.Length);
                var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
                Assert.Equal((clicked, (int)Direction.Up, (int)hand), (use.Position, use.Face, use.Hand));
                Assert.Equal((.5f, 1f, .5f), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.False(use.Inside || use.WorldBorderHit);
                if (success && protocol != 777)
                    Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                Assert.DoesNotContain(segment, frame => frame.Packet is ServerboundPunchPacket or ServerboundPlayerActionPacket or ServerboundInteractPacket or ServerboundAttackPacket);
                Assert.DoesNotContain(segment, frame => frame.Packet is ClientboundExplodePacket);
                Assert.True(segment[^1].Milliseconds - processed.Milliseconds >= 2000, "Retain actual post-processing traffic under the unchanged capture cap.");
                Assert.False(capture.Overflowed);
                scenarios.Add(new { scene, clicked, hand, prediction, success, countBefore, countAfter, crystalsBefore, crystalsAfter, processingMilliseconds = processed.Milliseconds, packetOrder = actions.Select(frame => new { frame.Flow, frame.WireId, packet = frame.Packet!.GetType().Name, frame.Payload }).ToArray(), localOnlySwingHasNoGraphicalClaim = protocol == 777 && success, unknownRetained = expectedPrediction == "Unknown" });
                await server.WriteAsync("crystal-use-scenarios.json", scenarios);
            }

            async Task<CapturedFrame> ProcessingWitness(int mark, BlockPos clicked, int clickedState, int neighborState)
            {
                if (protocol >= 759)
                {
                    var use = Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(capture.Since(mark), frame => frame.Packet is ServerboundUseItemOnPacket).Packet);
                    Assert.True(use.Sequence > previousSequence);
                    previousSequence = use.Sequence;
                    await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == use.Sequence), "exact native crystal-use sequence ACK", ct);
                    return Assert.Single(capture.Since(mark), frame => frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == use.Sequence);
                }
                await WaitAsync(() => Pairs().Length != 0, "native clicked/above processing replies", ct);
                return Assert.Single(Pairs()).Second;

                (CapturedFrame First, CapturedFrame Second)[] Pairs()
                {
                    CapturedFrame[] inbound = capture.Since(mark).Where(frame => frame.Flow == PacketFlow.Clientbound).ToArray();
                    return Enumerable.Range(1, Math.Max(0, inbound.Length - 1)).Where(index => inbound[index - 1].Packet is ClientboundBlockUpdatePacket a && a.Position == clicked && a.BlockStateId == clickedState && inbound[index].Packet is ClientboundBlockUpdatePacket b && b.Position == clicked.Above() && b.BlockStateId == neighborState).Select(index => (inbound[index - 1], inbound[index])).ToArray();
                }
            }

            async Task AssertSavedLegacyPlayerPosition(string scene)
            {
                var login = await client.InvokeAsync(c => (c.State.Self.Uuid, c.State.Self.Username), ct);
                Assert.NotEqual(Guid.Empty, login.Uuid);
                Assert.Equal(Username, login.Username);
                string path = Path.Combine(server.DirectoryPath, "world", "playerdata", login.Uuid.ToString("D") + ".dat");
                Assert.False(File.Exists(path + ".tmp"));
                DateTime? previousWriteUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
                int logStart = server.ServerLog.Length;
                await server.ConsoleAsync("save-all", ct);
                string feedback = server.ServerLog[logStart..];
                int started = feedback.IndexOf("Saving...", StringComparison.Ordinal);
                int finished = feedback.IndexOf("Saved the world", StringComparison.Ordinal);
                Assert.True(started >= 0 && finished > started, feedback);
                Assert.DoesNotContain("Saving failed:", feedback, StringComparison.Ordinal);
                Assert.DoesNotContain("Failed to save player data", feedback, StringComparison.Ordinal);
                var saved = new FileInfo(path);
                Assert.True(saved.Exists, "The owned server did not create the current login UUID's player file.");
                Assert.False(File.Exists(path + ".tmp"));
                Assert.True(previousWriteUtc is null || saved.LastWriteTimeUtc > previousWriteUtc.Value, "The native player file is not demonstrably fresh; do not accept a previous save.");
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
                Assert.Equal(BinaryPrimitives.ReadInt64BigEndian(uuid.AsSpan(0, 8)), Assert.IsType<NbtLong>(data["UUIDMost"]).Value);
                Assert.Equal(BinaryPrimitives.ReadInt64BigEndian(uuid.AsSpan(8, 8)), Assert.IsType<NbtLong>(data["UUIDLeast"]).Value);
                NbtList positions = Assert.IsType<NbtList>(data["Pos"]);
                Assert.Equal(NbtTagType.Double, positions.ElementType);
                Assert.Equal(3, positions.Count);
                Vec3d position = new(Assert.IsType<NbtDouble>(positions[0]).Value, Assert.IsType<NbtDouble>(positions[1]).Value, Assert.IsType<NbtDouble>(positions[2]).Value);
                Assert.Equal(self, position);
                Assert.Equal((sbyte)1, Assert.IsType<NbtByte>(data["OnGround"]).Value);
                var afterRead = new FileInfo(path);
                Assert.Equal(saved.LastWriteTimeUtc, afterRead.LastWriteTimeUtc);
                Assert.Equal(saved.Length, afterRead.Length);
                Assert.False(File.Exists(path + ".tmp"));
                await File.WriteAllBytesAsync(Path.Combine(server.DirectoryPath, scene + "-playerdata.dat"), compressed, ct);
                await server.WriteAsync(scene + "-player-position.json", new { login.Uuid, login.Username, source = "owned native save-all playerdata", position, previousWriteUtc, saved.LastWriteTimeUtc, compressedBytes = compressed.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(compressed)), orderedXyz = true, gzipNamedRoot = true });
            }

            async Task AssertNativeCount(int count)
            {
                string command = protocol < 393 ? $"clear {Username} minecraft:end_crystal -1 0" : $"clear {Username} minecraft:end_crystal 0";
                string response = await server.CommandAsync(command, ct);
                Assert.Contains(response, protocol < 393 ? new[] { $"{Username} has {count} items that match the criteria" } : new[] { $"Found {count} matching items on player {Username}", $"Found {count} matching item(s) on player {Username}" });
            }

            async Task AssertNativeCrystals(int count)
            {
                await server.ConsoleAsync("scoreboard players set crystalTotal crystalUse 0", ct);
                string countCommand = protocol < 393 ? $"execute @e[type={nativeEntity}] ~ ~ ~ scoreboard players add crystalTotal crystalUse 1" : $"execute as @e[type={nativeEntity}] run scoreboard players add crystalTotal crystalUse 1";
                await server.ConsoleAsync(countCommand, ct);
                string command = protocol < 393 ? $"scoreboard players test crystalTotal crystalUse {count} {count}" : $"execute if score crystalTotal crystalUse matches {count}";
                Assert.Equal(protocol < 393 ? $"Score {count} is in range {count} to {count}" : "Test passed", await server.CommandAsync(command, ct));
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("crystal-use-capture.json", capture.Frames);
            await server.WriteAsync("crystal-use-result.json", new { passed, release, protocol, completedScenes = scenarios.Count, capture.PayloadBytes, capture.Overflowed, sourceAndFramedRuntimeImplemented = true, liveUseOnExercised = scenarios.Count != 0, originalMinecraftClientMethodOrInputExecuted = false });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static bool CrystalStack(ItemStack stack, int count) => !stack.IsEmpty && stack.Item.Id == Identifier.Minecraft("end_crystal") && stack.Count == count && stack.Components.HasNoPatch;

    private static CrystalUsePrediction ReadCrystalUsePrediction(UmpkClient client, BlockPos clicked, Hand hand)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object prediction = client.Actions.Interaction.GetType().GetMethod("PredictBlockUse", flags)!.Invoke(client.Actions.Interaction, [clicked, Direction.Up, new Vec3d(.5, 1, .5), hand])!;
        return new((bool)prediction.GetType().GetProperty("SendPacket")!.GetValue(prediction)!, prediction.GetType().GetProperty("Result")!.GetValue(prediction)!.ToString()!, prediction.GetType().GetProperty("Swing")!.GetValue(prediction)!.ToString()!);
    }

    private sealed record CrystalUsePrediction(bool SendPacket, string Result, string Swing);

    private static async Task VerifyCrystalUsePrerequisites()
    {
        string path = Environment.GetEnvironmentVariable("UMPK_CRYSTAL_USE_PREREQUISITE_RECEIPT") ?? throw new InvalidOperationException("Supply the reviewed final combined producer receipt before live crystal-item execution.");
        byte[] bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_CRYSTAL_USE_PREREQUISITE_SHA256"), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement receipt = document.RootElement;
        Assert.Equal("ready for bounded live crystal-item acceptance", receipt.GetProperty("status").GetString());
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_INTERACTION_PRODUCER"), receipt.GetProperty("producer_commit").GetString());
        Assert.False(receipt.GetProperty("offline_compatibility_only").GetBoolean());
        Assert.Equal(86, receipt.GetProperty("executed_passing_useOn_runtime_cases").GetInt32());
        Assert.True(receipt.GetProperty("aot_bytes").GetInt32() < 25_165_824);
        await AssertTrx("final_client_full_trx", receipt.GetProperty("expected_final_client_cases").GetInt32());
        await AssertTrx("final_aot_smoke_trx", 1);
        foreach (JsonProperty source in receipt.GetProperty("harness_sources").EnumerateObject())
            Assert.Equal(source.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(FixturePaths.RepoRoot(), source.Name)))));
        var actual = Directory.GetFiles(AppContext.BaseDirectory, "Umpk.*").Where(file => Path.GetExtension(file) is ".dll" or ".pdb").ToDictionary(file => Path.GetFileName(file), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        JsonElement binaries = receipt.GetProperty("integration_binaries");
        Assert.Equal(actual.Keys.Order(StringComparer.Ordinal), binaries.EnumerateObject().Select(binary => binary.Name).Order(StringComparer.Ordinal));
        foreach (var binary in actual)
            Assert.Equal(binary.Value, binaries.GetProperty(binary.Key!).GetString());

        async Task AssertTrx(string key, int expected)
        {
            Assert.True(expected > 0);
            JsonElement pin = receipt.GetProperty(key);
            byte[] trx = await File.ReadAllBytesAsync(pin.GetProperty("path").GetString()!);
            Assert.Equal(pin.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(trx)));
            using var trxStream = new MemoryStream(trx, writable: false);
            XDocument result = XDocument.Load(trxStream);
            XNamespace ns = result.Root!.Name.Namespace;
            XElement counters = result.Descendants(ns + "Counters").Single();
            Assert.Equal(expected, (int)counters.Attribute("total")!);
            Assert.Equal(expected, (int)counters.Attribute("executed")!);
            Assert.Equal(expected, (int)counters.Attribute("passed")!);
            Assert.Equal(0, (int)counters.Attribute("failed")!);
            XElement[] rows = result.Descendants(ns + "UnitTestResult").ToArray();
            Assert.Equal(expected, rows.Length);
            Assert.All(rows, row => Assert.Equal("Passed", (string?)row.Attribute("outcome")));
        }
    }
}
