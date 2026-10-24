using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
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

public sealed class CreativeMiningSessionRuntimeTests
{
    [Theory]
    [InlineData(47)]
    [InlineData(393)]
    [InlineData(757)]
    [InlineData(758)]
    [InlineData(766)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task FramedSessionAndItsClockOwnTheFailedCreativeGroupAndRetainedDelay(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        Task first = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        long before = session.Client.State.SessionTick;
        List<IPacket> initial = await session.Tick();
        Assert.Equal(before + 1, session.Client.State.SessionTick);
        Assert.Equal(3, initial.Count);
        int startIndex = protocol == 47 ? 1 : 0;
        ServerboundPlayerActionPacket start = Assert.IsType<ServerboundPlayerActionPacket>(initial[startIndex]);
        AssertAction(start, protocol, 0);
        Assert.Equal(2, initial.Count(packet => packet is ServerboundSwingPacket or ServerboundPunchPacket));
        foreach (IPacket packet in initial.Where(packet => packet is not ServerboundPlayerActionPacket))
        {
            if (protocol == 777)
                Assert.IsType<ServerboundPunchPacket>(packet);
            else
                Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packet).Hand);
        }
        await session.Complete(first, start);
        Assert.False(session.Client.State.World.GetBlock(Session.Target).IsAir);
        Assert.Empty(await session.Tick());

        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        await session.Hold("diamond_pickaxe");
        Task second = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<ServerboundPlayerActionPacket> normal = [];
        ServerboundPlayerActionPacket? stop = null;
        for (int tick = 1; tick <= 10; tick++)
        {
            List<IPacket> sent = await session.Tick();
            normal.AddRange(sent.OfType<ServerboundPlayerActionPacket>());
            ServerboundPlayerActionPacket? observed = sent.OfType<ServerboundPlayerActionPacket>().SingleOrDefault(action => action.Action == (protocol == 777 ? 3 : 2));
            if (tick < 10)
                Assert.Null(observed);
            else
                stop = observed;
        }
        Assert.NotNull(stop);
        Assert.Equal(new[] { 0, protocol == 777 ? 3 : 2 }, normal.Select(action => action.Action));
        foreach (ServerboundPlayerActionPacket action in normal)
            AssertAction(action, protocol, action.Action);
        await session.Complete(second, stop);
        Assert.False(session.Client.State.World.GetBlock(Session.Target).IsAir);
        Assert.Empty(await session.Tick());

        await session.Block(Session.Target, "bedrock");
        await session.Hold("stone");
        Task held = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        Assert.Contains(await session.Tick(), packet => packet is ServerboundPlayerActionPacket { Action: 0 });
        Assert.False(held.IsCompleted);
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => held.WaitAsync(session.Token));
        Assert.True(session.Ticks.SessionCancelled);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await session.Server.NextFrameAsync(session.Token);
        });
    }

    private static void AssertAction(ServerboundPlayerActionPacket action, int protocol, int expected)
    {
        Assert.Equal(expected, action.Action);
        Assert.Equal(Session.Target, action.Position);
        Assert.Equal((byte)Direction.North, action.Direction);
        if (protocol >= 759)
            Assert.True(action.Sequence > 0);
        else
            Assert.Null(action.Sequence);
    }

    private sealed class Session : IAsyncDisposable
    {
        internal static readonly BlockPos Target = new(1, 65, 1);
        private static readonly Guid RequestedId = Guid.Parse("0baf5574-d2d0-4769-9c4a-0b02c326266f");
        private static readonly Guid AssignedId = Guid.Parse("f8cffac4-a661-4e6f-a9ca-14b9bf1f2905");
        internal FakeJavaServer Server { get; } = FakeJavaServer.Create();
        internal ObservedTicks Ticks { get; } = new();
        internal UmpkClient Client { get; }
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(40));
        private readonly JavaVersion _version;
        private readonly int _protocol;
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
            try { await Client.DisposeAsync(); }
            finally { await Server.DisposeAsync(); _budget.Dispose(); }
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
