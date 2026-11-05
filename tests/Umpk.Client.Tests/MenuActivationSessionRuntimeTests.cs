using System.Buffers;
using System.IO.Pipelines;
using Umpk.Client.Actions;
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

public sealed class MenuActivationSessionRuntimeTests
{
    [Theory]
    [InlineData(47, "crafting_table", Hand.Main, true, false)]
    [InlineData(107, "furnace", Hand.Main, true, false)]
    [InlineData(393, "chest", Hand.Main, true, false)]
    [InlineData(477, "crafting_table", Hand.Main, true, false)]
    [InlineData(498, "chest", Hand.Main, true, false)]
    [InlineData(573, "crafting_table", Hand.Main, true, false)]
    [InlineData(765, "crafting_table", Hand.Main, true, false)]
    [InlineData(768, "crafting_table", Hand.Main, true, false)]
    [InlineData(776, "crafting_table", Hand.Main, true, false)]
    [InlineData(47, "lit_furnace", Hand.Main, true, false)]
    [InlineData(340, "lit_furnace", Hand.Main, true, false)]
    [InlineData(766, "furnace", Hand.Main, true, false)]
    [InlineData(777, "crafting_table", Hand.Main, false, false)]
    [InlineData(766, "chest", Hand.Off, false, false)]
    [InlineData(47, "furnace", Hand.Off, false, true)]
    public async Task PublicBlockUsePreservesNativeMenuSwingAndHandBoundaries(int protocol, string block, Hand hand, bool packetSwing, bool rejectedHand)
    {
        await using Session session = await Session.Create(protocol, block);
        int original = session.Client.State.World.GetBlock(Session.Target).StateId;
        if (rejectedHand)
        {
            await Assert.ThrowsAsync<ActionNotSupportedException>(() => session.Use(hand));
            Assert.Empty(await session.Barrier());
        }
        else
        {
            List<IPacket> sent = await session.Use(hand);
            Assert.Equal(packetSwing ? 2 : 1, sent.Count);
            if (protocol == 47)
            {
                var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(sent[0]);
                Assert.Equal(Session.Target, use.Position);
                Assert.Equal((int)Direction.Up, use.Face);
                Assert.True(use.HeldItem.IsEmpty);
                Assert.Equal((byte)8, use.CursorX);
                Assert.Equal((byte)16, use.CursorY);
                Assert.Equal((byte)8, use.CursorZ);
            }
            else
            {
                var use = Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
                Assert.Equal(Session.Target, use.Position);
                Assert.Equal((int)Direction.Up, use.Face);
                Assert.Equal((int)hand, use.Hand);
                Assert.Equal(.5F, use.CursorX);
                Assert.Equal(1F, use.CursorY);
                Assert.Equal(.5F, use.CursorZ);
                Assert.False(use.Inside);
            }
            if (packetSwing)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(sent[1]).Hand);
        }
        Assert.Equal(original, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Equal(Identifier.Minecraft("stone"), session.Client.State.World.GetBlock(Session.Target.Above()).Block.Id);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
        Assert.True(session.Client.State.Inventory.GetSlot(36).IsEmpty);
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await session.Server.NextFrameAsync(session.Token);
        });
    }

    public static IEnumerable<object[]> ContextCases()
    {
        foreach (int protocol in new[] { 47, 107, 393, 477, 498, 573, 765, 766, 768, 777 })
            foreach (int scenario in Enumerable.Range(0, 8))
                if (protocol != 47 || scenario is not (3 or 4 or 5))
                    yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task MenuActivationKeepsSecondaryUsePermissionsCooldownAndMissingTerrainDistinct(int protocol, int scenario)
    {
        await using Session session = await Session.Create(protocol, "chest", terrain: scenario != 6);
        bool sneaking = scenario is 0 or 1 or 5;
        if (scenario is 1 or 2 or 3)
            await session.Hold("stick");
        if (scenario == 7)
            await session.Hold("stone");
        if (scenario == 5)
            await session.Offhand("stick");
        if (sneaking)
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.True(session.Client.State.Self.Sneaking);
            Assert.Empty(await session.Barrier());
        }
        if (scenario == 2)
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (scenario == 3)
            await session.Cooldown();
        Hand hand = scenario == 4 ? Hand.Off : Hand.Main;
        List<IPacket> sent = await session.Use(hand);
        bool activates = scenario is not (1 or 5 or 6) && (scenario != 4 || protocol < 766);
        Assert.Equal(activates && protocol < 777 ? 2 : 1, sent.Count);
        Assert.True(sent[0] is ServerboundLegacyBlockPlacePacket or ServerboundUseItemOnPacket);
        if (sent.Count == 2)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(sent[1]).Hand);
        Assert.Equal(Identifier.Minecraft("chest"), session.Client.State.World.GetBlock(Session.Target).Block.Id);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(Session.Target));
        if (scenario == 6)
        {
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
    }

    [Fact]
    public async Task ObservedLockedChestNbtStillDoesNotPredictServerMenuOpening()
    {
        await using Session session = await Session.Create(766, "chest");
        await session.LockedChest();
        List<IPacket> sent = await session.Use(Hand.Main);
        Assert.Equal(2, sent.Count);
        Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
        Assert.IsType<ServerboundSwingPacket>(sent[1]);
        Assert.Equal("native-lock", session.Client.State.World.GetBlockEntityNbt(Session.Target)!.GetString("Lock"));
        Assert.Equal(Identifier.Minecraft("chest"), session.Client.State.World.GetBlock(Session.Target).Block.Id);
    }

    private sealed class Session : IAsyncDisposable
    {
        internal static readonly BlockPos Target = new(1, 65, 1);
        private static readonly Guid RequestedId = Guid.Parse("0baf5574-d2d0-4769-9c4a-0b02c326266f");
        private static readonly Guid AssignedId = Guid.Parse("f8cffac4-a661-4e6f-a9ca-14b9bf1f2905");
        internal FakeJavaServer Server { get; } = FakeJavaServer.Create();
        internal ManualTickSource Ticks { get; } = new();
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
                .UseProfile(new GameProfile(RequestedId, "MenuSession"))
                .UseConnectionFactory(new PipeFactory(Server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(Ticks)
                .ConfigureFeatures(features => { features.Physics = false; features.Pathfinding = false; })
                .Build();
        }

        internal static async Task<Session> Create(int protocol, string block, bool terrain = true)
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
                if (terrain)
                    await session.Terrain();
                await session.Block(Target, block);
                await session.Block(Target.Above(), "stone");
                await session.Hold(null);
                Assert.Empty(await session.Barrier());
                Assert.Null(session.Client.State.World.GetBlockEntityNbt(Target));
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



        internal async Task Hold(string? name)
        {
            ItemStack stack = name is null ? ItemStack.Empty : new(Client.State.Registries!.Items[Identifier.Minecraft(name)], 1);
            await Send(new ClientboundContainerSetSlotPacket(0, 0, 36, stack));
            Assert.Empty(await Barrier());
            Assert.Equal(stack.IsEmpty, Client.State.Inventory.GetSlot(36).IsEmpty);
            if (!stack.IsEmpty)
                Assert.Equal(stack.Item.Id, Client.State.Inventory.GetSlot(36).Item.Id);
        }

        internal async Task Offhand(string name)
        {
            ItemStack stack = new(Client.State.Registries!.Items[Identifier.Minecraft(name)], 1);
            await Send(new ClientboundContainerSetSlotPacket(0, 0, 45, stack));
            Assert.Empty(await Barrier());
            Assert.Equal(stack.Item.Id, Client.State.Inventory.GetSlot(45).Item.Id);
        }

        internal async Task Cooldown()
        {
            ItemStack stack = Client.State.Inventory.GetSlot(36);
            await Send(new ClientboundCooldownPacket(_protocol >= 768 ? stack.Item.Id : default, 100, _protocol < 768 ? stack.Item.NetworkId : null));
            Assert.Empty(await Barrier());
            Assert.True(await Client.InvokeAsync(client => client.State.Self.IsItemOnCooldown(client.State.Inventory.GetSlot(36), _protocol), Token));
        }

        internal async Task LockedChest()
        {
            // The extracted protocol766 block_entity_type registry assigns chest1; this registry is not exposed by the static runtime snapshot.
            const int id = 1;
            var nbt = new NbtCompound { ["id"] = new NbtString("minecraft:chest"), ["Lock"] = new NbtString("native-lock") };
            await Send(new ClientboundBlockEntityDataPacket(Target, id, nbt));
            Assert.Empty(await Barrier());
            Assert.Equal("native-lock", Client.State.World.GetBlockEntityNbt(Target)!.GetString("Lock"));
        }

        internal Task Block(BlockPos position, string name)
            => Send(new ClientboundBlockUpdatePacket(position, Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value.DefaultStateId));





        internal async Task<List<IPacket>> Use(Hand hand)
        {
            Task action = Client.Actions.Interaction.PlaceBlockAsync(Target, Direction.Up, new Vec3d(.5, 1, .5), hand, ct: Token);
            await Client.InvokeAsync(_ => true, Token);
            List<IPacket> packets = await Barrier();
            if (_protocol >= 759)
            {
                ServerboundUseItemOnPacket use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
                Assert.False(action.IsCompleted);
                await Client.InvokeAsync(_ => true, Token);
                await Send(new ClientboundBlockChangedAckPacket(use.Sequence));
            }
            await action.WaitAsync(Token);
            return packets;
        }

        private async Task Terrain()
        {
            byte[] bytes;
            if (_protocol == 47)
                bytes = ChunkCodecs.EncodeStructural(new LegacyChunkWireBody(0, 0, true, 16, [new int[4096]], new byte[4096 + 256]));
            else if (_protocol is >= 477 and <= 573)
            {
                var empty = new ChunkPalettedContainerWire(4, 0, [0], new long[256]);
                bytes = ChunkCodecs.EncodeStructural(new Pre1_18ChunkWireBody(0, 0, true, 16, new NbtCompound(), [new ChunkSectionWire(0, null, empty, empty)], _protocol < 573 ? new byte[1024] : [], [0], _protocol == 573 ? new byte[4096] : []));
            }
            else if (_protocol < 477)
            {
                var body = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(body);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteBool(true);
                writer.WriteVarInt(0);
                int biomes = _protocol >= 393 ? 1024 : 256;
                writer.WriteVarInt(biomes);
                writer.WriteBytes(new byte[biomes]);
                if (_protocol >= 110)
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
                case ClientboundCooldownPacket cooldown:
                    Assert.Equal(cooldown, Assert.IsType<ClientboundCooldownPacket>(decoded));
                    break;
                case ClientboundBlockEntityDataPacket blockEntity:
                    var be = Assert.IsType<ClientboundBlockEntityDataPacket>(decoded);
                    Assert.Equal(blockEntity.Position, be.Position);
                    Assert.Equal(blockEntity.BlockEntityType, be.BlockEntityType);
                    Assert.Equal("native-lock", Assert.IsType<NbtCompound>(be.Nbt).GetString("Lock"));
                    break;
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

        internal async Task<List<IPacket>> Barrier()
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
                if (packet is ServerboundLegacyBlockPlacePacket or ServerboundUseItemOnPacket or ServerboundSwingPacket or ServerboundPunchPacket)
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
}
