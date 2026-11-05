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
    [InlineData("1.12.2", 340)]
    [InlineData("1.13", 393)]
    [InlineData("1.17", 755)]
    [InlineData("1.20.4", 765)]
    [InlineData("1.20.6", 766)]
    [InlineData("1.21.1", 767)]
    [InlineData("1.21.5", 770)]
    [InlineData("1.21.10", 773)]
    [InlineData("26.2", 776)]
    public async Task AxeUseMatchesNativeStrippingCopperAndSwing(string release, int protocol)
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
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "axe terrain", ct);
            Assert.True(client.State.Features.Physics);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = (int)Math.Floor(spawn.X), z = (int)Math.Floor(spawn.Z);
            var target = new BlockPos(x + ((x & 15) >= 14 ? -2 : 2), 100, z);
            Assert.Equal((x >> 4, z >> 4), (target.X >> 4, target.Z >> 4));
            await server.ConsoleAsync("difficulty peaceful", ct);
            string randomTickRule = protocol >= 774 ? "minecraft:random_tick_speed" : "randomTickSpeed";
            await server.ConsoleAsync($"gamerule {randomTickRule} 0", ct);
            string randomTickQuery = await server.CommandAsync($"gamerule {randomTickRule}", ct);
            string expectedRandomTickQuery = protocol < 393 ? "randomTickSpeed = 0"
                : $"Gamerule {(protocol >= 774 ? "random_tick_speed" : "randomTickSpeed")} is currently set to: 0";
            Assert.Equal(expectedRandomTickQuery, randomTickQuery);
            bool randomTicksDisabled = randomTickQuery == expectedRandomTickQuery;
            await server.WriteAsync("axe-setup.json", new { randomTickRule, randomTickQuery, randomTicksDisabled });
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync($"tp {Username} {x + .5:R} 100 {z + .5:R}", ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await server.ConsoleAsync($"clear {Username}", ct);
            await server.ConsoleAsync($"give {Username} minecraft:diamond_axe 1", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && c.State.Self.OnGround && Math.Abs(c.State.Self.Position.Y - 100) < .01 && c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("diamond_axe"), "grounded axe owner and inventory", ct);
            capture.Start();
            int damage = 0;
            int previousSequence = -1;
            var scenarios = new List<object>();
            await Use("oak_log", protocol >= 393 ? "stripped_oak_log" : "oak_log", Direction.Down, true, protocol >= 393, "z");
            if (protocol >= 393)
                await Use("stripped_oak_log", "stripped_oak_log", Direction.Up, false, false, "z");
            await Use("stone", "stone", Direction.Up, false, false);
            await Use("oak_wood", protocol >= 393 ? "stripped_oak_wood" : "oak_wood", Direction.Up, false, protocol >= 393, "x");
            await Use("oak_log", protocol >= 393 ? "stripped_oak_log" : "oak_log", Direction.Up, false, protocol >= 393, "y");
            if (protocol >= 755)
            {
                await client.Actions.Movement.SetSneakingAsync(true, ct);
                await Use("weathered_copper", "exposed_copper", Direction.Up, true, true);
                await Use("waxed_oxidized_cut_copper", "oxidized_cut_copper", Direction.Down, false, true);
                await Use("copper_block", "copper_block", Direction.Up, false, false);
                // Before 1.21 these registry targets require UPDATE_1_21 and cannot be established in an ordinary vanilla world. Packet-runtime cases cover that explicit feature context.
                if (protocol >= 767)
                    await Use("waxed_weathered_copper_bulb", "weathered_copper_bulb", Direction.Up, false, true);
                if (protocol >= 773)
                    await Use("waxed_oxidized_lightning_rod", "oxidized_lightning_rod", Direction.Up, false, true);
                await client.Actions.Movement.SetSneakingAsync(false, ct);
            }
            if (protocol >= 766)
            {
                await server.ConsoleAsync($"item replace entity {Username} weapon.offhand with minecraft:shield", ct);
                await WaitAsync(client, c => c.State.Inventory.GetSlot(45).Item.Id == Identifier.Minecraft("shield"), "axe offhand shield", ct);
                await Use("oak_log", protocol >= 767 ? "oak_log" : "stripped_oak_log", Direction.Up, false, protocol < 767, "y");
                await client.Actions.Movement.SetSneakingAsync(true, ct);
                await Use("oak_log", "stripped_oak_log", Direction.Up, false, true, "y");
                await client.Actions.Movement.SetSneakingAsync(false, ct);
            }
            int replayed = capture.AssertRoundTrips(client.State.Registries!);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "axe server-observed disconnect", ct);
            await server.WriteAsync("axe-acceptance.json", new { release, protocol, physics = true, randomTicksDisabled, randomTickRule, randomTickQuery, nativeStripping = protocol >= 393, copper = protocol >= 755, shieldIntent = protocol >= 767, damage, replayed, scenarios, peerObservedDisconnect = true });
            passed = true;

            async Task Use(string before, string after, Direction face, bool blockedAbove, bool success, string? axis = null)
            {
                await server.ConsoleAsync($"setblock {target.X} {target.Y + 1} {target.Z} minecraft:{(blockedAbove ? "stone" : "air")}", ct);
                string nativeTarget = NativeBlockName(before) + (protocol < 393 && axis is not null ? " " + LegacyAxis(axis) : Axis(axis));
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:{nativeTarget}", ct);
                await WaitAsync(client, c => HasBlock(c, before, axis) && c.State.World.GetBlock(target.Above()).Block.Id == Identifier.Minecraft(blockedAbove ? "stone" : "air"), "axe precondition updates", ct);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(target, face, new Vec3d(.5, 1, .5), ct: ct);
                if (success)
                    damage++;
                await WaitAsync(client, c => HasBlock(c, after, axis) && Damage(c.State.Inventory.GetSlot(36)) == damage, "axe result and durability", ct);
                await AssertBlockAsync(server, protocol, target, NativeBlockName(after) + (protocol >= 393 ? Axis(axis) : ""), ct, protocol < 393 && axis is not null ? LegacyAxis(axis) : null);
                if (protocol < 393)
                    Assert.Equal("Found " + Username, await server.CommandAsync($"testfor @a[name={Username}] {{SelectedItem:{{id:\"minecraft:diamond_axe\",Damage:{damage}s}}}}", ct));
                else
                {
                    string marker = "axe-damage-" + Guid.NewGuid().ToString("N");
                    string predicate = protocol < 766
                        ? "execute if entity @a[name=" + Username + ",nbt={SelectedItem:{id:\"minecraft:diamond_axe\",tag:{Damage:" + damage + "}}}]"
                        : $"execute if items entity {Username} weapon.mainhand minecraft:diamond_axe[minecraft:damage={damage}]";
                    await server.CommandAsync(predicate + " run say " + marker, ct);
                    await WaitAsync(() => server.ServerLog.Contains("[Rcon] " + marker, StringComparison.Ordinal), "independent axe durability predicate", ct);
                }
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                CapturedFrame[] frames = capture.Since(mark).Where(IsUseOrAnimation).ToArray();
                Assert.Equal(success ? 2 : 1, frames.Length);
                var use = Assert.IsType<ServerboundUseItemOnPacket>(frames[0].Packet);
                Assert.Equal(target, use.Position);
                Assert.Equal((int)face, use.Face);
                Assert.Equal(0, use.Hand);
                Assert.Equal(.5f, use.CursorX);
                Assert.Equal(1f, use.CursorY);
                Assert.Equal(.5f, use.CursorZ);
                Assert.False(use.Inside);
                Assert.False(use.WorldBorderHit);
                if (protocol >= 759)
                {
                    Assert.True(use.Sequence > previousSequence);
                    previousSequence = use.Sequence;
                }
                if (success)
                    Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(frames[1].Packet).Hand);
                scenarios.Add(new { before, after, face, blockedAbove, success, axis, damage, packets = frames.Length, sequence = protocol >= 759 ? use.Sequence : (int?)null });
                await server.WriteAsync("axe-scenarios.json", scenarios);
            }

            string NativeBlockName(string block) => protocol >= 393 ? block : block is "oak_log" or "oak_wood" ? "log" : block;
            static string Axis(string? axis) => axis is null ? "" : "[axis=" + axis + "]";
            static int LegacyAxis(string axis) => axis is "x" ? 4 : axis is "z" ? 8 : 0;
            int Damage(ItemStack stack) => protocol < 393 || protocol >= 766 ? stack.Damage : stack.Components.Get(DataComponents.LegacyNbt)?.Nbt.GetInt("Damage") ?? 0;
            bool HasBlock(UmpkClient owner, string block, string? axis)
            {
                var state = owner.State.World.GetBlock(target);
                return state.IsValid && state.Block.Id == Identifier.Minecraft(NativeBlockName(block))
                    && (axis is null || protocol < 393 && state.LegacyMeta == LegacyAxis(axis) || protocol >= 393 && state.TryGetProperty("axis", out string actual) && actual == axis);
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "axe use", capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }
}
