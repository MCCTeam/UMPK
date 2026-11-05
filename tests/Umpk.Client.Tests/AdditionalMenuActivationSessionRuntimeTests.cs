using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class AdditionalMenuActivationSessionRuntimeTests
{
    private static readonly string[] Menus =
    [
        "trapped_chest", "ender_chest", "dispenser", "dropper", "hopper", "brewing_stand", "enchanting_table", "anvil", "chipped_anvil", "damaged_anvil",
        "barrel", "smoker", "blast_furnace", "loom", "cartography_table", "stonecutter", "grindstone", "smithing_table", "crafter",
    ];

    public static IEnumerable<object[]> CatalogCases() => JavaVersions.All.Select(version => version.Version.Protocol).Distinct()
        .SelectMany(protocol => (protocol == 47 ? new[] { Hand.Main } : new[] { Hand.Main, Hand.Off }).Select(hand => new object[] { protocol, hand }));

    [Theory]
    [MemberData(nameof(CatalogCases))]
    public async Task RegisteredMenuStatesUseOriginalClientResultWithoutBlockEntityData(int protocol, Hand hand)
    {
        await using Session session = await Session.Create(protocol, "trapped_chest");
        await session.Block(Session.Target.Above(), "stone");
        Assert.Empty(await session.Barrier());
        string[] names = Menus.Where(name => session.Client.State.Registries!.Blocks.TryGet(Identifier.Minecraft(name), out _)).ToArray();
        Assert.Equal(protocol >= 765 ? 19 : protocol >= 477 ? 18 : protocol >= 393 ? 10 : 8, names.Length);
        foreach (string name in names)
        {
            BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value;
            int[] states = [block.MinStateId, block.DefaultStateId, block.MinStateId + block.StateCount - 1];
            foreach (int state in states.Distinct())
            {
                await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
                Assert.Empty(await session.Barrier());
                Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
                Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
                BlockUseResult expected = hand == Hand.Off && protocol >= 766 || name == "smithing_table" && protocol < 735 ? BlockUseResult.Pass : BlockUseResult.Success;
                await AssertUse(session, protocol, hand, expected);
                Assert.Equal(Identifier.Minecraft("stone"), session.Client.State.World.GetBlock(Session.Target.Above()).Block.Id);
            }
        }
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await session.Server.NextFrameAsync(session.Token);
        });
    }

    public static IEnumerable<object[]> ContextCases() => new[] { 47, 315, 477, 578, 735, 765, 766, 768, 777 }
        .SelectMany(protocol => Enumerable.Range(0, 13).Where(context => protocol != 47 || context is not (3 or 5 or 11)).Select(context => new object[] { protocol, context }));

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task MenuActivationRespectsExistingHandTerrainPermissionAndItemOwners(int protocol, int context)
    {
        await using Session session = await Session.Create(protocol, "trapped_chest", terrain: context != 7);
        if (context is 0 or 2 or 4)
            await session.Hold("stick");
        if (context is 5 or 6 or 12)
            await session.Hold("diamond_axe", custom: context is 6 or 12);
        if (context == 10)
            await session.Hold("stone");
        if (context is 3 or 11)
            await session.Offhand("stick");
        if (context is 1 or 2 or 3 or 12)
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.Self.Sneaking);
        }
        if (context == 4)
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == 5)
            await session.Cooldown();
        if (context == 8)
            await session.Forget();
        if (context == 9)
        {
            if (protocol < 735)
            {
                await session.Send(new ClientboundRespawnPacket(null, 0, new LegacyRespawnFields(-1, (byte)(protocol < 477 ? 2 : 0), 0, "flat")));
                Assert.Empty(await session.Barrier());
            }
            else
                await session.Respawn();
        }
        await session.Block(Session.Target.Above(), "stone");
        Assert.Empty(await session.Barrier());
        string[] names = Menus.Where(name => name != "smithing_table" && session.Client.State.Registries!.Blocks.TryGet(Identifier.Minecraft(name), out _)).ToArray();
        Assert.NotEmpty(names);
        foreach (string name in names)
        {
            await session.Block(Session.Target, name);
            Assert.Empty(await session.Barrier());
            if (context is 7 or 8 or 9)
            {
                Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
                Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
            }
            Hand hand = context == 11 ? Hand.Off : Hand.Main;
            // Legacy Unbreakable NBT does not change the axe's native pass branch; effective component patches remain unknown.
            BlockUseResult expected = context is 7 or 8 or 9 || context == 12 && protocol >= 766 ? BlockUseResult.Unknown
                : context is 2 or 3 or 12 || context == 11 && protocol >= 766 ? BlockUseResult.Pass
                : BlockUseResult.Success;
            await AssertUse(session, protocol, hand, expected);
        }
    }

    private static async Task AssertUse(Session session, int protocol, Hand hand, BlockUseResult expected)
    {
        Assert.Empty(await session.Barrier());
        ItemStack main = session.Client.State.Inventory.GetSlot(36);
        ItemStack off = session.Client.State.Inventory.GetSlot(45);
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        var blockEntity = session.Client.State.World.GetBlockEntityNbt(Session.Target);
        Vec3d cursor = new(.5, 1, .5);
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, cursor, hand), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        BlockUseSwing swing = expected == BlockUseResult.Success ? protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing, prediction.Swing);
        List<IPacket> sent = await session.Use(hand);
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, sent.Count);
        if (protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(sent[0]);
            Assert.Equal((Session.Target, (int)Direction.Up), (use.Position, use.Face));
            Assert.Equal(main, use.HeldItem);
            Assert.Equal(((byte)8, (byte)16, (byte)8), (use.CursorX, use.CursorY, use.CursorZ));
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
            Assert.Equal((Session.Target, (int)Direction.Up, (int)hand), (use.Position, use.Face, use.Hand));
            Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.False(use.Inside);
        }
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(sent[1]).Hand);
        Assert.DoesNotContain(sent, packet => packet is ServerboundPunchPacket);
        Assert.Empty(await session.Barrier());
        Assert.Equal(main, session.Client.State.Inventory.GetSlot(36));
        Assert.Equal(off, session.Client.State.Inventory.GetSlot(45));
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Same(blockEntity, session.Client.State.World.GetBlockEntityNbt(Session.Target));
    }
}
