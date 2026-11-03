using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class RegistryTagPhaseSessionRuntimeTests
{
    private static readonly Identifier BookTag = Identifier.Minecraft("lectern_books");
    private static readonly Identifier BlockTag = Identifier.Parse("umpk_test:phase_block");
    public static IEnumerable<object[]> Boundaries() => new[] { 766, 767, 768, 770, 777 }.Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task InitialConfigurationCollectsBothRegistriesUntilBeforePlay(int protocol)
    {
        List<(bool? Book, bool? Block)> observed = [];
        List<(bool? Book, bool? Block)> firstPlay = [];
        IPacket[] packets = [new ClientboundConfigUpdateTagsPacket([Blocks(protocol)]), new ClientboundConfigUpdateTagsPacket([Books(protocol)])];
        await using Session session = await Session.Create(protocol, "lectern", configurationPackets: packets, observePackets: (client, received) =>
        {
            if (received.Packet is ClientboundConfigUpdateTagsPacket)
                observed.Add(Membership(client, protocol));
            if (received.Phase == ProtocolPhase.Play && received.Packet is ClientboundPlayKeepAlivePacket && firstPlay.Count == 0)
                firstPlay.Add(Membership(client, protocol));
        });
        Assert.Equal(new (bool?, bool?)[] { (null, null), (null, null) }, observed);
        Assert.Equal(new (bool?, bool?)[] { (true, true) }, firstPlay);
        await AssertBookUse(session, true);
        await Disconnect(session);
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task ReentryWithoutTagsRetainsFactsAndEmptyCollectorResetsOnlyLegacyStatics(int protocol)
    {
        List<(bool? Book, bool? Block)> observed = [];
        await using Session session = await CreateObserved(protocol, observed);
        await Seed(session);
        await session.Reconfigure(null);
        Assert.Equal(((bool?)true, (bool?)true), Membership(session.Client, protocol));
        Assert.Empty(observed);
        await session.Reconfigure(null, [new ClientboundConfigUpdateTagsPacket([])]);
        Assert.Equal(new (bool?, bool?)[] { (true, true) }, observed);
        bool expected = protocol >= 768;
        Assert.Equal(((bool?)expected, (bool?)expected), Membership(session.Client, protocol));
        await session.RejoinWorld("lectern");
        await AssertBookUse(session, expected);
        await Disconnect(session);
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task AnyRegistryContentsSelectsTheNativeEmptyStaticPayloadBranch(int protocol)
    {
        List<(bool? Book, bool? Block)> observed = [];
        await using Session session = await CreateObserved(protocol, observed);
        for (int context = 0; context < 4; context++)
        {
            await Seed(session);
            observed.Clear();
            IPacket tag = new ClientboundConfigUpdateTagsPacket([context == 2 ? Books(protocol, members: false) : new TagRegistry(RegistryIds.Item, [])]);
            IPacket[] packets = context is 1 or 2 ? [UntrackedDamageTypeContents(), tag] : [tag];
            await session.Reconfigure(null, packets);
            Assert.Equal(new (bool?, bool?)[] { (true, true) }, observed);
            bool book = context == 1 && protocol >= 768;
            bool block = protocol >= 768;
            Assert.Equal(((bool?)book, (bool?)block), Membership(session.Client, protocol));
            await session.RejoinWorld("lectern");
            await AssertBookUse(session, book);
        }
        // The native contents branch can rebuild registries that this client does not own. Their previous tag facts become unknown even when no tag packet arrives.
        var otherTag = Identifier.Parse("umpk_test:unclassified_rebuild");
        await session.Send(new ClientboundUpdateTagsPacket([Books(protocol), Blocks(protocol), new TagRegistry(RegistryIds.Enchantment, [new TagEntry(otherTag, [0])])]));
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.RegistryTags.Contains(RegistryIds.Enchantment, otherTag, 0));
        observed.Clear();
        await session.Reconfigure(null, [UntrackedDamageTypeContents()]);
        Assert.Empty(observed);
        Assert.Equal(((bool?)true, (bool?)true), Membership(session.Client, protocol));
        Assert.Null(session.Client.State.RegistryTags.Contains(RegistryIds.Enchantment, otherTag, 0));
        await session.RejoinWorld("lectern");
        await AssertBookUse(session, true);
        // Preserve the old full-snapshot helper's known-empty block/item facts through the same contents-only commit.
        await session.Client.InvokeAsync(client => { client.State.RegistryTags.Install([]); return true; }, session.Token);
        await session.Reconfigure(null, [UntrackedDamageTypeContents()]);
        Assert.Equal(((bool?)false, (bool?)false), Membership(session.Client, protocol));
        await session.RejoinWorld("lectern");
        await AssertBookUse(session, false);
        await Disconnect(session);
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task LastPayloadReplacesOnlyItsRegistryWithoutEarlyPublication(int protocol)
    {
        List<(bool? Book, bool? Block)> observed = [];
        await using Session session = await CreateObserved(protocol, observed);
        await Seed(session);
        await session.Reconfigure(null,
        [
            new ClientboundConfigUpdateTagsPacket([Books(protocol)]),
            new ClientboundConfigUpdateTagsPacket([Blocks(protocol)]),
            new ClientboundConfigUpdateTagsPacket([Books(protocol, members: false)]),
        ]);
        Assert.Equal(new (bool?, bool?)[] { (true, true), (true, true), (true, true) }, observed);
        Assert.Equal(((bool?)false, (bool?)true), Membership(session.Client, protocol));
        await session.RejoinWorld("lectern");
        await AssertBookUse(session, false);
        await Disconnect(session);
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task UncommittedTagsDieWithTheirConnectionAndDoNotLeakIntoReconnect(int protocol)
    {
        var seenPending = new TaskCompletionSource<(bool? Book, bool? Block)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using Session session = await Session.Create(protocol, "lectern", observePackets: (client, received) =>
        {
            if (received.Packet is ClientboundConfigUpdateTagsPacket)
                seenPending.TrySetResult(Membership(client, protocol));
        });
        await Seed(session);
        await session.BeginReconfiguration();
        await session.SendConfiguration(new ClientboundConfigUpdateTagsPacket([Books(protocol, members: false)]));
        Assert.Equal(((bool?)true, (bool?)true), await seenPending.Task.WaitAsync(session.Token));
        await Disconnect(session);
        Assert.Equal(((bool?)null, (bool?)null), Membership(session.Client, protocol));
        await session.Restart("lectern");
        Assert.Equal(((bool?)null, (bool?)null), Membership(session.Client, protocol));
        await AssertBookUse(session, null);
        await Disconnect(session);
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task PlayUpdatesUseNativeRegistryScopeAndKnownEmptyDistinctions(int protocol)
    {
        await using Session session = await Session.Create(protocol, "lectern");
        await session.Send(new ClientboundUpdateTagsPacket([Books(protocol)]));
        Assert.Empty(await session.Barrier());
        Assert.Equal(((bool?)true, protocol < 768 ? false : (bool?)null), Membership(session.Client, protocol));
        await session.Send(new ClientboundUpdateTagsPacket([Blocks(protocol)]));
        Assert.Empty(await session.Barrier());
        Assert.Equal(((bool?)(protocol >= 768), (bool?)true), Membership(session.Client, protocol));
        await Seed(session);
        await session.Send(new ClientboundUpdateTagsPacket([new TagRegistry(RegistryIds.Item, [])]));
        Assert.Empty(await session.Barrier());
        Assert.Equal(((bool?)false, (bool?)(protocol >= 768)), Membership(session.Client, protocol));
        await Seed(session);
        await session.Send(new ClientboundUpdateTagsPacket([]));
        Assert.Empty(await session.Barrier());
        Assert.Equal(((bool?)(protocol >= 768), (bool?)(protocol >= 768)), Membership(session.Client, protocol));
        await AssertBookUse(session, protocol >= 768);
        await Disconnect(session);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MalformedModernPlayUpdatePublishesNeitherMembershipNorRegistryKnowledge(int fault)
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_5);
        await harness.ApplyAsync(new ClientboundUpdateTagsPacket([Blocks(770)]));
        TagRegistry[] groups = fault switch
        {
            0 => [Books(770), new(RegistryIds.Block, []), Blocks(770)],
            1 => [Books(770), new(RegistryIds.Block, [new(BlockTag, [1]), new(BlockTag, [2])])],
            _ => [Books(770), new(RegistryIds.Block, [new(BlockTag, [-1])])],
        };
        await Assert.ThrowsAsync<ProtocolViolationException>(async () => await harness.ApplyAsync(new ClientboundUpdateTagsPacket(groups)));
        Assert.True(harness.State.RegistryTags.Contains(RegistryIds.Block, BlockTag, Stone(770)));
        Assert.Null(harness.State.RegistryTags.Contains(RegistryIds.Item, BookTag, Book(770)));
    }

    private static Task<Session> CreateObserved(int protocol, List<(bool? Book, bool? Block)> observed) => Session.Create(protocol, "lectern", observePackets: (client, received) =>
    {
        if (received.Packet is ClientboundConfigUpdateTagsPacket)
            observed.Add(Membership(client, protocol));
    });

    private static async Task Seed(Session session)
    {
        await session.Send(new ClientboundUpdateTagsPacket([Books(session.Protocol), Blocks(session.Protocol)]));
        Assert.Empty(await session.Barrier());
        Assert.Equal(((bool?)true, (bool?)true), Membership(session.Client, session.Protocol));
    }

    private static (bool? Book, bool? Block) Membership(UmpkClient client, int protocol) =>
        (client.State.RegistryTags.Contains(RegistryIds.Item, BookTag, Book(protocol)), client.State.RegistryTags.Contains(RegistryIds.Block, BlockTag, Stone(protocol)));
    private static int Book(int protocol) => JavaGameData.Registries(protocol).Items[Identifier.Minecraft("writable_book")].NetworkId;
    private static int Stone(int protocol) => JavaGameData.Registries(protocol).Blocks[Identifier.Minecraft("stone")].NetworkId;
    private static TagRegistry Books(int protocol, bool members = true) => new(RegistryIds.Item, [new TagEntry(BookTag, members ? [Book(protocol)] : [])]);
    private static TagRegistry Blocks(int protocol) => new(RegistryIds.Block, [new TagEntry(BlockTag, [Stone(protocol)])]);

    private static ClientboundConfigRegistryDataPacket UntrackedDamageTypeContents() => new(Identifier.Minecraft("damage_type"),
    [
        new PackedRegistryEntry(Identifier.Minecraft("generic"), new NbtCompound
        {
            ["message_id"] = new NbtString("generic"),
            ["scaling"] = new NbtString("when_caused_by_living_non_player"),
            ["exhaustion"] = new NbtFloat(0),
        }),
    ]);

    private static async Task AssertBookUse(Session session, bool? membership)
    {
        Assert.Equal(membership, session.Client.State.RegistryTags.Contains(RegistryIds.Item, BookTag, Book(session.Protocol)));
        await session.Hold("writable_book");
        ItemStack stack = session.Client.State.Inventory.GetSlot(36);
        int state = session.Client.State.World.GetBlock(Session.Target).StateId;
        BlockUseResult result = membership is true ? BlockUseResult.Success : membership is false ? BlockUseResult.Consume : BlockUseResult.Unknown;
        BlockUseSwing swing = result == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main), session.Token);
        List<IPacket> packets = await session.Use(Hand.Main);
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((Session.Target, (int)Direction.Up, 0, .5F, 1F, .5F), (use.Position, use.Face, use.Hand, use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside);
        if (swing == BlockUseSwing.Packet)
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.Equal(new BlockUsePrediction(true, result, swing), prediction);
        Assert.Equal(stack, session.Client.State.Inventory.GetSlot(36));
        Assert.Equal(state, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
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
