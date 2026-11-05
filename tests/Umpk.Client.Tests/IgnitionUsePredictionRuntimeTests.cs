using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class IgnitionUsePredictionRuntimeTests
{
    private static readonly int[] BoundaryProtocols = [735, 755, 765, 766, 767, 768, 776, 777];
    private static readonly string[] Items = ["flint_and_steel", "fire_charge"];

    public static IEnumerable<object[]> CatalogCases() => JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol is >= 735 and <= 777).SelectMany(protocol => new[] { Hand.Main, Hand.Off }.Select(hand => new object[] { protocol, hand }));

    [Theory]
    [MemberData(nameof(CatalogCases))]
    public async Task SynchronizedDryUnlitTargetsUseTheNativeSuccessPacketGroup(int protocol, Hand hand)
    {
        await using Session session = await Session.Create(protocol, "campfire");
        await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        await Tags(session, protocol, includeTargets: true);
        string[] targets = session.Client.State.Registries!.Blocks.Where(block => IsCampfire(block.Id.Path) || protocol >= 755 && IsCandle(block.Id.Path)).Select(block => block.Id.Path).ToArray();
        Assert.Equal(protocol < 755 ? 2 : 19, targets.Length);
        foreach (string target in targets)
            foreach (string item in Items)
            {
                await State(session, target, lit: false, waterlogged: false);
                await Hold(session, hand, item);
                Direction face = (Direction)((Array.IndexOf(targets, target) + (int)hand) % 6);
                await AssertPredictionAndUse(session, protocol, hand, BlockUseResult.Success, face);
            }
    }

    public static IEnumerable<object[]> ContextCases() => BoundaryProtocols.SelectMany(protocol => Items.SelectMany(item => new[] { "lit", "wet", "missing-terrain", "unload", "missing-tags", "removed-tag", "custom", "adventure", "cooldown", "cooldown-adventure", "standing", "respawn" }.Select(context => new object[] { protocol, item, context })));

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task UnsupportedContextRemainsUnknownAndExistingPermissionOrderingWins(int protocol, string item, string context)
    {
        await using Session session = await Session.Create(protocol, "campfire", terrain: context != "missing-terrain");
        if (context != "standing")
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        if (context != "missing-tags")
            await Tags(session, protocol, includeTargets: true);
        await State(session, "campfire", lit: context == "lit", waterlogged: context == "wet");
        await session.Hold(item, custom: context == "custom");
        if (context == "removed-tag")
            await Tags(session, protocol, includeTargets: false);
        if (context == "unload")
        {
            await session.Forget();
            await State(session, "campfire", lit: false, waterlogged: false);
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        if (context is "adventure" or "cooldown-adventure")
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context is "cooldown" or "cooldown-adventure")
            await session.Cooldown();
        if (context == "respawn")
        {
            await session.Respawn();
            await State(session, "campfire", lit: false, waterlogged: false);
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        await AssertPredictionAndUse(session, protocol, Hand.Main, context is "cooldown" or "cooldown-adventure" ? BlockUseResult.Pass : context == "custom" && protocol < 766 ? BlockUseResult.Success : BlockUseResult.Unknown);
    }

    public static IEnumerable<object[]> DamageCases() => BoundaryProtocols.SelectMany(protocol => new[] { Hand.Main, Hand.Off }.Select(hand => new object[] { protocol, hand }));

    [Theory]
    [MemberData(nameof(DamageCases))]
    public async Task ObservedDurabilityDoesNotRemoveTheNativeIgnitionResult(int protocol, Hand hand)
    {
        await using Session session = await Session.Create(protocol, "campfire");
        await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        await Tags(session, protocol, includeTargets: true);
        await State(session, "campfire", lit: false, waterlogged: false);
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("flint_and_steel")], 1);
        stack = protocol >= 766 ? stack.With(DataComponents.Damage, new DamageComponent(7)) : stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(new NbtCompound { ["Damage"] = new NbtInt(7) }));
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, hand == Hand.Main ? 36 : 45, stack));
        Assert.Empty(await session.Barrier());
        ItemStack observed = session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
        if (protocol >= 766)
            Assert.Equal(7, observed.Damage);
        else
        {
            Assert.True(observed.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy));
            Assert.Equal(7, Assert.IsType<NbtCompound>(legacy.Nbt).GetInt("Damage"));
        }
        await AssertPredictionAndUse(session, protocol, hand, BlockUseResult.Success);
    }

    [Theory]
    [InlineData(340)]
    [InlineData(498)]
    [InlineData(578)]
    public async Task EarlierIgnitionMethodsRemainUnclassified(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone");
        await session.Hold("flint_and_steel");
        await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
        await AssertPredictionAndUse(session, protocol, Hand.Main, BlockUseResult.Unknown);
    }

    private static bool IsCampfire(string block) => block is "campfire" or "soul_campfire";
    private static bool IsCandle(string block) => block == "candle" || block.EndsWith("_candle", StringComparison.Ordinal);

    private static async Task Hold(Session session, Hand hand, string item)
    {
        if (hand == Hand.Main)
            await session.Hold(item);
        else
            await session.Offhand(item);
    }

    private static async Task State(Session session, string name, bool lit, bool waterlogged)
    {
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value;
        int state = Enumerable.Range(block.MinStateId, block.StateCount).First(id => block.TryGetPropertyValue(id, "lit", out string actualLit) && actualLit == (lit ? "true" : "false") && block.TryGetPropertyValue(id, "waterlogged", out string actualWater) && actualWater == (waterlogged ? "true" : "false"));
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
    }

    private static async Task Tags(Session session, int protocol, bool includeTargets)
    {
        var blocks = session.Client.State.Registries!.Blocks;
        int[] campfires = includeTargets ? blocks.Where(block => IsCampfire(block.Id.Path)).Select(block => block.NetworkId).ToArray() : [];
        int[] candles = includeTargets ? blocks.Where(block => IsCandle(block.Id.Path)).Select(block => block.NetworkId).ToArray() : [];
        var packet = new ClientboundUpdateTagsPacket([new TagRegistry(RegistryIds.Block, [new TagEntry(Identifier.Minecraft("campfires"), campfires), new TagEntry(Identifier.Minecraft("candles"), candles)])]);
        BoundPacketCodec codec = Support.BoundDescriptorCodec.Clientbound(protocol, "update_tags");
        PacketCodecContext context = new(session.Client.State.Registries, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<ClientboundUpdateTagsPacket>(codec.Decode(bytes, context));
        var registry = Assert.Single(decoded.Registries, registry => registry.Registry == RegistryIds.Block);
        Assert.Equal(campfires, Assert.Single(registry.Tags, tag => tag.TagId == Identifier.Minecraft("campfires")).Ids);
        Assert.Equal(candles, Assert.Single(registry.Tags, tag => tag.TagId == Identifier.Minecraft("candles")).Ids);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
        int id = blocks[Identifier.Minecraft("campfire")].NetworkId;
        Assert.Equal(includeTargets, session.Client.State.RegistryTags.Contains(RegistryIds.Block, Identifier.Minecraft("campfires"), id));
    }

    private static async Task AssertPredictionAndUse(Session session, int protocol, Hand hand, BlockUseResult expected, Direction face = Direction.Up)
    {
        Assert.Empty(await session.Barrier());
        ItemStack before = session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        Vec3d hit = new(.25, .75, .5);
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, face, hit, hand), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        BlockUseSwing swing = expected == BlockUseResult.Success ? protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing, prediction.Swing);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, face, hit, hand, insideBlock: true, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((Session.Target, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
        Assert.Equal(((float)hit.X, (float)hit.Y, (float)hit.Z), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.Equal(protocol >= 477, use.Inside);
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

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
