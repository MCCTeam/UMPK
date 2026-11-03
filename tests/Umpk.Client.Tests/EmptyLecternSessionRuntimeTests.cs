using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class EmptyLecternSessionRuntimeTests
{
    private static readonly int[] Protocols = JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol >= 766).ToArray();

    public static IEnumerable<object[]> HandCases() => Protocols.SelectMany(protocol => new[] { Hand.Main, Hand.Off }
        .SelectMany(hand => new string?[] { null, "stick", "writable_book", "written_book", "stone" }.Select(item => new object[] { protocol, hand, item! })));

    [Theory]
    [MemberData(nameof(HandCases))]
    public async Task ReceivedBookTagControlsBothHandsBeforeDefaultAndItemContinuation(int protocol, Hand hand, string? item)
    {
        await using Session session = await Session.Create(protocol, "lectern", physics: hand == Hand.Off && item == "stone");
        await Tags(session, ["writable_book", "written_book"]);
        await Hold(session, hand, item);
        BlockUseResult expected = item switch
        {
            "writable_book" or "written_book" => BlockUseResult.Success,
            null => BlockUseResult.Pass,
            _ when hand == Hand.Main => BlockUseResult.Consume,
            "stone" => BlockUseResult.Success,
            _ => BlockUseResult.Pass,
        };
        await AssertUse(session, hand, expected);
        await Disconnect(session);
    }

    public static IEnumerable<object[]> TagCases() => Protocols.SelectMany(protocol => Enumerable.Range(0, 8).Select(context => new object[] { protocol, context }));

    [Theory]
    [MemberData(nameof(TagCases))]
    public async Task MissingWrongRegistryRemovedAndReplacedTagsRetainNativeKnowledge(int protocol, int context)
    {
        await using Session session = await Session.Create(protocol, "lectern");
        Hand hand = context == 5 ? Hand.Off : Hand.Main;
        string? item = context == 5 ? "stick" : context == 6 ? null : "writable_book";
        await Hold(session, hand, item);
        if (context == 1)
            await Tags(session, [], omitTag: true);
        else if (context == 2)
            await Tags(session, ["writable_book"], blockRegistry: true);
        else if (context == 3)
            await Tags(session, []);
        else if (context is 4 or 7)
            await Tags(session, ["writable_book", "written_book"]);
        else if (context == 5)
            await Tags(session, ["stick"]);
        else if (context == 6)
            await Tags(session, ["air"]);
        BlockUseResult expected = context switch
        {
            0 => BlockUseResult.Unknown,
            2 => protocol < 768 ? BlockUseResult.Consume : BlockUseResult.Unknown,
            1 or 3 => BlockUseResult.Consume,
            _ => BlockUseResult.Success,
        };
        await AssertUse(session, hand, expected);
        if (context == 7)
        {
            await Tags(session, []);
            await AssertUse(session, hand, BlockUseResult.Consume);
        }
        await Disconnect(session);
    }

    public static IEnumerable<object[]> ContextCases() => new[] { 766, 768, 770, 774, 777 }
        .SelectMany(protocol => Enumerable.Range(0, 12).Select(context => new object[] { protocol, context }));

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task ActivationPrecedesCooldownAdventureAndComponentsButRespectsSecondaryUseAndTerrain(int protocol, int context)
    {
        await using Session session = await Session.Create(protocol, "lectern", terrain: context != 7);
        if (context != 9)
            await Tags(session, ["writable_book", "written_book"]);
        string? item = context is 3 or 4 or 11 ? "stick" : context is 6 or 9 ? null : "writable_book";
        await Hold(session, Hand.Main, item, patched: context is 2 or 11);
        if (context == 6)
            await Hold(session, Hand.Off, "writable_book");
        if (context is 0 or 3)
            await session.Cooldown();
        if (context is 1 or 4)
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context is 5 or 6)
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.Self.Sneaking);
        }
        if (context == 8)
        {
            await session.Forget();
            await session.Block(Session.Target, "lectern");
            Assert.Empty(await session.Barrier());
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        if (context == 10)
            await session.Mode(GameMode.Spectator, instantBuild: false);
        BlockUseResult expected = context switch
        {
            3 or 4 or 11 => BlockUseResult.Consume,
            5 or 6 => BlockUseResult.Pass,
            7 or 8 or 9 => BlockUseResult.Unknown,
            10 => protocol < 768 ? BlockUseResult.Success : BlockUseResult.Consume,
            _ => BlockUseResult.Success,
        };
        await AssertUse(session, Hand.Main, expected);
        await Disconnect(session);
    }

    public static IEnumerable<object[]> StateCases() => Protocols.Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(StateCases))]
    public async Task EveryEmptyStateAcceptsTaggedBookWithoutPredictingServerMutations(int protocol)
    {
        await using Session session = await Session.Create(protocol, "lectern");
        await Tags(session, ["writable_book", "written_book"]);
        await Hold(session, Hand.Main, "written_book", patched: true);
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft("lectern")].Value;
        int[] states = Enumerable.Range(block.MinStateId, block.StateCount)
            .Where(state => block.TryGetPropertyValue(state, "has_book", out string hasBook) && hasBook == "false").ToArray();
        Assert.Equal(8, states.Length);
        foreach (int state in states)
        {
            await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
            Assert.Empty(await session.Barrier());
            Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
            await AssertUse(session, Hand.Main, BlockUseResult.Success);
        }
        await Disconnect(session);
    }

    private static async Task AssertUse(Session session, Hand hand, BlockUseResult expected)
    {
        ItemStack main = session.Client.State.Inventory.GetSlot(36);
        ItemStack off = session.Client.State.Inventory.GetSlot(45);
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
        BlockUsePrediction preview = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand), session.Token);
        BlockUseSwing swing = expected == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        List<IPacket> packets = await session.Use(hand);
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((Session.Target, (int)Direction.Up, (int)hand), (use.Position, use.Face, use.Hand));
        Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside);
        Assert.False(use.WorldBorderHit);
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket or ServerboundPlayerActionPacket);
        Assert.Equal(new BlockUsePrediction(true, expected, swing), preview);
        Assert.Empty(await session.Barrier());
        Assert.Equal(main, session.Client.State.Inventory.GetSlot(36));
        Assert.Equal(off, session.Client.State.Inventory.GetSlot(45));
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
        Assert.False(session.Client.State.Inventory.HasOpenContainer);
    }

    private static async Task Hold(Session session, Hand hand, string? name, bool patched = false)
    {
        ItemStack stack = name is null ? ItemStack.Empty : new(session.Client.State.Registries!.Items[Identifier.Minecraft(name)], 3);
        if (patched)
            stack = stack.With(DataComponents.Unbreakable, new UnbreakableComponent());
        int slot = hand == Hand.Main ? 36 : 45;
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, slot, stack));
        Assert.Empty(await session.Barrier());
        ItemStack observed = session.Client.State.Inventory.GetSlot(slot);
        Assert.Equal(stack, observed);
        if (patched)
            Assert.True(observed.Components.TryGet(DataComponents.Unbreakable, out _));
    }

    private static async Task Tags(Session session, string[] names, bool omitTag = false, bool blockRegistry = false)
    {
        Identifier registry = blockRegistry ? RegistryIds.Block : RegistryIds.Item;
        int[] ids = names.Select(name => blockRegistry ? session.Client.State.Registries!.Blocks[Identifier.Minecraft(name == "writable_book" ? "stone" : name)].NetworkId : session.Client.State.Registries!.Items[Identifier.Minecraft(name)].NetworkId).ToArray();
        TagEntry[] tags = omitTag ? [] : [new TagEntry(Identifier.Minecraft("lectern_books"), ids)];
        var packet = new ClientboundUpdateTagsPacket([new TagRegistry(registry, tags)]);
        BoundPacketCodec codec = Support.BoundDescriptorCodec.Clientbound(session.Protocol, "update_tags");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<ClientboundUpdateTagsPacket>(codec.Decode(bytes, context));
        TagRegistry observed = Assert.Single(decoded.Registries);
        Assert.Equal(registry, observed.Registry);
        Assert.Equal(tags.Length, observed.Tags.Count);
        if (!omitTag)
        {
            TagEntry tag = Assert.Single(observed.Tags);
            Assert.Equal(Identifier.Minecraft("lectern_books"), tag.TagId);
            Assert.Equal(ids, tag.Ids);
        }
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
        if (!omitTag && !blockRegistry)
            foreach (string name in new[] { "air", "stick", "writable_book", "written_book" })
                Assert.Equal(names.Contains(name, StringComparer.Ordinal), session.Client.State.RegistryTags.Contains(RegistryIds.Item, Identifier.Minecraft("lectern_books"), session.Client.State.Registries!.Items[Identifier.Minecraft(name)].NetworkId));
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }

    private static async Task Disconnect(Session session)
    {
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await session.Server.NextFrameAsync(session.Token);
        });
    }
}
