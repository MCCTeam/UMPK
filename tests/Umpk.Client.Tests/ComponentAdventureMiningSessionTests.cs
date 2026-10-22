using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ComponentAdventureMiningSessionTests
{
    [Theory]
    [InlineData(766, true)]
    [InlineData(767, true)]
    [InlineData(768, true)]
    [InlineData(769, true)]
    [InlineData(766, false)]
    [InlineData(767, false)]
    [InlineData(768, false)]
    [InlineData(769, false)]
    public async Task FramedCanBreakBlockSetPermitsOrdinaryAdventureMining(int protocol, bool adventure)
    {
        await using Session session = await Session.Create(protocol);
        GameMode mode = adventure ? GameMode.Adventure : GameMode.Survival;
        await session.Mode(mode, instantBuild: false);
        await session.Info(mode);
        await session.HoldStonePermission();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        bool passed = false;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> initial = await session.Tick();
            ServerboundPlayerActionPacket start = Assert.Single(initial.OfType<ServerboundPlayerActionPacket>());
            AssertAction(start, protocol, 0);
            Assert.Equal(2, initial.OfType<ServerboundSwingPacket>().Count());
            ServerboundPlayerActionPacket? stop = null;
            for (int tick = 1; tick < 6; tick++)
            {
                List<IPacket> sent = await session.Tick();
                var actions = sent.OfType<ServerboundPlayerActionPacket>().ToArray();
                Assert.Single(sent.OfType<ServerboundSwingPacket>());
                if (tick < 5)
                    Assert.Empty(actions);
                else
                    stop = Assert.Single(actions);
            }
            Assert.NotNull(stop);
            AssertAction(stop, protocol, 2);
            await session.Complete(action, stop);
            Assert.False(session.Client.State.World.GetBlock(Session.Target).IsAir);
            passed = true;
        }
        finally
        {
            await caller.CancelAsync();
            try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (!passed) { }
        }
    }

    public static IEnumerable<object[]> DeniedComponents()
    {
        foreach (int protocol in new[] { 766, 767, 768, 769 })
            foreach (string kind in new[] { "absent", "nonmatch" })
                yield return [protocol, kind];
    }

    [Theory]
    [MemberData(nameof(DeniedComponents))]
    public async Task KnownDenialRetainsHeldRequestAndAChangedWireComponentCanStartIt(int protocol, string kind)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.HoldPermission(kind);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        bool passed = false;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> denied = await session.Tick();
            Assert.Empty(denied.OfType<ServerboundPlayerActionPacket>());
            Assert.Single(denied.OfType<ServerboundSwingPacket>());
            await session.HoldStonePermission();
            List<IPacket> allowed = await session.Tick();
            AssertAction(Assert.Single(allowed.OfType<ServerboundPlayerActionPacket>()), protocol, 0);
            Assert.Single(allowed.OfType<ServerboundSwingPacket>());
            Assert.False(action.IsCompleted);
            passed = true;
        }
        finally
        {
            await caller.CancelAsync();
            try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (!passed) { }
        }
    }

    [Theory]
    [InlineData(766, false)]
    [InlineData(767, false)]
    [InlineData(768, false)]
    [InlineData(769, false)]
    [InlineData(766, true)]
    [InlineData(767, true)]
    [InlineData(768, true)]
    [InlineData(769, true)]
    public async Task TagReloadKeepsCurrentComponentCacheAndEqualWireReplacementRecomputes(int protocol, bool initiallyAllowed)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.Tags(initiallyAllowed);
        await session.HoldPermission("tag");
        await AssertInitialPermission(session, initiallyAllowed);
        await session.Tags(!initiallyAllowed);
        await AssertInitialPermission(session, initiallyAllowed);
        await session.HoldPermission("tag");
        await AssertInitialPermission(session, !initiallyAllowed, sameCachedTarget: initiallyAllowed);
    }

    private static async Task AssertInitialPermission(Session session, bool allowed, bool sameCachedTarget = false)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await session.Tick();
            if (allowed)
                Assert.Collection(sent, packet => AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), session.Protocol, 0), AssertMainHandSwing, AssertMainHandSwing);
            else if (sameCachedTarget)
                Assert.Collection(sent, AssertMainHandSwing, AssertMainHandSwing);
            else
                AssertMainHandSwing(Assert.Single(sent));
            Assert.False(action.IsCompleted);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
        }
    }

    private static void AssertMainHandSwing(IPacket packet)
    {
        ServerboundSwingPacket swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.Equal(0, swing.Hand);
        Assert.True(swing.HasHand);
    }

    public static IEnumerable<object[]> UnsupportedContexts()
    {
        foreach (int protocol in new[] { 766, 767, 768, 769 })
            foreach (string kind in new[] { "range", "nbt" })
                yield return [protocol, kind];
    }

    [Theory]
    [MemberData(nameof(UnsupportedContexts))]
    public async Task NativePropertyOrderAndFullBlockEntitySaveRemainExplicitlyUnavailable(int protocol, string kind)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.Block(Session.Target, kind == "range" ? "oak_log" : "chest");
        await session.HoldPermission(kind);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        Assert.Empty((await session.Tick()).OfType<ServerboundPlayerActionPacket>());
        InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
        Assert.Contains("requires unavailable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protocol765RetainsItsFlattenedNbtPermission()
    {
        await using Session session = await Session.Create(765);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.HoldStonePermission();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await session.Tick();
            AssertAction(Assert.Single(sent.OfType<ServerboundPlayerActionPacket>()), 765, 0);
            Assert.Equal(2, sent.OfType<ServerboundSwingPacket>().Count());
        }
        finally
        {
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
        }
    }

    [Fact]
    public async Task Protocol770RemainsOutsideTheBoundComponentPayloadAndPermissionSlice()
    {
        await using Session session = await Session.Create(770);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.Hold("diamond_pickaxe");
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        Assert.Empty(await session.Tick());
        InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
        Assert.Equal("This game mode requires an allowed block interaction before mining.", error.Message);
    }

    internal static void AssertAction(ServerboundPlayerActionPacket action, int protocol, int expected)
    {
        Assert.Equal(expected, action.Action);
        Assert.Equal(Session.Target, action.Position);
        Assert.Equal((byte)Direction.North, action.Direction);
        if (protocol >= 759)
        {
            if (expected == 1)
                Assert.Equal(0, action.Sequence);
            else
                Assert.True(action.Sequence > 0);
        }
        else
            Assert.Null(action.Sequence);
    }

    internal sealed class Session : IAsyncDisposable
    {
        internal static readonly BlockPos Target = new(1, 65, 1);
        private static readonly Guid RequestedId = Guid.Parse("0baf5574-d2d0-4769-9c4a-0b02c326266f");
        private static readonly Guid AssignedId = Guid.Parse("f8cffac4-a661-4e6f-a9ca-14b9bf1f2905");
        internal FakeJavaServer Server { get; } = FakeJavaServer.Create();
        private ObservedTicks Ticks { get; } = new();
        internal UmpkClient Client { get; }
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(40));
        private readonly JavaVersion _version;
        private readonly int _protocol;
        internal int Protocol => _protocol;
        private long _barrier;
        internal CancellationToken Token => _budget.Token;
        private PacketCodecContext Context => new(Client.State.Registries!, IConnectionCodecState.Empty);

        private Session(int protocol)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out _version!));
            Client = new UmpkClientBuilder().UseVersion(_version)
                .UseProfile(new GameProfile(RequestedId, "CreativeSession"))
                .UseConnectionFactory(new PipeFactory(Server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(Ticks)
                .ConfigureFeatures(features => { features.Physics = true; features.Pathfinding = false; })
                .Build();
        }

        internal static async Task<Session> Create(int protocol)
        {
            var session = new Session(protocol);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session.Token), session.DriveToPlay());
                var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable join = session.Client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
                var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
                await session.Send(new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, new LegacyLoginFields(0, 2, "flat")));
                await joined.Task.WaitAsync(session.Token);
                var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable correction = session.Client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
                var position = new Vec3d(2.5, 65, 2.5);
                await session.Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 1, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
                await corrected.Task.WaitAsync(session.Token);
                Assert.Equal(AssignedId, session.Client.State.Self.Uuid);
                Assert.NotEqual(RequestedId, session.Client.State.Self.Uuid);
                await session.Terrain();
                for (int x = 1; x <= 3; x++)
                    for (int z = 1; z <= 3; z++)
                        await session.Block(new BlockPos(x, 64, z), "stone");
                await session.Block(Target, "stone");
                if (protocol >= 766)
                {
                    int stone = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId;
                    await session.Send(new ClientboundUpdateTagsPacket([new TagRegistry(RegistryIds.Block, [new TagEntry(Identifier.Minecraft("mineable/pickaxe"), [stone]), new TagEntry(Identifier.Minecraft("incorrect_for_diamond_tool"), [])])]));
                }
                await session.Mode(GameMode.Creative, instantBuild: true);
                await session.Info(GameMode.Creative);
                await session.Hold("diamond_sword");
                Assert.Empty(await session.Barrier());
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
                Assert.True(session.Client.State.Self.OnGround);
                Assert.True(session.Client.State.EntityMembership.HasColumn(Target));
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        private async Task DriveToPlay()
        {
            await Server.NextFrameAsync(Token);
            Server.ServerConnection.SetPhase(ProtocolPhase.Login);
            await Server.NextFrameAsync(Token);
            await ScriptedServer.SendAsync(Server, _version.Protocol, ProtocolPhase.Login,
                new ClientboundLoginFinishedPacket(AssignedId, "AssignedPlayer", [], null), Token);
            if (_version.Features.ConfigurationPhase)
            {
                await Server.NextFrameAsync(Token);
                Server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
                await Server.NextFrameAsync(Token);
                await ScriptedServer.SendAsync(Server, _version.Protocol, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), Token);
                await Server.NextFrameAsync(Token);
            }
            Server.ServerConnection.SetPhase(ProtocolPhase.Play);
            await ScriptedServer.SendPlayReadinessFrameAsync(Server, _version.Protocol, Token);
        }

        internal async Task Mode(GameMode mode, bool instantBuild)
        {
            await Send(new ClientboundGameEventPacket(3, (float)mode));
            await Send(new ClientboundPlayerAbilitiesPacket((byte)(instantBuild ? 8 : 0), .05F, .1F));
            Assert.Empty(await Barrier());
            Assert.Equal(mode, Client.State.Self.GameMode);
            Assert.Equal(instantBuild, Client.State.Self.InstantBuild);
        }

        internal async Task Info(GameMode mode)
        {
            Guid uuid = Client.State.Self.Uuid;
            if (_protocol < 761)
                await Send(new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.AddPlayer, [new LegacyPlayerListEntry(uuid, "CreativeSession", [], (int)mode, 0, null)]));
            else
                await Send(new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode, [new PlayerInfoEntry(uuid, "CreativeSession", [], false, null, mode, true, 0, null, 0, true)]));
            Assert.Empty(await Barrier());
            Assert.True(Client.State.TabList.TryGet(uuid, out var info));
            Assert.Equal(mode, info.GameMode);
        }

        internal async Task Hold(string name)
        {
            ItemStack stack = new(Client.State.Registries!.Items[Identifier.Minecraft(name)], 1);
            await Send(new ClientboundContainerSetSlotPacket(0, 0, 36, stack));
            Assert.Empty(await Barrier());
            Assert.Equal(stack.Item.Id, Client.State.Inventory.GetSlot(36).Item.Id);
            Assert.Equal(1, Client.State.Inventory.GetSlot(36).Count);
        }

        internal Task HoldStonePermission() => HoldPermission("stone");

        internal async Task HoldPermission(string kind, bool showInTooltip = true)
        {
            ItemStack stack = new(Client.State.Registries!.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
            if (_protocol == 765)
                stack = stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(new NbtCompound { ["CanDestroy"] = new NbtList(NbtTagType.String) { new NbtString("minecraft:stone") } }));
            else if (kind != "absent")
            {
                string blockName = kind switch { "nonmatch" => "dirt", "range" => "oak_log", "nbt" => "chest", _ => "stone" };
                int block = Client.State.Registries.Blocks[Identifier.Minecraft(blockName)].NetworkId;
                StatePropertyMatcher[]? properties = kind == "range" ? [new("axis", false, MinValue: "x", MaxValue: "z")] : null;
                NbtCompound? nbt = kind == "nbt" ? new NbtCompound { ["CustomName"] = new NbtString("unavailable full native save") } : null;
                stack = stack.With(DataComponents.CanBreak, new AdventureModePredicateComponent([new BlockPredicateEntry(kind == "tag" ? new HolderSetRef(Identifier.Minecraft("permission_probe"), []) : new HolderSetRef(null, [block]), properties, nbt)], showInTooltip));
            }
            await Send(new ClientboundContainerSetSlotPacket(0, 0, 36, stack));
            Assert.Empty(await Barrier());
            ItemStack actual = Client.State.Inventory.GetSlot(36);
            if (_protocol == 765)
                Assert.Equal(stack.Components.Get(DataComponents.LegacyNbt)!.Nbt, actual.Components.Get(DataComponents.LegacyNbt)!.Nbt);
            else if (kind == "absent")
                Assert.Null(actual.Components.Get(DataComponents.CanBreak));
            else
            {
                AdventureModePredicateComponent observed = actual.Components.Get(DataComponents.CanBreak)!;
                BlockPredicateEntry expected = Assert.Single(stack.Components.Get(DataComponents.CanBreak)!.Predicates);
                BlockPredicateEntry predicate = Assert.Single(observed.Predicates);
                Assert.Equal(expected.Blocks!.Ids, predicate.Blocks!.Ids);
                Assert.Equal(expected.Blocks.Tag, predicate.Blocks.Tag);
                Assert.Equal(expected.Properties, predicate.Properties);
                Assert.Equal(expected.Nbt, predicate.Nbt);
                Assert.Equal(showInTooltip, observed.ShowInTooltip);
            }
        }

        internal async Task Tags(bool allowed)
        {
            int stone = Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId;
            await Send(new ClientboundUpdateTagsPacket([new TagRegistry(RegistryIds.Block, [new TagEntry(Identifier.Minecraft("mineable/pickaxe"), [stone]), new TagEntry(Identifier.Minecraft("incorrect_for_diamond_tool"), []), new TagEntry(Identifier.Minecraft("permission_probe"), allowed ? [stone] : [])])]));
            Assert.Empty(await Barrier());
        }

        internal Task Block(BlockPos position, string name)
            => Send(new ClientboundBlockUpdatePacket(position, Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value.DefaultStateId));

        internal async Task<List<IPacket>> Tick()
        {
            await Ticks.Fire(Token);
            return await Barrier();
        }

        internal async Task Complete(Task action, ServerboundPlayerActionPacket packet)
        {
            if (_protocol >= 759)
            {
                Assert.False(action.IsCompleted);
                await Client.InvokeAsync(_ => true, Token);
                await Send(new ClientboundBlockChangedAckPacket(packet.Sequence!.Value));
            }
            await action.WaitAsync(Token);
        }

        private async Task Terrain()
        {
            byte[] bytes;
            if (_protocol == 47)
                bytes = ChunkCodecs.EncodeStructural(new LegacyChunkWireBody(0, 0, true, 16, [new int[4096]], new byte[4096 + 256]));
            else if (_protocol == 393)
            {
                var section = new ArrayBufferWriter<byte>();
                var blocks = new PacketWriter(section);
                blocks.WriteByte(4);
                blocks.WriteVarInt(1);
                blocks.WriteVarInt(0);
                blocks.WriteVarInt(256);
                blocks.WriteBytes(new byte[2048 + 4096 + 1024]);
                var body = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(body);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteBool(true);
                writer.WriteVarInt(16);
                writer.WriteVarInt(section.WrittenCount);
                writer.WriteBytes(section.WrittenSpan);
                writer.WriteVarInt(0);
                bytes = body.WrittenSpan.ToArray();
            }
            else
            {
                var empty = new ChunkPalettedContainerWire(0, 0, null, []);
                var sections = Enumerable.Range(0, Client.State.World.Dimension.SectionCount).Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
                byte[] tail = new byte[_protocol <= 762 ? 8 : 7];
                ChunkWireBody body = _protocol >= 770
                    ? new ModernChunkWireBody(0, 0, _protocol >= 775, [], sections, [], tail)
                    : _protocol >= 768 ? new NbtHeightmapChunkWireBody(0, 0, new NbtCompound(), sections, [], tail)
                    : new Protocol764To767ChunkWireBody(0, 0, new NbtCompound(), sections, [], tail, _protocol >= 764 ? NbtWireFormat.JavaUnnamedRoot : NbtWireFormat.JavaNamedRoot);
                bytes = ChunkCodecs.EncodeStructural(body);
            }
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, Context));
            Assert.True(packet.FullChunk);
            Assert.Equal(0, packet.ChunkX);
            Assert.Equal(0, packet.ChunkZ);
            Assert.Equal(0, packet.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
            Assert.Equal(bytes, Encode(codec, packet));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
            await Send(packet);
            Assert.Empty(await Barrier());
            Assert.NotNull(Client.State.World.GetColumn(Target));
            Assert.True(Client.State.EntityMembership.HasColumn(Target));
        }

        private async Task Send(IPacket packet)
        {
            string identifier = packet switch
            {
                ClientboundPlayerAbilitiesPacket when _protocol == 47 => "abilities",
                ClientboundContainerSetSlotPacket when _protocol == 47 => "set_slot",
                _ => packet.Type.Id.Path,
            };
            var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft(identifier));
            Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented);
            byte[] bytes = Encode(codec, packet);
            object decoded = codec.Decode(bytes, Context);
            switch (packet)
            {
                case ClientboundLoginPacket login:
                    Assert.Equal(login.PlayerId, Assert.IsType<ClientboundLoginPacket>(decoded).PlayerId);
                    break;
                case ClientboundPlayerPositionPacket position:
                    var observed = Assert.IsType<ClientboundPlayerPositionPacket>(decoded);
                    Assert.Equal(new Vec3d(position.X, position.Y, position.Z), observed.ModernValues?.Position ?? new Vec3d(observed.X, observed.Y, observed.Z));
                    break;
                case ClientboundGameEventPacket game:
                    Assert.Equal(game, Assert.IsType<ClientboundGameEventPacket>(decoded));
                    break;
                case ClientboundPlayerAbilitiesPacket abilities:
                    Assert.Equal(abilities, Assert.IsType<ClientboundPlayerAbilitiesPacket>(decoded));
                    break;
                case ClientboundContainerSetSlotPacket slot:
                    var item = Assert.IsType<ClientboundContainerSetSlotPacket>(decoded).Item;
                    Assert.Equal(slot.Item.Item.Id, item.Item.Id);
                    Assert.Equal(slot.Item.Count, item.Count);
                    if (slot.Item.Components.Get(DataComponents.CanBreak) is { } expectedPredicate)
                    {
                        AdventureModePredicateComponent actualPredicate = item.Components.Get(DataComponents.CanBreak)!;
                        Assert.Equal(expectedPredicate.ShowInTooltip, actualPredicate.ShowInTooltip);
                        BlockPredicateEntry expectedBlock = Assert.Single(expectedPredicate.Predicates);
                        BlockPredicateEntry actualBlock = Assert.Single(actualPredicate.Predicates);
                        Assert.Equal(expectedBlock.Blocks!.Ids, actualBlock.Blocks!.Ids);
                        Assert.Equal(expectedBlock.Blocks.Tag, actualBlock.Blocks.Tag);
                        Assert.Equal(expectedBlock.Properties, actualBlock.Properties);
                        Assert.Equal(expectedBlock.Nbt, actualBlock.Nbt);
                    }
                    break;
                case ClientboundBlockUpdatePacket block:
                    Assert.Equal(block, Assert.IsType<ClientboundBlockUpdatePacket>(decoded));
                    break;
                case ClientboundLegacyPlayerListItemPacket legacy:
                    var entry = Assert.Single(Assert.IsType<ClientboundLegacyPlayerListItemPacket>(decoded).Entries);
                    Assert.Equal(legacy.Entries[0].ProfileId, entry.ProfileId);
                    Assert.Equal(legacy.Entries[0].GameMode, entry.GameMode);
                    break;
                case ClientboundPlayerInfoUpdatePacket info:
                    var modern = Assert.Single(Assert.IsType<ClientboundPlayerInfoUpdatePacket>(decoded).Entries);
                    Assert.Equal(info.Entries[0].ProfileId, modern.ProfileId);
                    Assert.Equal(info.Entries[0].GameMode, modern.GameMode);
                    break;
            }
            Assert.Equal(bytes, Encode(codec, decoded));
            await Server.SendFrameAsync(pair.WireId, bytes, Token);
        }

        private byte[] Encode(BoundPacketCodec codec, object packet)
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, packet, Context);
            return output.WrittenSpan.ToArray();
        }

        private async Task<List<IPacket>> Barrier()
        {
            long nonce = ++_barrier;
            await Send(new ClientboundPlayKeepAlivePacket(nonce));
            List<IPacket> actions = [];
            while (true)
            {
                InboundFrame frame = await Server.NextFrameAsync(Token);
                var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
                Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
                var packet = Assert.IsAssignableFrom<IPacket>(codec.Decode(frame.Payload, Context));
                Assert.Equal(frame.Payload.ToArray(), Encode(codec, packet));
                if (packet is ServerboundPlayKeepAlivePacket { Id: var id } && id == nonce)
                    return actions;
                if (packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket)
                    actions.Add(packet);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Client.DisconnectAsync(Token);
                await Assert.ThrowsAsync<ConnectionClosedException>(async () => { while (true) await Server.NextFrameAsync(Token); });
            }
            finally
            {
                try { await Client.DisposeAsync(); }
                finally { await Server.DisposeAsync(); _budget.Dispose(); }
            }
        }
    }

    private sealed class PipeFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTicks : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        internal bool SessionCancelled { get; private set; }
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        internal async Task Fire(CancellationToken ct)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completion, ct);
            await completion.Task.WaitAsync(ct);
        }
        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            try
            {
                await foreach (TaskCompletionSource completion in _ticks.Reader.ReadAllAsync(ct))
                {
                    yield return ++sequence;
                    completion.TrySetResult();
                }
            }
            finally { SessionCancelled = ct.IsCancellationRequested; }
        }
    }
}
