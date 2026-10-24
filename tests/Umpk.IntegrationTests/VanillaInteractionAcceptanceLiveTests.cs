using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.IntegrationTests;

/// <summary>Direct survival interaction acceptance with physics, independent vanilla commands, actual outbound frames and retained cleanup evidence.</summary>
[Collection(LiveServerCollection.Name)]
public sealed partial class VanillaInteractionAcceptanceLiveTests(ITestOutputHelper output)
{
    private const string Username = "InteractionTest";

    public static IEnumerable<object[]> Releases()
    {
        string requested = Environment.GetEnvironmentVariable("UMPK_INTERACTION_RELEASES") ?? "1.8.9,26.3";
        string[] releases;
        if (requested == "all")
        {
            using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturePaths.RepoRoot(), "data", "java", "versions.json")));
            releases = catalog.RootElement.GetProperty("versions").EnumerateArray().Select(row => row.GetProperty("name").GetString()!).ToArray();
            Assert.Equal(75, releases.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(75, releases.Length);
        }
        else
            releases = requested.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (string release in releases)
        {
            Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version), $"Unknown supported release {release}.");
            yield return [release, version.Version.Protocol];
        }
    }

    [NightlyTheory]
    [MemberData(nameof(Releases))]
    public async Task SurvivalMiningAndPlacementHaveNativePacketsAndIndependentServerOutcomes(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        using var logs = new CapturingLoggerFactory(capacity: 1000);
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), Username))
            .UseStaticRegistries(JavaGameData.Registries(protocol))
            .UseLoggerFactory(logs)
            .Build();
        var capture = new Capture(version);
        client.PacketFrameObserved += capture.Observe;
        bool passed = false;
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "initial terrain", ct);
            Assert.True(client.State.Features.Physics, "This leg must exercise the real physics owner.");
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = (int)Math.Floor(spawn.X);
            int z = (int)Math.Floor(spawn.Z);
            var target = new BlockPos(x + 2, 100, z);
            var support = target.Below();
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobSpawning false" : "gamerule minecraft:spawn_mobs false", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobLoot false" : "gamerule minecraft:mob_drops false", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync($"tp {Username} {x + 0.5:R} 100 {z + 0.5:R}", ct);
            await server.ConsoleAsync(protocol < 393 ? "kill @e[type=!Player]" : "kill @e[type=!minecraft:player]", ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && c.State.Self.OnGround
                && Math.Abs(c.State.Self.Position.X - (x + 0.5)) < 0.01 && Math.Abs(c.State.Self.Position.Y - 100) < 0.01
                && c.State.World.GetColumn(target) is not null && c.State.World.GetBlock(support).Block.Id == Identifier.Minecraft("stone"), "survival platform and grounded physics", ct);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            capture.Start();

            await server.ConsoleAsync($"clear {Username}", ct);
            await server.ConsoleAsync($"give {Username} minecraft:diamond_pickaxe 1", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => !c.State.Inventory.GetSlot(36).IsEmpty && c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("diamond_pickaxe"), "held pickaxe", ct);
            await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:stone", ct);
            await WaitAsync(client, c => c.State.World.GetBlock(target).Block.Id == Identifier.Minecraft("stone"), "mining target", ct);
            await AssertBlockAsync(server, protocol, target, "stone", ct);
            int miningStart = capture.Mark();
            await client.Actions.Interaction.DigBlockAsync(target, Direction.West, ct);
            await WaitAsync(client, c => c.State.World.GetBlock(target).IsAir, "mined target update", ct);
            await AssertBlockAsync(server, protocol, target, "air", ct);
            await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
            CapturedFrame[] mining = capture.Since(miningStart);
            AssertMining(mining, protocol, target);

            await server.ConsoleAsync($"clear {Username}", ct);
            await server.ConsoleAsync($"give {Username} minecraft:stone 32", ct);
            await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("stone") && c.State.Inventory.GetSlot(36).Count == 32, "placement stack", ct);
            await AssertCountAsync(server, protocol, 32, ct);
            await AssertBlockAsync(server, protocol, target, "air", ct);
            await server.WriteAsync("placement-inputs.json", await client.InvokeAsync(c => VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, support, target), ct));
            int placementStart = capture.Mark();
            await client.Actions.Interaction.PlaceBlockAsync(support, Direction.Up, new Vec3d(0.5, 1, 0.5), ct: ct);
            await WaitAsync(client, c => c.State.World.GetBlock(target).Block.Id == Identifier.Minecraft("stone") && c.State.Inventory.GetSlot(36).Count == 31, "placement block and inventory updates", ct);
            await AssertBlockAsync(server, protocol, target, "stone", ct);
            await AssertCountAsync(server, protocol, 31, ct);
            await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
            CapturedFrame[] placement = capture.Since(placementStart);
            AssertPlacement(placement, protocol, support);

            int rejectedStart = capture.Mark();
            await client.Actions.Interaction.PlaceBlockAsync(support, Direction.Up, new Vec3d(0.5, 1, 0.5), ct: ct);
            await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
            if (protocol < 393)
                Assert.DoesNotContain(capture.Since(rejectedStart), IsUseOrAnimation);
            else
                AssertUsePacket(Assert.Single(capture.Since(rejectedStart), IsUseOrAnimation), protocol, support);
            await AssertBlockAsync(server, protocol, target, "stone", ct);
            await AssertCountAsync(server, protocol, 31, ct);

            await server.ConsoleAsync($"clear {Username}", ct);
            await server.ConsoleAsync($"give {Username} minecraft:diamond_pickaxe 1", ct);
            await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("diamond_pickaxe"), "unbreakable mining pickaxe", ct);
            await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:bedrock", ct);
            await WaitAsync(client, c => c.State.World.GetBlock(target).Block.Id == Identifier.Minecraft("bedrock"), "unbreakable target", ct);
            await AssertBlockAsync(server, protocol, target, "bedrock", ct);
            int unbreakableStart = capture.Mark();
            using (var heldAttack = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                Task heldMining = client.Actions.Interaction.DigBlockAsync(target, Direction.West, heldAttack.Token);
                try
                {
                    await WaitAsync(() => capture.Since(unbreakableStart).Count(frame => frame.Flow == PacketFlow.Serverbound && frame.Packet is ServerboundSwingPacket or ServerboundPunchPacket) >= 3, "held unbreakable animations", ct);
                    Assert.False(heldMining.IsCompleted, "Bedrock must not complete survival mining.");
                }
                finally
                {
                    await heldAttack.CancelAsync();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => heldMining.WaitAsync(TimeSpan.FromSeconds(5)));
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
            CapturedFrame[] unbreakable = capture.Since(unbreakableStart).Where(IsMiningOrAnimation).ToArray();
            var destruction = unbreakable.Where(frame => frame.Packet is ServerboundPlayerActionPacket).ToArray();
            Assert.Equal(2, destruction.Length);
            Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(destruction[0].Packet).Action);
            var abort = Assert.IsType<ServerboundPlayerActionPacket>(unbreakable[^1].Packet);
            Assert.Equal(protocol == 777 ? 2 : 1, abort.Action);
            Assert.Equal(target, abort.Position);
            await AssertBlockAsync(server, protocol, target, "bedrock", ct);
            int replayed = capture.AssertRoundTrips(client.State.Registries ?? JavaGameData.Registries(protocol));
            Assert.Equal(ClientStatus.Playing, client.Status);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "server-observed disconnect", ct);
            await server.WriteAsync("acceptance.json", new { release, protocol, clientProfile = version.Version.Name, physics = true, mining = "server air and seven ordered animations", placement = "server stone and 32-to-31 stack", rejectedPlacement = protocol < 393 ? "occupied stone remains and stack stays31 with no use or swing" : "occupied stone remains and stack stays31 with use and no swing", unbreakableMining = "server bedrock survives held attack and ordered cancellation", replayed, peerObservedDisconnect = true });
            passed = true;
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("client-logs.json", logs.Entries);
            await server.WriteAsync("result.json", new { passed, release, protocol, capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    [NightlyTheory]
    [InlineData("1.8.9", 47)]
    [InlineData("1.9", 107)]
    [InlineData("1.13", 393)]
    [InlineData("1.14.3", 490)]
    [InlineData("1.14.4", 498)]
    [InlineData("1.17", 755)]
    [InlineData("26.2", 776)]
    public async Task HoeUseMatchesNativeTerrainDurabilityAndSwing(string release, int protocol)
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
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "hoe terrain", ct);
            Assert.True(client.State.Features.Physics);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = (int)Math.Floor(spawn.X), z = (int)Math.Floor(spawn.Z);
            var target = new BlockPos(x + 2, 100, z);
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync($"tp {Username} {x + 0.5:R} 100 {z + 0.5:R}", ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await server.ConsoleAsync($"clear {Username}", ct);
            await server.ConsoleAsync($"give {Username} minecraft:diamond_hoe 1", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && c.State.Self.OnGround && Math.Abs(c.State.Self.Position.Y - 100) < .01 && c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("diamond_hoe"), "grounded hoe owner and inventory", ct);
            capture.Start();

            await Use("dirt", "farmland", Direction.Up, blockedAbove: false, success: true, damage: 1);
            await Use("grass_block", "farmland", Direction.Up, blockedAbove: false, success: true, damage: 2);
            await Use("coarse_dirt", "dirt", Direction.Up, blockedAbove: false, success: true, damage: 3);
            await Use("dirt", "dirt", Direction.Down, blockedAbove: false, success: false, damage: 3);
            await Use("dirt", "dirt", Direction.Up, blockedAbove: true, success: false, damage: 3);
            if (protocol >= 755)
                await Use("rooted_dirt", "dirt", Direction.Down, blockedAbove: true, success: true, damage: 4);
            int replayed = capture.AssertRoundTrips(client.State.Registries!);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "hoe server-observed disconnect", ct);
            await server.WriteAsync("hoe-acceptance.json", new { release, protocol, physics = true, dirtGrassAndCoarse = true, wornTool = true, downwardAndCoveredDirtPass = true, rootedDirt = protocol >= 755, replayed, peerObservedDisconnect = true });
            passed = true;

            async Task Use(string before, string after, Direction face, bool blockedAbove, bool success, int damage)
            {
                await server.ConsoleAsync($"setblock {target.X} {target.Y + 1} {target.Z} minecraft:{(blockedAbove ? "stone" : "air")}", ct);
                string nativeTarget = NativeBlockName(before) + (protocol < 393 && before == "coarse_dirt" ? " 1" : "");
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:{nativeTarget}", ct);
                await WaitAsync(client, c => HasBlock(c, before) && c.State.World.GetBlock(target.Above()).Block.Id == Identifier.Minecraft(blockedAbove ? "stone" : "air"), "hoe precondition updates", ct);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(target, face, new Vec3d(.5, 1, .5), ct: ct);
                await WaitAsync(client, c => HasBlock(c, after) && Damage(c.State.Inventory.GetSlot(36)) == damage, "hoe result and native durability", ct);
                await AssertBlockAsync(server, protocol, target, NativeBlockName(after), ct, protocol < 393 && after == "dirt" ? 0 : null);
                if (protocol < 393)
                {
                    string response = await server.CommandAsync($"testfor @a[name={Username}] {{SelectedItem:{{id:\"minecraft:diamond_hoe\",Damage:{damage}s}}}}", ct);
                    Assert.Equal("Found " + Username, response);
                }
                else
                {
                    string marker = "hoe-damage-" + Guid.NewGuid().ToString("N");
                    string predicate = protocol < 766
                        ? "execute if entity @a[name=" + Username + ",nbt={SelectedItem:{id:\"minecraft:diamond_hoe\",tag:{Damage:" + damage + "}}}]"
                        : $"execute if items entity {Username} weapon.mainhand minecraft:diamond_hoe[minecraft:damage={damage}]";
                    await server.CommandAsync(predicate + " run say " + marker, ct);
                    await WaitAsync(() => server.ServerLog.Contains("[Rcon] " + marker, StringComparison.Ordinal), "independent server durability predicate", ct);
                }
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                CapturedFrame[] frames = capture.Since(mark).Where(IsUseOrAnimation).ToArray();
                Assert.Equal(success ? 2 : 1, frames.Length);
                if (protocol == 47)
                {
                    var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(frames[0].Packet);
                    Assert.Equal(target, use.Position);
                    Assert.Equal((int)face, use.Face);
                    Assert.Equal(Identifier.Minecraft("diamond_hoe"), use.HeldItem.Item.Id);
                    Assert.Equal((byte)8, use.CursorX);
                    Assert.Equal((byte)16, use.CursorY);
                    Assert.Equal((byte)8, use.CursorZ);
                }
                else
                {
                    var use = Assert.IsType<ServerboundUseItemOnPacket>(frames[0].Packet);
                    Assert.Equal(target, use.Position);
                    Assert.Equal((int)face, use.Face);
                    Assert.Equal(0, use.Hand);
                }
                if (success)
                    Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(frames[1].Packet).Hand);
            }

            int Damage(ItemStack stack) => protocol < 393 || protocol >= 766 ? stack.Damage : stack.Components.Get(DataComponents.LegacyNbt)?.Nbt.GetInt("Damage") ?? 0;

            string NativeBlockName(string block) => protocol >= 393 ? block : block switch
            {
                "grass_block" => "grass",
                "coarse_dirt" => "dirt",
                _ => block,
            };

            bool HasBlock(UmpkClient owner, string block)
            {
                var state = owner.State.World.GetBlock(target);
                return state.IsValid && state.Block.Id == Identifier.Minecraft(NativeBlockName(block))
                    && (protocol >= 393 || block is not ("dirt" or "coarse_dirt") || state.LegacyMeta == (block == "coarse_dirt" ? 1 : 0));
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "hoe use", capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static async Task AssertBlockAsync(VanillaInteractionAcceptanceHarness server, int protocol, BlockPos position, string block, CancellationToken ct, int? legacyData = null)
    {
        string command = protocol < 393
            ? $"testforblock {position.X} {position.Y} {position.Z} minecraft:{block}" + (legacyData is int value ? $" {value}" : "")
            : $"execute if block {position.X} {position.Y} {position.Z} minecraft:{block}";
        string response = await server.CommandAsync(command, ct);
        string expected = protocol >= 393 ? "Test passed"
            : protocol >= 210 ? $"Successfully found the block at {position.X}, {position.Y}, {position.Z}"
            : $"Successfully found the block at {position.X},{position.Y},{position.Z}.";
        Assert.Equal(expected, response);
    }

    private static async Task AssertCountAsync(VanillaInteractionAcceptanceHarness server, int protocol, int count, CancellationToken ct)
    {
        string command = protocol < 393 ? $"clear {Username} minecraft:stone -1 0" : $"clear {Username} minecraft:stone 0";
        string response = await server.CommandAsync(command, ct);
        string[] expected = protocol < 393
            ? [$"{Username} has {count} items that match the criteria"]
            : [$"Found {count} matching items on player {Username}", $"Found {count} matching item(s) on player {Username}"];
        Assert.Contains(response, expected);
    }

    private static void AssertMining(CapturedFrame[] frames, int protocol, BlockPos target)
    {
        CapturedFrame[] actions = frames.Where(IsMiningOrAnimation).ToArray();
        Assert.Equal(9, actions.Length);
        Assert.Equal(7, actions.Count(frame => frame.Packet is ServerboundSwingPacket or ServerboundPunchPacket));
        int startIndex = protocol == 47 ? 1 : 0;
        var start = Assert.IsType<ServerboundPlayerActionPacket>(actions[startIndex].Packet);
        Assert.Equal(0, start.Action);
        Assert.Equal(target, start.Position);
        Assert.Equal((byte)Direction.West, start.Direction);
        var finish = Assert.IsType<ServerboundPlayerActionPacket>(actions[^2].Packet);
        Assert.Equal(protocol == 777 ? 3 : 2, finish.Action);
        Assert.Equal(target, finish.Position);
        Assert.Equal((byte)Direction.West, finish.Direction);
        foreach (CapturedFrame animation in actions.Where(frame => frame.Packet is ServerboundSwingPacket or ServerboundPunchPacket))
        {
            if (protocol == 777)
                Assert.IsType<ServerboundPunchPacket>(animation.Packet);
            else
                Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(animation.Packet).Hand);
        }
        Assert.True(actions[^1].Packet is ServerboundSwingPacket or ServerboundPunchPacket);
        Assert.True(actions[^2].Milliseconds - actions[startIndex].Milliseconds >= 150, "Survival stone must accumulate progress over several real client ticks.");
        Assert.True(frames[^1].Milliseconds - actions[^1].Milliseconds >= 900, "The capture must continue after the action.");
    }

    private static void AssertPlacement(CapturedFrame[] frames, int protocol, BlockPos support)
    {
        CapturedFrame[] actions = frames.Where(IsUseOrAnimation).ToArray();
        Assert.Equal(protocol == 777 ? 1 : 2, actions.Length);
        AssertUsePacket(actions[0], protocol, support);
        if (protocol != 777)
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
        Assert.True(frames[^1].Milliseconds - actions[0].Milliseconds >= 900, "The capture must continue after the use packet.");
    }

    private static bool IsMiningOrAnimation(CapturedFrame frame) => frame.Flow == PacketFlow.Serverbound && frame.Packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket;
    private static bool IsUseOrAnimation(CapturedFrame frame) => frame.Flow == PacketFlow.Serverbound && frame.Packet is ServerboundLegacyBlockPlacePacket or ServerboundUseItemOnPacket or ServerboundSwingPacket or ServerboundPunchPacket;

    private static void AssertUsePacket(CapturedFrame frame, int protocol, BlockPos support)
    {
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(frame.Packet);
            Assert.Equal(support, use.Position);
            Assert.Equal((int)Direction.Up, use.Face);
            Assert.Equal(Identifier.Minecraft("stone"), use.HeldItem.Item.Id);
            Assert.Equal((byte)8, use.CursorX);
            Assert.Equal((byte)16, use.CursorY);
            Assert.Equal((byte)8, use.CursorZ);
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(frame.Packet);
            Assert.Equal(support, use.Position);
            Assert.Equal((int)Direction.Up, use.Face);
            Assert.Equal(0, use.Hand);
            Assert.Equal(0.5f, use.CursorX);
            Assert.Equal(1f, use.CursorY);
            Assert.Equal(0.5f, use.CursorZ);
        }
    }

    private static Task WaitAsync(UmpkClient client, Func<UmpkClient, bool> condition, string description, CancellationToken ct)
        => WaitAsync(async () => await client.InvokeAsync(condition, ct), description, ct);

    private static Task WaitAsync(Func<bool> condition, string description, CancellationToken ct)
        => WaitAsync(() => Task.FromResult(condition()), description, ct);

    private static async Task WaitAsync(Func<Task<bool>> condition, string description, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            Assert.False(deadline.IsCancellationRequested, $"Timed out waiting for {description}.");
            await Task.Delay(50, deadline.Token);
        }
    }

    private sealed record CapturedFrame(double Milliseconds, PacketFlow Flow, int WireId, byte[] Payload)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public object? Packet { get; init; }
        public ProtocolPhase Phase { get; init; } = ProtocolPhase.Play;
    }

    private sealed class Capture(JavaVersion version, bool includeEnabledFeatures = false)
    {
        private const int Limit = 20 * 1024 * 1024;
        private readonly Lock _gate = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<CapturedFrame> _frames = [];
        private bool _active;
        public long PayloadBytes { get; private set; }
        public bool Overflowed { get; private set; }
        public CapturedFrame[] Frames { get { lock (_gate) return [.. _frames]; } }
        public void Start() { lock (_gate) _active = true; }
        public void Stop() { lock (_gate) _active = false; }
        public int Mark() { lock (_gate) return _frames.Count; }
        public CapturedFrame[] Since(int mark) { lock (_gate) return _frames.Skip(mark).ToArray(); }

        public void Observe(PacketObservation observation)
        {
            if (observation.Phase != ProtocolPhase.Play && !(includeEnabledFeatures && observation.Phase == ProtocolPhase.Configuration && observation.DecodedPacket is ClientboundConfigUpdateEnabledFeaturesPacket))
                return;
            lock (_gate)
            {
                if (!_active)
                    return;
                PayloadBytes += observation.PayloadLength;
                if (PayloadBytes > Limit)
                {
                    Overflowed = true;
                    return;
                }
                _frames.Add(new CapturedFrame(_clock.Elapsed.TotalMilliseconds, observation.Flow, observation.WireId, observation.CopyPayload()) { Packet = observation.DecodedPacket, Phase = observation.Phase });
            }
        }

        public int AssertRoundTrips(Umpk.Game.Registries.RegistryAccess registries)
        {
            Assert.False(Overflowed, "The unchanged 20 MiB capture cap was exceeded.");
            var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
            int count = 0;
            foreach (CapturedFrame frame in Frames)
            {
                if (frame.Packet is not (ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundLegacyBlockPlacePacket or ServerboundUseItemOnPacket or ClientboundBlockUpdatePacket))
                    continue;
                PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, frame.Flow);
                Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
                object decoded = codec.Decode(frame.Payload, context);
                object? expected = frame.Packet;
                if (expected is ServerboundUseItemOnPacket use)
                    expected = use with
                    {
                        Inside = version.Version.Protocol >= 477 && use.Inside,
                        WorldBorderHit = version.Version.Protocol >= 768 && use.WorldBorderHit,
                        Sequence = version.Version.Protocol >= 759 ? use.Sequence : 0,
                    };
                if (expected is ServerboundSwingPacket swing)
                    expected = swing with { HasHand = version.Version.Protocol >= 107 };
                Assert.Equal(expected, decoded);
                var bytes = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(bytes);
                codec.Encode(ref writer, decoded, context);
                Assert.Equal(frame.Payload, bytes.WrittenSpan.ToArray());
                count++;
            }
            Assert.True(count >= 10);
            return count;
        }
    }
}
