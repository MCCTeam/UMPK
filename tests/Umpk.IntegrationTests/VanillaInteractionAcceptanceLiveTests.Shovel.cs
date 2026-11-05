using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    [NightlyTheory]
    [InlineData("1.8.9", 47)]
    [InlineData("1.9", 107)]
    [InlineData("1.15", 573)]
    [InlineData("1.17", 755)]
    [InlineData("26.2", 776)]
    public async Task ShovelUseMatchesNativePathsCampfiresDurabilityAndSwing(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct);
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        client.PacketFrameObserved += capture.Observe;
        bool passed = false;
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "shovel terrain", ct);
            Assert.True(client.State.Features.Physics);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = (int)Math.Floor(spawn.X), z = (int)Math.Floor(spawn.Z);
            var target = new BlockPos(x + 2, 100, z);
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync($"tp {Username} {x + .5:R} 100 {z + .5:R}", ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await server.ConsoleAsync($"clear {Username}", ct);
            await server.ConsoleAsync($"give {Username} minecraft:diamond_shovel 1", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && c.State.Self.OnGround && Math.Abs(c.State.Self.Position.Y - 100) < .01 && c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("diamond_shovel"), "grounded shovel owner and inventory", ct);
            capture.Start();
            int damage = 0;
            string path = protocol >= 755 ? "dirt_path" : "grass_path";
            await Use("grass_block", protocol >= 107 ? path : "grass_block", Direction.Up, false, protocol >= 107);
            await Use("dirt", protocol >= 755 ? path : "dirt", Direction.Up, false, protocol >= 755);
            await Use("coarse_dirt", protocol >= 755 ? path : "coarse_dirt", Direction.Up, false, protocol >= 755);
            if (protocol >= 755)
                await Use("rooted_dirt", path, Direction.Up, false, true);
            await Use("grass_block", "grass_block", Direction.Down, false, false);
            await Use("grass_block", "grass_block", Direction.Up, true, false);
            if (protocol >= 573)
            {
                await client.Actions.Movement.SetSneakingAsync(true, ct);
                await Use("campfire", "campfire", Direction.Up, true, true, initialLit: true, expectedLit: false);
                await Use("campfire", "campfire", Direction.Up, false, false, initialLit: false, expectedLit: false);
                await Use("campfire", "campfire", Direction.Down, false, false, initialLit: true, expectedLit: true);
                await client.Actions.Movement.SetSneakingAsync(false, ct);
            }
            int replayed = capture.AssertRoundTrips(client.State.Registries!);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "shovel server-observed disconnect", ct);
            await server.WriteAsync("shovel-acceptance.json", new { release, protocol, physics = true, nativePaths = protocol >= 107, expandedSoils = protocol >= 755, secondaryUseCampfire = protocol >= 573, damage, replayed, peerObservedDisconnect = true });
            passed = true;

            async Task Use(string before, string after, Direction face, bool blockedAbove, bool success, bool? initialLit = null, bool? expectedLit = null)
            {
                await server.ConsoleAsync($"setblock {target.X} {target.Y + 1} {target.Z} minecraft:{(blockedAbove ? "stone" : "air")}", ct);
                string nativeTarget = NativeBlockName(before) + (protocol < 393 && before == "coarse_dirt" ? " 1" : "") + Lit(initialLit);
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:{nativeTarget}", ct);
                await WaitAsync(client, c => HasBlock(c, before, initialLit) && c.State.World.GetBlock(target.Above()).Block.Id == Identifier.Minecraft(blockedAbove ? "stone" : "air"), "shovel precondition updates", ct);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(target, face, new Vec3d(.5, 1, .5), ct: ct);
                if (success)
                    damage++;
                await WaitAsync(client, c => HasBlock(c, after, expectedLit) && Damage(c.State.Inventory.GetSlot(36)) == damage, "shovel result and durability", ct);
                await AssertBlockAsync(server, protocol, target, NativeBlockName(after) + Lit(expectedLit), ct, protocol < 393 && after is "dirt" or "coarse_dirt" ? after == "coarse_dirt" ? 1 : 0 : null);
                if (protocol < 393)
                    Assert.Equal("Found " + Username, await server.CommandAsync($"testfor @a[name={Username}] {{SelectedItem:{{id:\"minecraft:diamond_shovel\",Damage:{damage}s}}}}", ct));
                else
                {
                    string marker = "shovel-damage-" + Guid.NewGuid().ToString("N");
                    string predicate = protocol < 766
                        ? "execute if entity @a[name=" + Username + ",nbt={SelectedItem:{id:\"minecraft:diamond_shovel\",tag:{Damage:" + damage + "}}}]"
                        : $"execute if items entity {Username} weapon.mainhand minecraft:diamond_shovel[minecraft:damage={damage}]";
                    await server.CommandAsync(predicate + " run say " + marker, ct);
                    await WaitAsync(() => server.ServerLog.Contains("[Rcon] " + marker, StringComparison.Ordinal), "independent shovel durability predicate", ct);
                }
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                CapturedFrame[] frames = capture.Since(mark).Where(IsUseOrAnimation).ToArray();
                Assert.Equal(success ? 2 : 1, frames.Length);
                if (protocol == 47)
                {
                    var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(frames[0].Packet);
                    Assert.Equal(target, use.Position);
                    Assert.Equal((int)face, use.Face);
                    Assert.Equal(Identifier.Minecraft("diamond_shovel"), use.HeldItem.Item.Id);
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

            string NativeBlockName(string block) => protocol >= 393 ? block : block switch { "grass_block" => "grass", "coarse_dirt" => "dirt", _ => block };
            static string Lit(bool? lit) => lit is bool value ? "[lit=" + (value ? "true" : "false") + "]" : "";
            int Damage(ItemStack stack) => protocol < 393 || protocol >= 766 ? stack.Damage : stack.Components.Get(DataComponents.LegacyNbt)?.Nbt.GetInt("Damage") ?? 0;
            bool HasBlock(UmpkClient owner, string block, bool? lit)
            {
                var state = owner.State.World.GetBlock(target);
                return state.IsValid && state.Block.Id == Identifier.Minecraft(NativeBlockName(block))
                    && (protocol >= 393 || block is not ("dirt" or "coarse_dirt") || state.LegacyMeta == (block == "coarse_dirt" ? 1 : 0))
                    && (lit is not bool value || state.TryGetProperty("lit", out string actual) && actual == (value ? "true" : "false"));
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "shovel use", capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }
}
