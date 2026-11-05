using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    /// <summary>Returns only the controlled 1.11/315 slice and rejects other configured selectors before yielding a row.</summary>
    public static IEnumerable<object[]> DoorBoundaryReleases()
    {
        string selected = Environment.GetEnvironmentVariable("UMPK_DOOR_RELEASES") ?? "1.11";
        string[] releases = selected.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(releases);
        Assert.Equal(releases.Length, releases.Distinct(StringComparer.Ordinal).Count());
        Assert.All(releases, release => Assert.Equal("1.11", release));
        foreach (string release in releases)
        {
            Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
            Assert.Equal(315, version.Version.Protocol);
            yield return [release, version.Version.Protocol];
        }
    }

    /// <summary>Checks only controlled, observer-assisted 1.11/315 Door behavior through public actions; the other five boundaries remain unexecuted by this fixture.</summary>
    /// <remarks>The existing observer invokes real prediction and geometry owners before the public action and may refresh or invalidate their caches.</remarks>
    [NightlyTheory]
    [MemberData(nameof(DoorBoundaryReleases))]
    public async Task DoorUseMatchesNativeActivationPassAndSwing(string release, int protocol)
    {
        Assert.Equal("1.11", release);
        Assert.Equal(315, protocol);
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        Assert.Equal(protocol, version.Version.Protocol);
        bool experimentalCopper = Environment.GetEnvironmentVariable("UMPK_DOOR_EXPERIMENTAL_COPPER") == "1";
        if (experimentalCopper)
            Assert.Contains(protocol, new[] { 765, 766 });
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct, viewDistance: 5);
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        var features = new List<DoorFeatureFrame>();
        var featureGate = new Lock();
        var publishedFeatures = new List<Identifier[]>();
        using IDisposable featureEvents = client.Events.Subscribe<PacketReceived>(received =>
        {
            if (received.Packet is ClientboundConfigUpdateEnabledFeaturesPacket packet)
                lock (featureGate)
                    publishedFeatures.Add(packet.Features.ToArray());
        });
        long featureBytes = 0;
        bool featureOverflow = false;
        void Observe(PacketObservation observation)
        {
            lock (featureGate)
            {
                bool feature = DoorFeatureObservationMatches(observation.Phase, observation.Flow, observation.DecodedPacket);
                if (featureOverflow)
                    return;
                if ((feature || observation.Phase == ProtocolPhase.Play) && !DoorRecordingFits(capture.PayloadBytes, featureBytes, observation.PayloadLength))
                {
                    featureOverflow = true;
                    return;
                }
                capture.Observe(observation);
                if (!feature)
                    return;
                featureBytes += observation.PayloadLength;
                var packet = (ClientboundConfigUpdateEnabledFeaturesPacket)observation.DecodedPacket!;
                features.Add(new(observation.Phase, observation.Flow, observation.WireId, observation.CopyPayload(), packet.Features.ToArray()));
            }
        }
        client.PacketFrameObserved += Observe;
        var scenarios = new List<object>();
        var dispositions = new List<object>();
        var markers = new List<string>();
        bool passed = false;
        int previousSequence = 0;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "native door terrain", ct);
            Assert.True(client.State.Features.Physics);
            string ownSource = Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.IntegrationTests", "VanillaInteractionAcceptanceLiveTests.Door.cs");
            await server.WriteAsync("door-source.json", new { sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(ownSource, ct))) });
            DoorFeatureFrame[] nativeFeatures;
            lock (featureGate)
                nativeFeatures = features.ToArray();
            if (protocol >= 764)
            {
                DoorFeatureFrame native = Assert.Single(nativeFeatures);
                Assert.Equal(ProtocolPhase.Configuration, native.Phase);
                Assert.Contains(Identifier.Minecraft("vanilla"), native.Features);
                lock (featureGate)
                    Assert.Equal(native.Features, Assert.Single(publishedFeatures));
            }
            else
                Assert.Empty(nativeFeatures);
            if (experimentalCopper)
                Assert.Contains(Identifier.Minecraft("update_1_21"), Assert.Single(nativeFeatures).Features);
            int featureReplayed = ReplayDoorFeatures(nativeFeatures, version, client.State.Registries!);
            await server.WriteAsync("door-native-features.json", nativeFeatures);
            await server.WriteAsync("door-native-feature-events.json", publishedFeatures);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = protocol < 393 ? 64 : (int)Math.Floor(spawn.X), z = protocol < 393 ? 192 : (int)Math.Floor(spawn.Z);
            var lower = new BlockPos(x + ((x & 15) >= 14 ? -2 : 2), 100, z);
            BlockPos upper = lower.Above();
            Direction face = lower.X > x ? Direction.West : Direction.East;
            var cursor = new Vec3d(face == Direction.West ? 0 : 1, .5, .5);
            Assert.Equal((x >> 4, z >> 4), (lower.X >> 4, lower.Z >> 4));
            await server.ConsoleAsync("difficulty peaceful", ct);
            string dropRule = protocol == 777 ? "block_drops" : "doTileDrops";
            await server.ConsoleAsync("gamerule " + dropRule + " false", ct);
            string dropReadback = await server.CommandAsync("gamerule " + dropRule, ct);
            Assert.Contains(dropRule, dropReadback, StringComparison.Ordinal);
            Assert.EndsWith("false", dropReadback, StringComparison.Ordinal);
            await server.WriteAsync("door-cleanup-rule.json", new { name = dropRule, response = dropReadback, role = "Prevent setup removal from dropping old doors into the later inventory scenario; action behavior does not destroy a door." });
            DoorLegacyPrimerProof? legacyPrimer = null;
            if (protocol < 393)
            {
                // Preparing the fixed legacy destination requires native player chunk delivery before Fill can use its loaded-column precondition.
                int modeStart = capture.Mark();
                DoorLegacyInitialPacketEvidence initial = DoorLegacyInitialPackets(capture.Frames[..modeStart]);
                await client.InvokeAsync(c =>
                {
                    Assert.Equal((GameMode)initial.GameType, c.State.Self.GameMode);
                    Assert.Equal((initial.Flags & 1) != 0, c.State.Self.Invulnerable);
                    Assert.Equal((initial.Flags & 2) != 0, c.State.Self.Flying);
                    Assert.Equal((initial.Flags & 4) != 0, c.State.Self.MayFly);
                    Assert.Equal((initial.Flags & 8) != 0, c.State.Self.InstantBuild);
                    Assert.Equal(initial.FlyingSpeed, c.State.Self.FlyingSpeed);
                    Assert.Equal(initial.WalkingSpeed, c.State.Self.WalkingSpeed);
                    return true;
                }, ct);
                await server.ConsoleAsync($"gamemode creative {Username}", ct);
                await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Creative && c.State.Self.Invulnerable && c.State.Self.MayFly && c.State.Self.InstantBuild, "explicit native Creative preparation mode", ct);
                await WaitAsync(() => capture.Since(modeStart).Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundGameEventPacket { Event: 3, Param: 1 }) && capture.Since(modeStart).Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundPlayerAbilitiesPacket abilities && (abilities.Flags & 13) == 13), "actual Creative game event and ability frames", ct);
                int primerStart = capture.Mark();
                DoorLegacySetupPacketEvidence preparation = DoorLegacySetupPackets(capture.Frames[..primerStart], modeStart);
                await client.InvokeAsync(c =>
                {
                    Assert.Equal((preparation.Flags & 2) != 0, c.State.Self.Flying);
                    Assert.Equal(preparation.FlyingSpeed, c.State.Self.FlyingSpeed);
                    Assert.Equal(preparation.WalkingSpeed, c.State.Self.WalkingSpeed);
                    return true;
                }, ct);
                await server.ConsoleAsync($"tp {Username} {x + .5:R} 100 {z + .5:R}", ct);
                await WaitAsync(client, c => c.State.Self.Position.X == 64.5 && c.State.Self.Position.Z == 192.5, "native legacy destination teleport", ct);
                await WaitAsync(() => DoorHasFillTerrain(capture.Frames, x, z), "all packet-established full columns for both legacy fill rectangles", ct);
                CapturedFrame[] terrainFrames = capture.Frames;
                legacyPrimer = DoorLegacyPrimerFrames(terrainFrames, modeStart, primerStart, terrainFrames.Length, x, z);
                await server.WriteAsync("door-terrain-primer.json", new { status = "full terrain established; native floor and air not yet checked", protocol, physics = true, setupGameMode = "Creative", proof = legacyPrimer, nativeFloorAirReadback = false });
            }
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            if (protocol < 393)
            {
                await AssertBlockAsync(server, protocol, new BlockPos(x, 99, z), "stone", ct);
                await AssertBlockAsync(server, protocol, new BlockPos(x, 100, z), "air", ct);
                await AssertBlockAsync(server, protocol, new BlockPos(x, 101, z), "air", ct);
                await WaitAsync(client, c => c.State.World.GetBlock(new BlockPos(x, 99, z)).Block.Id == Identifier.Minecraft("stone") && c.State.World.GetBlock(new BlockPos(x, 100, z)).Block.Id == Identifier.Minecraft("air") && c.State.World.GetBlock(new BlockPos(x, 101, z)).Block.Id == Identifier.Minecraft("air"), "packet-applied legacy floor and body clearance", ct);
            }
            await server.ConsoleAsync($"tp {Username} {x + .5:R} 100 {z + .5:R}", ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && c.State.Self.OnGround && Math.Abs(c.State.Self.Position.Y - 100) < .01 && (protocol >= 393 || c.State.Self.Position.X == 64.5 && c.State.Self.Position.Z == 192.5), "grounded native door actor", ct);
            await WaitAsync(() => capture.Frames.Any(frame => frame.Flow == PacketFlow.Clientbound && (frame.Packet is ClientboundLevelChunkPacket chunk && chunk.FullChunk && chunk.ChunkX == lower.X >> 4 && chunk.ChunkZ == lower.Z >> 4 || protocol == 47 && frame.Packet is ClientboundMapChunkBulkPacket bulk && bulk.Columns.Any(column => column.Position.X == lower.X >> 4 && column.Position.Z == lower.Z >> 4))), "packet-established door column", ct);
            if (protocol < 393)
                await DoorWaitLegacyGroundedAsync(server, x, z, ct);
            else
                await NativePredicate($"execute if entity @a[name={Username},x={x + .5:R},y=100,z={z + .5:R},distance=..0.02,nbt={{OnGround:1b}}]");

            if (protocol < 393)
            {
                using JsonDocument commands = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(server.DirectoryPath, "commands.json"), ct));
                DoorAssertLegacyPrimerCommands(commands.RootElement, protocol, x, z);
                DoorAssertLegacySurvivalPackets(capture.Frames, legacyPrimer!.TerrainEndMark);
                await server.WriteAsync("door-terrain-primer.json", new { status = "passed", protocol, physics = true, setupGameMode = "Creative", actionGameMode = "Survival", proof = legacyPrimer, nativeFloorAirReadback = true, floor = new BlockPos(x, 99, z), lowerAir = new BlockPos(x, 100, z), upperAir = new BlockPos(x, 101, z), commands = commands.RootElement.Clone(), noScenarioActionBeforeSetup = true });
            }

            Assert.Equal(315, protocol);
            const double wolfX = 82.40966796875, wolfY = 79, wolfZ = 153.26318359375;
            var wolfCell = BlockPos.Containing(new Vec3d(wolfX, wolfY, wolfZ));
            await WaitAsync(() => capture.Frames.Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == wolfCell.X >> 4 && chunk.ChunkZ == wolfCell.Z >> 4), "native full column for the controlled far wolf", ct);
            await WaitAsync(client, c => c.State.World.GetColumn(wolfCell) is not null, "owned application of the wolf column", ct);
            BlockPos[] wolfFloor = [new(82, 78, 152), new(82, 78, 153)];
            BlockPos[] wolfAir = [new(82, 79, 152), new(82, 79, 153), new(82, 80, 152), new(82, 80, 153)];
            await server.ConsoleAsync("fill 82 78 152 82 78 153 minecraft:stone", ct);
            await server.ConsoleAsync("fill 82 79 152 82 80 153 minecraft:air", ct);
            foreach (BlockPos floor in wolfFloor)
                await AssertBlockAsync(server, protocol, floor, "stone", ct);
            foreach (BlockPos air in wolfAir)
                await AssertBlockAsync(server, protocol, air, "air", ct);
            await WaitAsync(client, c => wolfFloor.All(cell => c.State.World.GetBlock(cell).IsValid && c.State.World.GetBlock(cell).Block.Id == Identifier.Minecraft("stone")) && wolfAir.All(cell => c.State.World.GetBlock(cell).IsValid && c.State.World.GetBlock(cell).IsAir), "packet-applied native floor and body air for the controlled wolf", ct);
            string wolfName = "UmpkWolf" + Guid.NewGuid().ToString("N");
            string wolfPredicate = $"testfor @e[type=wolf,name={wolfName}] {{NoAI:1b,PersistenceRequired:1b,Age:0}}";
            int wolfStart = capture.Mark();
            await server.ConsoleAsync($"summon minecraft:wolf {wolfX:R} {wolfY:R} {wolfZ:R} {{NoAI:1b,PersistenceRequired:1b,Age:0,CustomName:\"{wolfName}\"}}", ct);
            string wolfNativeReadback = await server.CommandAsync(wolfPredicate, ct);
            Assert.Equal("Found " + wolfName, wolfNativeReadback);
            await WaitAsync(() => capture.Since(wolfStart).Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundAddMobPacket { TypeId: 95 } spawn && spawn.Metadata.Entries.Any(entry => entry.Index == 2 && entry.Value.Equals(MetadataValue.String(wolfName)))), "actual native type95 wolf spawn and metadata", ct);
            CapturedFrame wolfFrame = Assert.Single(capture.Since(wolfStart), frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundAddMobPacket { TypeId: 95 } spawn && spawn.Metadata.Entries.Any(entry => entry.Index == 2 && entry.Value.Equals(MetadataValue.String(wolfName))));
            var wolfSpawn = Assert.IsType<ClientboundAddMobPacket>(wolfFrame.Packet);
            Assert.Equal((wolfX, wolfY, wolfZ), (wolfSpawn.X, wolfSpawn.Y, wolfSpawn.Z));
            Assert.Empty(wolfSpawn.Metadata.RawTail);
            Assert.Contains(wolfSpawn.Metadata.Entries, entry => entry.Index == 2 && entry.WireSerializerId == 3 && entry.Value.Equals(MetadataValue.String(wolfName)));
            Assert.Contains(wolfSpawn.Metadata.Entries, entry => entry.Index == 11 && entry.WireSerializerId == 0 && entry.Value.Kind == MetadataValueKind.Byte && (entry.Value.AsByte() & 1) == 1);
            Assert.Contains(wolfSpawn.Metadata.Entries, entry => entry.Index == 12 && entry.WireSerializerId == 6 && entry.Value.Equals(MetadataValue.Boolean(false)));
            var wolfCodecContext = new PacketCodecContext(client.State.Registries!, IConnectionCodecState.Empty);
            Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).TryGetInbound(wolfFrame.WireId, out BoundPacketCodec wolfCodec));
            var wolfDecoded = Assert.IsType<ClientboundAddMobPacket>(wolfCodec.Decode(wolfFrame.Payload, wolfCodecContext));
            Assert.Equal((wolfSpawn.EntityId, wolfSpawn.Uuid, 95, wolfX, wolfY, wolfZ), (wolfDecoded.EntityId, wolfDecoded.Uuid, wolfDecoded.TypeId, wolfDecoded.X, wolfDecoded.Y, wolfDecoded.Z));
            Assert.Equal(wolfSpawn.Metadata.Entries.ToArray(), wolfDecoded.Metadata.Entries.ToArray());
            var wolfEncoded = new ArrayBufferWriter<byte>();
            var wolfWriter = new PacketWriter(wolfEncoded);
            wolfCodec.Encode(ref wolfWriter, wolfDecoded, wolfCodecContext);
            Assert.Equal(wolfFrame.Payload, wolfEncoded.WrittenSpan.ToArray());
            await WaitAsync(client, c => c.State.Entities.Get(wolfSpawn.EntityId) is { } wolf && wolf.Uuid == wolfSpawn.Uuid && wolf.Type.Id == Identifier.Minecraft("wolf") && wolf.Metadata.TryGet(2, out MetadataValue name) && name.Equals(MetadataValue.String(wolfName)) && wolf.Metadata.TryGet(11, out MetadataValue flags) && flags.Kind == MetadataValueKind.Byte && (flags.AsByte() & 1) == 1 && wolf.Metadata.TryGet(12, out MetadataValue baby) && baby.Equals(MetadataValue.Boolean(false)), "owned application fence for native wolf identity and initial metadata", ct);
            var wolfObservations = new List<object>();
            await server.WriteAsync("door-controlled-wolf.json", new { status = "native wolf established before scenes", nativeEntityName = wolfName, wolfSpawn.EntityId, wolfSpawn.Uuid, nativeType = 95, requestedPosition = new Vec3d(wolfX, wolfY, wolfZ), nativePosition = new Vec3d(wolfSpawn.X, wolfSpawn.Y, wolfSpawn.Z), wolfCell, nativeReadback = wolfNativeReadback, nativeFloorAndAirReadback = true, ownedFloorAndAirApplied = true, floor = wolfFloor, bodyAir = wolfAir, noGravityOverride = false, initialMetadata = wolfSpawn.Metadata.Entries.Select(entry => new { entry.Index, entry.WireSerializerId, kind = entry.Value.Kind.ToString(), value = entry.Value.ToString() }).ToArray(), spawnPayloadSha256 = Convert.ToHexStringLower(SHA256.HashData(wolfFrame.Payload)), spawnByteRoundTrip = true, ownedPacketApplicationFence = true, observations = wolfObservations });

            foreach (DoorScenario scene in DoorScenarios(protocol))
            {
                if (scene.RequiredFeature is { } required && !nativeFeatures.Any(frame => frame.Features.Contains(Identifier.Minecraft(required))))
                {
                    dispositions.Add(new { scene.Id, status = "not run", reason = "The actual native Configuration feature set omits the required experimental flag; the maintained launcher does not enable experimental packs. No flag, block, stack or action was synthesized.", requiredFeature = required, actualFeatures = nativeFeatures.SelectMany(frame => frame.Features).Select(id => id.ToString()).ToArray() });
                    continue;
                }
                await client.Actions.Movement.SetSneakingAsync(false, ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                // Rebuild both native halves, then require independent native and packet-applied readback before acting. Invalid stable half pairs are never assumed.
                await SetCell(upper, "air");
                await SetCell(lower, "air");
                await SetCell(lower.Offset(face), "air");
                await SetCell(upper.Offset(face), "air");
                await SetCell(lower, DoorBlockSpec(scene.Block, upper: false, scene.InitialOpen));
                await SetCell(upper, DoorBlockSpec(scene.Block, upper: true, scene.InitialOpen));
                await WaitAsync(client, c => DoorStateMatches(c.State.World.GetBlock(lower), protocol, scene.Block, upper: false, scene.InitialOpen) && DoorStateMatches(c.State.World.GetBlock(upper), protocol, scene.Block, upper: true, scene.InitialOpen), "native complete door setup", ct);
                await AssertPair(scene.Block, scene.InitialOpen);
                if (scene.Main is { } main)
                    await server.ConsoleAsync($"give {Username} minecraft:{main} {DoorCount(main)}", ct);
                if (scene.Off is { } off)
                    await server.ConsoleAsync(protocol >= 755 ? $"item replace entity {Username} weapon.offhand with minecraft:{off} 1" : $"replaceitem entity {Username} {(protocol == 315 ? "slot.weapon.offhand" : "weapon.offhand")} minecraft:{off} 1", ct);
                await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
                await WaitAsync(client, c => DoorStackMatches(c.State.Inventory.GetSlot(36), scene.Main, DoorCount(scene.Main)) && (protocol == 47 || DoorStackMatches(c.State.Inventory.GetSlot(45), scene.Off, scene.Off is null ? 0 : 1)), "actual native door stacks", ct);
                await AssertInventory(scene.Main, DoorCount(scene.Main), scene.Off);
                (ItemStack mainBefore, ItemStack offBefore) = await client.InvokeAsync(c => (c.State.Inventory.GetSlot(36), protocol == 47 ? ItemStack.Empty : c.State.Inventory.GetSlot(45)), ct);
                if (scene.Sneak)
                    await client.Actions.Movement.SetSneakingAsync(true, ct);
                await client.InvokeAsync(c =>
                {
                    Assert.Equal(scene.Sneak, c.State.Self.Sneaking);
                    Assert.True(c.State.Self.OnGround);
                    Assert.InRange(c.State.Self.Position.Y, 99.99, 100.01);
                    return true;
                }, ct);
                await Marker("precondition");
                BlockPos clicked = scene.Upper ? upper : lower;
                BlockPos neighbor = clicked.Offset(face);
                int expectedClicked = DoorStateId(client.State.Registries!, protocol, scene.Block, scene.Upper, scene.FinalOpen);
                int expectedNeighbor = client.State.Registries!.Blocks[Identifier.Minecraft(scene.PlaceStone ? "stone" : "air")].Value.DefaultStateId;
                Assert.Equal("Found " + wolfName, await server.CommandAsync(wolfPredicate, ct));
                DoorControlledWolfSnapshot wolfBeforeAction = await client.InvokeAsync(c => ReadDoorControlledWolf(c, wolfSpawn.EntityId, wolfSpawn.Uuid, clicked, face, cursor, scene.Hand), ct);
                wolfObservations.Add(new { scene.Id, snapshot = wolfBeforeAction, nativeEntityExists = true, nativeNoAiAndPersistenceReadBack = true, ownedObservationBeforeAction = true });
                await server.WriteAsync("door-controlled-wolf-observations.json", wolfObservations);
                Assert.True(wolfBeforeAction.Member);
                Assert.False(wolfBeforeAction.ExactBodyKnown);
                Assert.False(wolfBeforeAction.AmbientController);
                Assert.False(wolfBeforeAction.OrdinaryLivingController);
                Assert.True(wolfBeforeAction.EnvelopeExcluded);
                if (scene.Id == "iron-held-stone")
                {
                    Assert.True(wolfBeforeAction.SendPacket);
                    Assert.Equal("Success", wolfBeforeAction.Result);
                    Assert.Equal("Packet", wolfBeforeAction.Swing);
                }
                int mark = capture.Mark();
                object processing;
                if (scene.RejectHand)
                {
                    await Assert.ThrowsAsync<ActionNotSupportedException>(() => client.Actions.Interaction.PlaceBlockAsync(clicked, face, cursor, scene.Hand, ct: ct));
                    processing = new { kind = "pre-send legacy hand rejection", noNativeUse = true };
                }
                else
                {
                    await client.Actions.Interaction.PlaceBlockAsync(clicked, face, cursor, scene.Hand, ct: ct);
                    processing = await ProcessingWitness(mark, clicked, neighbor, expectedClicked, expectedNeighbor);
                }
                await WaitAsync(client, c => DoorStateMatches(c.State.World.GetBlock(lower), protocol, scene.Block, upper: false, scene.FinalOpen) && DoorStateMatches(c.State.World.GetBlock(upper), protocol, scene.Block, upper: true, scene.FinalOpen) && c.State.World.GetBlock(neighbor).Block.Id == Identifier.Minecraft(scene.PlaceStone ? "stone" : "air") && DoorStackMatches(c.State.Inventory.GetSlot(36), scene.Main, DoorCount(scene.Main) - (scene.PlaceStone ? 1 : 0)), "native door result and inventory", ct);
                await AssertPair(scene.Block, scene.FinalOpen);
                await AssertBlockAsync(server, protocol, neighbor, scene.PlaceStone ? "stone" : "air", ct);
                await AssertInventory(scene.Main, DoorCount(scene.Main) - (scene.PlaceStone ? 1 : 0), scene.Off);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                string postActionMarker = await Marker("post-action");
                await client.InvokeAsync(c =>
                {
                    Assert.Equal(scene.PlaceStone ? mainBefore.WithCount(mainBefore.Count - 1) : mainBefore, c.State.Inventory.GetSlot(36));
                    if (protocol != 47)
                        Assert.Equal(offBefore, c.State.Inventory.GetSlot(45));
                    return true;
                }, ct);
                int captureEndMark = capture.Mark();
                CapturedFrame[] frames = capture.Since(mark);
                AssertDoorActions(frames, protocol, clicked, face, cursor, scene.Hand, scene.Main, scene.PacketSwing, scene.RejectHand);
                scenarios.Add(new { scene.Id, status = "passed", scene.Block, scene.Upper, scene.InitialOpen, scene.FinalOpen, scene.Main, scene.Off, scene.Sneak, scene.Hand, scene.PacketSwing, scene.PlaceStone, scene.RejectHand, clicked, neighbor, face, cursor, expectedClicked, expectedNeighbor, captureStartMark = mark, captureEndMark, postActionMarker, processing, capturedActions = frames.Count(IsUseOrAnimation), nativePairReadback = true, nativeInventoryReadback = true, mainComponentsUnchanged = true, offComponentsUnchanged = true, mainDamage = protocol < 393 || protocol >= 766 ? mainBefore.Damage : mainBefore.Components.Get(DataComponents.LegacyNbt)?.Nbt.GetInt("Damage") ?? 0, postActionCaptureMilliseconds = 2100 });
                await server.WriteAsync("door-scenarios.json", scenarios);
                await server.WriteAsync("door-dispositions.json", dispositions);
            }
            Assert.False(capture.Overflowed);
            Assert.False(featureOverflow);
            Assert.InRange(capture.PayloadBytes + featureBytes, 1, 20 * 1024 * 1024);
            int actionReplay = capture.AssertRoundTrips(client.State.Registries!);
            var replay = new List<object>();
            int causalReplay = ReplayMenuFrames(capture.Frames, version, client.State.Registries!, markers, replay);
            await server.WriteAsync("door-causal-replay.json", new { expectedMarkers = markers, byteIdenticalFrames = causalReplay, frames = replay, scope = "Existing maintained typed replay. Every exact causal marker and selected nonchat frame requires byte identity; ambient chat records semantic checks and any raw/reencoded differences." });
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "door peer-observed disconnect", ct);
            await server.WriteAsync("door-acceptance.json", new { release, protocol, clientProfile = version.Version.Name, protocol47Profile = protocol == 47 ? "1.8.9" : null, physics = true, normalLauncher = !experimentalCopper, launcherVariant = experimentalCopper ? "explicit native initial-pack properties" : "maintained default launcher", evidenceOrigin = "native vanilla server session", nativeFeatureFrameCount = nativeFeatures.Length, featureReplay = featureReplayed, actionReplay, causalReplay, markers = markers.Count, scenarios, dispositions, totalPayloadBytes = capture.PayloadBytes + featureBytes, peerObservedDisconnect = true, originalMinecraftInputOrNetworkExecuted = false, invalidStableHalfPairsClaimed = false, missingContextNativeActionClaimed = false });
            passed = true;

            async Task SetCell(BlockPos position, string state)
            {
                await server.ConsoleAsync($"setblock {position.X} {position.Y} {position.Z} minecraft:{state}", ct);
            }

            string DoorBlockSpec(string block, bool upper, bool open) => protocol < 393 ? block + " " + (upper ? 8 : open ? 4 : 0) : block + "[half=" + (upper ? "upper" : "lower") + ",open=" + (open ? "true" : "false") + ",facing=north,hinge=left,powered=false]";

            async Task AssertPair(string block, bool open)
            {
                await AssertBlockAsync(server, protocol, lower, protocol < 393 ? block : block + "[half=lower,open=" + (open ? "true" : "false") + "]", ct, protocol < 393 ? open ? 4 : 0 : null);
                await AssertBlockAsync(server, protocol, upper, protocol < 393 ? block : block + "[half=upper,open=" + (open ? "true" : "false") + "]", ct, protocol < 393 ? 8 : null);
            }

            async Task AssertInventory(string? main, int count, string? off)
            {
                var stacks = new List<string>();
                if (main is not null)
                    stacks.Add(protocol >= 766 ? $"{{Slot:0b,id:\"minecraft:{main}\",count:{count}}}" : $"{{Slot:0b,id:\"minecraft:{main}\",Count:{count}b}}");
                if (off is not null)
                    stacks.Add(protocol >= 766 ? $"{{Slot:-106b,id:\"minecraft:{off}\",count:1}}" : $"{{Slot:-106b,id:\"minecraft:{off}\",Count:1b}}");
                string nbt = "{Inventory:[" + string.Join(',', stacks) + "]}";
                if (protocol < 393)
                    Assert.Equal("Found " + Username, await server.CommandAsync($"testfor @a[name={Username}] {nbt}", ct));
                else if (stacks.Count == 0)
                    await NativePredicate($"execute unless data entity {Username} Inventory[0]");
                else
                    await NativePredicate($"execute if entity @a[name={Username},nbt={nbt}]");
                if (main == "diamond_axe")
                    await NativePredicate(protocol < 766 ? "execute if entity @a[name=" + Username + ",nbt={SelectedItem:{id:\"minecraft:diamond_axe\",tag:{Damage:0}}}]" : $"execute if items entity {Username} weapon.mainhand minecraft:diamond_axe[minecraft:damage=0]");
            }

            async Task NativePredicate(string predicate)
            {
                string marker = "door-native-" + Guid.NewGuid().ToString("N");
                markers.Add(marker);
                await server.CommandAsync(predicate + " run say " + marker, ct);
                await WaitAsync(() => capture.Frames.Any(frame => MenuChatContains(frame.Packet, marker)), "independent native door predicate", ct);
            }

            async Task<string> Marker(string role)
            {
                string marker = "door-" + role + "-" + Guid.NewGuid().ToString("N");
                markers.Add(marker);
                await server.ConsoleAsync("say " + marker, ct);
                await WaitAsync(() => capture.Frames.Any(frame => MenuChatContains(frame.Packet, marker)), "exact native door causal marker", ct);
                Assert.Single(capture.Frames, frame => MenuChatContains(frame.Packet, marker));
                return marker;
            }

            async Task<object> ProcessingWitness(int mark, BlockPos clicked, BlockPos neighbor, int clickedState, int neighborState)
            {
                await WaitAsync(() => capture.Since(mark).Any(IsUseOrAnimation), "native door use send", ct);
                CapturedFrame use = capture.Since(mark).First(IsUseOrAnimation);
                if (protocol >= 759)
                {
                    int sequence = Assert.IsType<ServerboundUseItemOnPacket>(use.Packet).Sequence;
                    Assert.True(sequence > previousSequence);
                    previousSequence = sequence;
                    await WaitAsync(() => capture.Since(mark).Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == sequence), "matching native door acknowledgment", ct);
                    CapturedFrame received = Assert.Single(capture.Since(mark), frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == sequence);
                    Assert.True(received.Milliseconds >= use.Milliseconds);
                    return new { kind = "matching native block-change acknowledgment", sequence, useMilliseconds = use.Milliseconds, processedMilliseconds = received.Milliseconds };
                }
                await WaitAsync(() => DoorProcessingPair(capture.Since(mark), clicked, neighbor, clickedState, neighborState) is not null, "ordered native clicked/neighbor reply pair", ct);
                int[] pair = Assert.IsType<int[]>(DoorProcessingPair(capture.Since(mark), clicked, neighbor, clickedState, neighborState));
                CapturedFrame[] frames = capture.Since(mark);
                return new { kind = "ordered native clicked/neighbor reply pair", clickedIndex = pair[0], neighborIndex = pair[1], useMilliseconds = use.Milliseconds, processedMilliseconds = frames[pair[1]].Milliseconds, note = "Door state mutation can publish additional identical updates; all are retained. The ordered pair is selected after this use, never from setup traffic." };
            }
        }
        finally
        {
            capture.Stop();
            client.PacketFrameObserved -= Observe;
            DoorFeatureFrame[] finalFeatures;
            lock (featureGate)
                finalFeatures = features.ToArray();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("door-native-features.json", finalFeatures);
            await server.WriteAsync("door-dispositions.json", dispositions);
            await server.WriteAsync("door-result.json", new { passed, release, protocol, capture.PayloadBytes, capture.Overflowed, featureBytes, featureOverflow });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static ChunkPos[] DoorFillColumns(int x, int z)
    {
        var columns = new List<ChunkPos>();
        for (int chunkX = (x - 4) >> 4; chunkX <= (x + 4) >> 4; chunkX++)
            for (int chunkZ = (z - 4) >> 4; chunkZ <= (z + 4) >> 4; chunkZ++)
                columns.Add(new ChunkPos(chunkX, chunkZ));
        return columns.ToArray();
    }

    private static DoorColumnWitness[] DoorFullColumnWitnesses(CapturedFrame[] frames)
    {
        var columns = new Dictionary<ChunkPos, int>();
        for (int index = 0; index < frames.Length; index++)
        {
            CapturedFrame frame = frames[index];
            if (frame.Flow != PacketFlow.Clientbound)
                continue;
            switch (frame.Packet)
            {
                case ClientboundLoginPacket or ClientboundRespawnPacket:
                    columns.Clear();
                    break;
                case ClientboundLevelChunkPacket chunk:
                    var position = new ChunkPos(chunk.ChunkX, chunk.ChunkZ);
                    bool legacyUnload = chunk.WireEra == ChunkWireEra.Legacy1_8 && chunk.FullChunk && chunk.RawBody is { Length: >= 11 } body && BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(9, 2)) == 0;
                    if (legacyUnload)
                        columns.Remove(position);
                    else if (chunk.FullChunk)
                        columns[position] = index;
                    break;
                case ClientboundMapChunkBulkPacket bulk:
                    foreach (var column in bulk.Columns)
                        columns[column.Position] = index;
                    break;
                case ClientboundForgetLevelChunkPacket forget:
                    columns.Remove(forget.Chunk);
                    break;
            }
        }
        return columns.OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Z).Select(pair => new DoorColumnWitness(pair.Key, pair.Value)).ToArray();
    }

    private static bool DoorHasFillTerrain(CapturedFrame[] frames, int x, int z)
    {
        HashSet<ChunkPos> loaded = DoorFullColumnWitnesses(frames).Select(witness => witness.Column).ToHashSet();
        return DoorFillColumns(x, z).All(loaded.Contains);
    }

    private static DoorLegacyInitialPacketEvidence DoorLegacyInitialPackets(CapturedFrame[] frames)
    {
        int loginIndex = Array.FindIndex(frames, frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundLoginPacket);
        int abilitiesIndex = Array.FindIndex(frames, frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundPlayerAbilitiesPacket);
        Assert.True(loginIndex >= 0 && abilitiesIndex > loginIndex, "Actual initial Login and later abilities packets are required before preparation.");
        var login = Assert.IsType<ClientboundLoginPacket>(frames[loginIndex].Packet);
        var abilities = Assert.IsType<ClientboundPlayerAbilitiesPacket>(frames[abilitiesIndex].Packet);
        Assert.InRange(login.SpawnInfo.GameType, 0, 3);
        return new DoorLegacyInitialPacketEvidence(loginIndex, abilitiesIndex, login.SpawnInfo.GameType, abilities.Flags, abilities.FlyingSpeed, abilities.WalkingSpeed);
    }

    private static DoorLegacySetupPacketEvidence DoorLegacySetupPackets(CapturedFrame[] frames, int preparationModeStartMark)
    {
        Assert.InRange(preparationModeStartMark, 1, frames.Length - 1);
        DoorLegacyInitialPacketEvidence initial = DoorLegacyInitialPackets(frames[..preparationModeStartMark]);
        int modeIndex = Array.FindLastIndex(frames, frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundGameEventPacket { Event: 3 });
        int abilitiesIndex = Array.FindLastIndex(frames, frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundPlayerAbilitiesPacket);
        Assert.True(modeIndex >= preparationModeStartMark && abilitiesIndex >= preparationModeStartMark, "The explicit preparation mode requires actual game-event and abilities frames after its command.");
        Assert.Equal(1, Assert.IsType<ClientboundGameEventPacket>(frames[modeIndex].Packet).Param);
        var abilities = Assert.IsType<ClientboundPlayerAbilitiesPacket>(frames[abilitiesIndex].Packet);
        Assert.Equal(13, abilities.Flags & 13);
        return new DoorLegacySetupPacketEvidence(initial, preparationModeStartMark, modeIndex, abilitiesIndex, abilities.Flags, abilities.FlyingSpeed, abilities.WalkingSpeed);
    }

    private static DoorLegacyPrimerProof DoorLegacyPrimerFrames(CapturedFrame[] frames, int preparationModeStartMark, int primerStartMark, int terrainEndMark, int x, int z)
    {
        Assert.InRange(primerStartMark, 1, frames.Length);
        Assert.InRange(terrainEndMark, primerStartMark + 1, frames.Length);
        DoorLegacySetupPacketEvidence setup = DoorLegacySetupPackets(frames[..primerStartMark], preparationModeStartMark);
        CapturedFrame[] beforeFill = frames[..terrainEndMark];
        Assert.True(DoorHasFillTerrain(beforeFill, x, z), "Every fill column needs current full-chunk packet evidence before either fill.");
        int teleportIndex = Array.FindIndex(beforeFill, primerStartMark, frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundPlayerPositionPacket position && (position.RelativeFlags & 7) == 0 && position.X == x + .5 && position.Y == 100 && position.Z == z + .5);
        Assert.True(teleportIndex >= primerStartMark, "The initial native teleport packet is required before filling the fixed destination.");
        ChunkPos[] required = DoorFillColumns(x, z);
        DoorColumnWitness[] witnesses = DoorFullColumnWitnesses(beforeFill).Where(witness => required.Contains(witness.Column)).ToArray();
        Assert.Equal(required, witnesses.Select(witness => witness.Column));
        return new DoorLegacyPrimerProof(preparationModeStartMark, primerStartMark, terrainEndMark, setup, teleportIndex, required, witnesses);
    }

    private static void DoorAssertLegacySurvivalPackets(CapturedFrame[] frames, int terrainEndMark)
    {
        int modeIndex = Array.FindLastIndex(frames, frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundGameEventPacket { Event: 3 });
        int abilitiesIndex = Array.FindLastIndex(frames, frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundPlayerAbilitiesPacket);
        Assert.True(modeIndex >= terrainEndMark && abilitiesIndex >= terrainEndMark, "Actual Survival game-event and abilities frames are required after terrain preparation and before actions.");
        Assert.Equal(0, Assert.IsType<ClientboundGameEventPacket>(frames[modeIndex].Packet).Param);
        Assert.Equal(0, Assert.IsType<ClientboundPlayerAbilitiesPacket>(frames[abilitiesIndex].Packet).Flags & 13);
    }

    private static void DoorAssertLegacyPrimerCommands(JsonElement commands, int protocol, int x, int z)
    {
        int after = -1;
        JsonElement Next(string command, bool console)
        {
            JsonElement[] rows = commands.EnumerateArray().ToArray();
            int index = Array.FindIndex(rows, after + 1, row => row.GetProperty("command").GetString() == command);
            Assert.True(index > after, "Missing or out-of-order native primer command: " + command);
            after = index;
            JsonElement row = rows[index];
            if (console)
            {
                Assert.Equal("console", row.GetProperty("channel").GetString());
                Assert.Contains(row.GetProperty("completionMarker").GetString()!, row.GetProperty("response").GetString()!, StringComparison.Ordinal);
            }
            else
                Assert.Equal(0, row.GetProperty("exitCode").GetInt32());
            return row;
        }
        string teleport = $"tp {Username} {x + .5:R} 100 {z + .5:R}";
        Next($"gamemode creative {Username}", console: true);
        Next(teleport, console: true);
        foreach (string command in new[] { $"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", $"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air" })
        {
            string response = Next(command, console: true).GetProperty("response").GetString()!;
            Assert.Matches(@"\b(?:\d+|No) blocks filled\b", response);
            Assert.DoesNotContain("Cannot place blocks outside of the world", response, StringComparison.Ordinal);
        }
        foreach ((int y, string block) in new[] { (99, "stone"), (100, "air"), (101, "air") })
        {
            string response = Next($"testforblock {x} {y} {z} minecraft:{block}", console: false).GetProperty("response").GetString()!;
            Assert.Equal(protocol >= 210 ? $"Successfully found the block at {x}, {y}, {z}" : $"Successfully found the block at {x},{y},{z}.", response.Trim());
        }
        Next(teleport, console: true);
        Next($"gamemode survival {Username}", console: true);
        var groundedMarkers = new HashSet<string>(StringComparer.Ordinal);
        bool grounded;
        do
        {
            Assert.True(after + 1 < commands.GetArrayLength(), "Native grounded proof never converged before scenario commands.");
            JsonElement attempt = commands[++after];
            grounded = DoorLegacyGroundedCommandPassed(attempt, x, z);
            Assert.True(groundedMarkers.Add(attempt.GetProperty("completionMarker").GetString()!), "A grounded attempt reused a native completion marker.");
        }
        while (!grounded);
    }

    private sealed record DoorColumnWitness(ChunkPos Column, int CaptureIndex);
    private sealed record DoorLegacyInitialPacketEvidence(int LoginCaptureIndex, int AbilitiesCaptureIndex, int GameType, byte Flags, float FlyingSpeed, float WalkingSpeed);
    private sealed record DoorLegacySetupPacketEvidence(DoorLegacyInitialPacketEvidence Initial, int PreparationModeStartMark, int GameModeCaptureIndex, int AbilitiesCaptureIndex, byte Flags, float FlyingSpeed, float WalkingSpeed);
    private sealed record DoorLegacyPrimerProof(int PreparationModeStartMark, int PrimerStartMark, int TerrainEndMark, DoorLegacySetupPacketEvidence Setup, int TeleportCaptureIndex, IReadOnlyList<ChunkPos> RequiredColumns, IReadOnlyList<DoorColumnWitness> FullColumnWitnesses);

    private static async Task DoorWaitLegacyGroundedAsync(VanillaInteractionAcceptanceHarness server, int x, int z, CancellationToken ct)
    {
        // RCON reads native player state on another thread. Preserve the exact predicate and require native console-owned convergence before any scenario.
        using var ready = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ready.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            await server.ConsoleAsync(DoorLegacyGroundedPredicate(x, z), ready.Token);
            using JsonDocument commands = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(server.DirectoryPath, "commands.json"), ready.Token));
            if (DoorLegacyGroundedCommandPassed(commands.RootElement.EnumerateArray().Last(), x, z))
                return;
            await Task.Delay(25, ready.Token);
        }
    }

    private static bool DoorLegacyGroundedCommandPassed(JsonElement row, int x, int z)
    {
        Assert.Equal(DoorLegacyGroundedPredicate(x, z), row.GetProperty("command").GetString());
        Assert.Equal("console", row.GetProperty("channel").GetString());
        string marker = row.GetProperty("completionMarker").GetString()!;
        Assert.Matches("^interaction-console-[0-9a-f]{32}$", marker);
        string response = row.GetProperty("response").GetString()!;
        string[] messages = System.Text.RegularExpressions.Regex.Matches(response, @"(?m)^\[\d{2}:\d{2}:\d{2}\] \[Server thread/INFO\]: (?<message>[^\r\n]*)\r?$")
            .Select(match => match.Groups["message"].Value).ToArray();
        string found = "Found " + Username;
        string notGrounded = Username + " did not match the required data structure";
        string result = Assert.Single(messages, message => message == found || message == notGrounded);
        string completed = "[Server] " + marker;
        Assert.Single(messages, message => message == completed);
        Assert.True(Array.IndexOf(messages, completed) > Array.IndexOf(messages, result), "The native completion marker preceded the grounded result.");
        return result == found;
    }

    private static string DoorLegacyGroundedPredicate(int x, int z)
    {
        Assert.Equal((64, 192), (x, z));
        // Native list matching is unordered. Distinct, separated cells reject every coordinate permutation before exact NBT doubles establish the intended position.
        return $"testfor @a[name={Username},x=64,y=100,z=192,dx=0,dy=0,dz=0] {{OnGround:1b,Pos:[64.5d,100.0d,192.5d]}}";
    }

    private sealed record DoorFeatureFrame(ProtocolPhase Phase, PacketFlow Flow, int WireId, byte[] Payload, IReadOnlyList<Identifier> Features);
    private static DoorControlledWolfSnapshot ReadDoorControlledWolf(UmpkClient client, int id, Guid uuid, BlockPos clicked, Direction face, Vec3d cursor, Hand hand)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Entity wolf = Assert.IsType<Entity>(client.State.Entities.Get(id));
        Assert.Equal(uuid, wolf.Uuid);
        Assert.Equal(Identifier.Minecraft("wolf"), wolf.Type.Id);
        Assert.False(JavaGameData.BlockInteractionData(315).IsAlwaysNonObstructingEntity(wolf.Type.Id));
        Assert.False(JavaGameData.BlockInteractionData(315).TryGetFixedEntityDimensions(wolf.Type.Id, out _, out _));
        Assert.False(JavaGameData.EntityGeometryData(315).TryGetRule(wolf.Type.Id, out _));
        object interaction = client.Actions.Interaction;
        object prediction = interaction.GetType().GetMethod("PredictBlockUse", members)!.Invoke(interaction, [clicked, face, cursor, hand])!;
        object geometry = typeof(ClientState).GetProperty("EntityGeometry", members)!.GetValue(client.State)!;
        object?[] dimensions = [wolf, null];
        bool exactBodyKnown = (bool)geometry.GetType().GetMethod("TryGetDimensions", members)!.Invoke(geometry, dimensions)!;
        object motion = typeof(ClientState).GetProperty("EntityMotion", members)!.GetValue(client.State)!;
        object ambient = motion.GetType().GetField("_legacyAmbient", members)!.GetValue(motion)!;
        object?[] ambientValue = [wolf, null];
        bool ambientController = (bool)ambient.GetType().GetMethod("TryGetValue")!.Invoke(ambient, ambientValue)!;
        bool ordinaryLivingController = (bool)motion.GetType().GetMethod("IsTrackedOrdinaryLiving", members)!.Invoke(motion, [wolf])!;
        object membership = typeof(ClientState).GetProperty("EntityMembership", members)!.GetValue(client.State)!;
        bool member = (bool?)membership.GetType().GetMethod("IsWorldQueryMember", members)!.Invoke(membership, [wolf]) == true;
        BlockPos destination = clicked.Offset(face);
        bool excluded = (bool)geometry.GetType().GetMethod("IsProvenPlacementNonCandidate", members)!.Invoke(geometry, [wolf, Aabb.BlockAt(destination.X, destination.Y, destination.Z)])!;
        return new(id, uuid, wolf.Position, member, exactBodyKnown, ambientController, ordinaryLivingController, excluded, (bool)prediction.GetType().GetProperty("SendPacket")!.GetValue(prediction)!, prediction.GetType().GetProperty("Result")!.GetValue(prediction)!.ToString()!, prediction.GetType().GetProperty("Swing")!.GetValue(prediction)!.ToString()!);
    }

    private sealed record DoorControlledWolfSnapshot(int EntityId, Guid Uuid, Vec3d ObservedPosition, bool Member, bool ExactBodyKnown, bool AmbientController, bool OrdinaryLivingController, bool EnvelopeExcluded, bool SendPacket, string Result, string Swing);

    private sealed record DoorScenario(string Id, string Block, bool Upper, bool InitialOpen, bool FinalOpen, Hand Hand, bool Sneak, string? Main, string? Off, bool PacketSwing, bool PlaceStone = false, bool RejectHand = false, string? RequiredFeature = null);

    private static DoorScenario[] DoorScenarios(int protocol)
    {
        Assert.Contains(protocol, new[] { 47, 315, 393, 765, 766, 777 });
        string wood = protocol < 393 ? "wooden_door" : "oak_door";
        bool remoteSwing = protocol != 777;
        var rows = new List<DoorScenario>
        {
            new("wood-lower-empty", wood, false, false, true, Hand.Main, false, null, null, remoteSwing),
            new("wood-upper-empty", wood, true, false, true, Hand.Main, false, null, null, remoteSwing),
            new("wood-lower-open", wood, false, true, false, Hand.Main, false, null, null, remoteSwing),
            new("wood-held-stone-consumed", wood, false, false, true, Hand.Main, false, "stone", null, remoteSwing),
            new("wood-sneak-empty", wood, false, false, true, Hand.Main, true, null, null, remoteSwing),
            new("wood-sneak-stick-pass", wood, false, false, false, Hand.Main, true, "stick", null, false),
            new("iron-lower-empty", "iron_door", false, false, false, Hand.Main, false, null, null, protocol == 47),
            new("iron-upper-empty", "iron_door", true, false, false, Hand.Main, false, null, null, protocol == 47),
            new("iron-held-stone", "iron_door", false, false, false, Hand.Main, false, "stone", null, remoteSwing, PlaceStone: protocol != 47),
            new("wood-offhand-empty", wood, false, false, protocol is > 47 and < 766, Hand.Off, false, null, null, protocol is > 47 and < 766, RejectHand: protocol == 47),
        };
        if (protocol != 47)
            rows.Add(new("wood-sneak-offhand-stick", wood, false, false, false, Hand.Main, true, null, "stick", false));
        if (protocol >= 765)
        {
            string? flag = protocol < 767 ? "update_1_21" : null;
            rows.Add(new("copper-lower-empty", "waxed_weathered_copper_door", false, false, true, Hand.Main, false, null, null, remoteSwing, RequiredFeature: flag));
            rows.Add(new("copper-before-axe", "waxed_weathered_copper_door", false, false, true, Hand.Main, false, "diamond_axe", null, remoteSwing, RequiredFeature: flag));
        }
        return rows.ToArray();
    }

    private static bool DoorRecordingFits(long playBytes, long featureBytes, int nextBytes) => playBytes >= 0 && featureBytes >= 0 && nextBytes >= 0 && playBytes <= 20 * 1024 * 1024 - featureBytes && nextBytes <= 20 * 1024 * 1024 - featureBytes - playBytes;

    private static int DoorCount(string? item) => item is null ? 0 : item == "stone" ? 8 : 1;
    private static bool DoorStackMatches(ItemStack stack, string? item, int count) => item is null ? stack.IsEmpty : !stack.IsEmpty && stack.Item.Id == Identifier.Minecraft(item) && stack.Count == count;
    private static bool DoorFeatureObservationMatches(ProtocolPhase phase, PacketFlow flow, object? packet) => phase == ProtocolPhase.Configuration && flow == PacketFlow.Clientbound && packet is ClientboundConfigUpdateEnabledFeaturesPacket;

    private static bool DoorStateMatches(BlockState state, int protocol, string block, bool upper, bool open) => state.IsValid && state.Block.Id == Identifier.Minecraft(block) && (protocol < 393 ? (state.LegacyMeta & 8) != 0 == upper && (upper || ((state.LegacyMeta & 4) != 0) == open) : state.TryGetProperty("half", out string half) && half == (upper ? "upper" : "lower") && state.TryGetProperty("open", out string value) && value == (open ? "true" : "false"));

    private static int DoorStateId(RegistryAccess registries, int protocol, string block, bool upper, bool open)
    {
        BlockDefinition definition = registries.Blocks[Identifier.Minecraft(block)].Value;
        if (protocol < 393)
            return definition.DefaultStateId | (upper ? 8 : open ? 4 : 0);
        return Enumerable.Range(definition.MinStateId, definition.StateCount).Single(id => definition.TryGetPropertyValue(id, "half", out string half) && half == (upper ? "upper" : "lower") && definition.TryGetPropertyValue(id, "open", out string value) && value == (open ? "true" : "false") && definition.TryGetPropertyValue(id, "facing", out string facing) && facing == "north" && definition.TryGetPropertyValue(id, "hinge", out string hinge) && hinge == "left" && definition.TryGetPropertyValue(id, "powered", out string powered) && powered == "false");
    }

    private static int[]? DoorProcessingPair(CapturedFrame[] frames, BlockPos clicked, BlockPos neighbor, int clickedState, int neighborState)
    {
        int use = Array.FindIndex(frames, IsUseOrAnimation);
        if (use < 0)
            return null;
        int first = -1;
        for (int index = use + 1; index < frames.Length; index++)
        {
            if (frames[index].Flow != PacketFlow.Clientbound || frames[index].Packet is not ClientboundBlockUpdatePacket block)
                continue;
            if (block.Position == clicked && block.BlockStateId == clickedState)
                first = index;
            if (first >= 0 && block.Position == neighbor && block.BlockStateId == neighborState)
                return [first, index];
        }
        return null;
    }

    private static void AssertDoorActions(CapturedFrame[] frames, int protocol, BlockPos clicked, Direction face, Vec3d cursor, Hand hand, string? held, bool packetSwing, bool rejectHand)
    {
        Assert.DoesNotContain(frames, frame => frame.Flow == PacketFlow.Serverbound && frame.Packet is ServerboundInteractPacket or ServerboundAttackPacket or ServerboundPlayerActionPacket);
        CapturedFrame[] actions = frames.Where(IsUseOrAnimation).ToArray();
        Assert.Equal(rejectHand ? 0 : packetSwing ? 2 : 1, actions.Length);
        if (rejectHand)
            return;
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(actions[0].Packet);
            Assert.Equal(clicked, use.Position);
            Assert.Equal((int)face, use.Face);
            Assert.Equal(held is null ? default : Identifier.Minecraft(held), use.HeldItem.Item.Id);
            Assert.Equal(DoorCount(held), use.HeldItem.Count);
            Assert.Equal((byte)(cursor.X * 16), use.CursorX);
            Assert.Equal((byte)(cursor.Y * 16), use.CursorY);
            Assert.Equal((byte)(cursor.Z * 16), use.CursorZ);
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
            Assert.Equal(clicked, use.Position);
            Assert.Equal((int)face, use.Face);
            Assert.Equal((int)hand, use.Hand);
            Assert.Equal((float)cursor.X, use.CursorX);
            Assert.Equal((float)cursor.Y, use.CursorY);
            Assert.Equal((float)cursor.Z, use.CursorZ);
            Assert.False(use.Inside);
            Assert.False(use.WorldBorderHit);
            if (protocol >= 759)
                Assert.True(use.Sequence > 0);
        }
        if (packetSwing)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
    }

    private static int ReplayDoorFeatures(DoorFeatureFrame[] frames, JavaVersion version, RegistryAccess registries)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        foreach (DoorFeatureFrame frame in frames)
        {
            Assert.Equal(ProtocolPhase.Configuration, frame.Phase);
            Assert.Equal(PacketFlow.Clientbound, frame.Flow);
            PhaseRegistry registry = version.Protocol.GetRegistry(frame.Phase, frame.Flow);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            Assert.Equal(Identifier.Minecraft("update_enabled_features"), codec.Type.Id);
            var packet = Assert.IsType<ClientboundConfigUpdateEnabledFeaturesPacket>(codec.Decode(frame.Payload, context));
            Assert.Equal(frame.Features, packet.Features);
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, packet, context);
            Assert.Equal(frame.Payload, output.WrittenSpan.ToArray());
        }
        return frames.Length;
    }
}
