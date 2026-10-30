using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class HoneycombUsePredictionRuntimeTests
{
    private static readonly int[] BoundaryProtocols = [755, 765, 766, 768, 776];
    private static readonly string[] OriginalTargets =
    [
        "copper_block", "exposed_copper", "weathered_copper", "oxidized_copper",
        "cut_copper", "exposed_cut_copper", "weathered_cut_copper", "oxidized_cut_copper",
        "cut_copper_slab", "exposed_cut_copper_slab", "weathered_cut_copper_slab", "oxidized_cut_copper_slab",
        "cut_copper_stairs", "exposed_cut_copper_stairs", "weathered_cut_copper_stairs", "oxidized_cut_copper_stairs",
    ];

    public static IEnumerable<object[]> CatalogCases() => JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol is >= 755 and <= 776).SelectMany(protocol => new[] { Hand.Main, Hand.Off }.SelectMany(hand => new[] { false, true }.Select(waxed => new object[] { protocol, hand, waxed })));

    [Theory]
    [MemberData(nameof(CatalogCases))]
    public async Task OriginalCopperMembershipDeterminesSuccessOrPassAcrossProperties(int protocol, Hand hand, bool waxed)
    {
        await using Session session = await Session.Create(protocol, "copper_block");
        await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        if (hand == Hand.Main)
            await session.Hold("honeycomb");
        else
            await session.Offhand("honeycomb");
        foreach (string target in OriginalTargets)
        {
            string name = waxed ? "waxed_" + target : target;
            BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value;
            int[] states = [block.MinStateId, block.DefaultStateId, block.MinStateId + block.StateCount - 1];
            foreach (int state in states.Distinct())
            {
                await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
                Assert.Empty(await session.Barrier());
                Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
                Direction face = (Direction)((Array.IndexOf(OriginalTargets, target) + state + (int)hand) % 6);
                await AssertUse(session, protocol, hand, waxed ? BlockUseResult.Pass : BlockUseResult.Success, face);
            }
        }
    }

    public static IEnumerable<object[]> ContextCases() => BoundaryProtocols.SelectMany(protocol => new[] { "missing-terrain", "unload", "respawn", "components", "adventure", "cooldown", "cooldown-adventure", "standing", "unclassified-target" }.Select(context => new object[] { protocol, context }));

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task ExistingContextAndPermissionOwnersRetainTheirDecisions(int protocol, string context)
    {
        await using Session session = await Session.Create(protocol, "copper_block", terrain: context != "missing-terrain");
        if (context != "standing")
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        await session.Hold("honeycomb", custom: context == "components");
        if (context == "missing-terrain")
        {
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        if (context == "unclassified-target")
        {
            await session.Block(Session.Target, protocol >= 765 ? "chiseled_copper" : "stone");
            Assert.Empty(await session.Barrier());
        }
        if (context == "unload")
        {
            await session.Forget();
            await session.Block(Session.Target, "copper_block");
            Assert.Empty(await session.Barrier());
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        if (context == "respawn")
        {
            await session.Respawn();
            await session.Block(Session.Target, "copper_block");
            Assert.Empty(await session.Barrier());
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        if (context is "adventure" or "cooldown-adventure")
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context is "cooldown" or "cooldown-adventure")
            await session.Cooldown();
        BlockUseResult expected = context == "standing" ? BlockUseResult.Success : context is "cooldown" or "cooldown-adventure" ? BlockUseResult.Pass : BlockUseResult.Unknown;
        await AssertUse(session, protocol, Hand.Main, expected);
    }

    [Theory]
    [InlineData(578, (int)BlockUseResult.Pass)]
    [InlineData(754, (int)BlockUseResult.Pass)]
    [InlineData(777, (int)BlockUseResult.Unknown)]
    public async Task EarlierDefaultPassAndLaterTransformerOwnershipRemainSeparate(int protocol, int expected)
    {
        await using Session session = await Session.Create(protocol, "stone");
        await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        await session.Hold("honeycomb");
        await AssertUse(session, protocol, Hand.Main, (BlockUseResult)expected);
    }

    private static async Task AssertUse(Session session, int protocol, Hand hand, BlockUseResult expected, Direction face = Direction.Up)
    {
        Assert.Empty(await session.Barrier());
        ItemStack before = session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        Vec3d cursor = new(.125, .625, .875);
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, face, cursor, hand), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        BlockUseSwing swing = expected == BlockUseResult.Success ? BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing, prediction.Swing);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, face, cursor, hand, insideBlock: true, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((Session.Target, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
        Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.True(use.Inside);
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
        if (protocol >= 759)
        {
            Assert.False(action.IsCompleted);
            await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence));
        }
        await action.WaitAsync(session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal(before, session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45));
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
    }
}
