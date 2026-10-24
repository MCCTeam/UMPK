using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Actions;
using Umpk.Client.Events;
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

namespace Umpk.Client.Tests.Support;

internal sealed class BlockUseSession : IAsyncDisposable
{
    internal static readonly BlockPos Target = new(1, 65, 1);
    private static readonly Guid RequestedId = Guid.Parse("0baf5574-d2d0-4769-9c4a-0b02c326266f");
    private static readonly Guid AssignedId = Guid.Parse("f8cffac4-a661-4e6f-a9ca-14b9bf1f2905");
    internal List<PacketReceived> FeatureFrames { get; } = [];
    internal List<bool?> VanillaMembershipAtFeatureEvent { get; } = [];
    internal FakeJavaServer Server { get; } = FakeJavaServer.Create();
    private ObservedTicks Ticks { get; } = new();
    internal UmpkClient Client { get; }
    private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(40));
    private readonly JavaVersion _version;
    private readonly int _protocol;
    private readonly bool _featureFlags;
    private long _barrier;
    internal CancellationToken Token => _budget.Token;
    private PacketCodecContext Context => new(Client.State.Registries!, IConnectionCodecState.Empty);

    private BlockUseSession(int protocol, bool physics, bool featureFlags)
    {
        _protocol = protocol;
        _featureFlags = featureFlags;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out _version!));
        Client = new UmpkClientBuilder().UseVersion(_version)
            .UseProfile(new GameProfile(RequestedId, "DoorSession"))
            .UseConnectionFactory(new PipeFactory(Server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(Ticks)
            .ConfigureFeatures(features => { features.Physics = physics; features.Pathfinding = false; })
            .Build();
        Client.Events.Subscribe<PacketReceived>(received =>
        {
            if (received.Packet is ClientboundConfigUpdateEnabledFeaturesPacket)
            {
                FeatureFrames.Add(received);
                VanillaMembershipAtFeatureEvent.Add(Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("stone"), _protocol));
            }
        });
    }

    internal static async Task<BlockUseSession> Create(int protocol, string block, bool terrain = true, bool physics = false, bool featureFlags = true)
    {
        var session = new BlockUseSession(protocol, physics, featureFlags);
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
            await session.Block(Target.Above(), "air");
            await session.Block(new BlockPos(2, 64, 2), "stone");
            await session.Hold(null);
            Assert.Empty(await session.Barrier());
            Assert.Null(session.Client.State.World.GetBlockEntityNbt(Target));
            await session.Ticks.Fire(session.Token);
            Assert.Equal(1L, session.Client.State.SessionTick);
            Assert.Empty(await session.Barrier());
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
            await Features(ProtocolPhase.Configuration);
            await ScriptedServer.SendAsync(Server, _version.Protocol, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), Token);
            await Server.NextFrameAsync(Token);
        }
        Server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await ScriptedServer.SendPlayReadinessFrameAsync(Server, _version.Protocol, Token);
        if (_protocol is >= 761 and < 764)
            await Features(ProtocolPhase.Play);
    }

    internal async Task Mode(GameMode mode, bool instantBuild)
    {
        await Send(new ClientboundGameEventPacket(3, (float)mode));
        await Send(new ClientboundPlayerAbilitiesPacket((byte)(instantBuild ? 8 : 0), .05F, .1F));
        Assert.Empty(await Barrier());
        Assert.Equal(mode, Client.State.Self.GameMode);
        Assert.Equal(instantBuild, Client.State.Self.InstantBuild);
    }



    internal async Task Hold(string? name, bool custom = false)
    {
        ItemStack stack = name is null ? ItemStack.Empty : new(Client.State.Registries!.Items[Identifier.Minecraft(name)], 1);
        if (custom)
            stack = _protocol >= 766 ? stack.With(DataComponents.Unbreakable, new UnbreakableComponent()) : stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(new NbtCompound { ["Unbreakable"] = new NbtByte(1) }));
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

    internal Task Block(BlockPos position, string name, bool upper = false, bool open = false, int? metadata = null)
    {
        BlockDefinition block = Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value;
        int state = block.DefaultStateId;
        if (_protocol < 393 && name.EndsWith("_door", StringComparison.Ordinal))
            state |= metadata ?? (upper ? 8 : open ? 4 : 0);
        else if (name.EndsWith("_door", StringComparison.Ordinal))
            state = Enumerable.Range(block.MinStateId, block.StateCount).First(id => block.TryGetPropertyValue(id, "half", out string half) && half == (upper ? "upper" : "lower") && block.TryGetPropertyValue(id, "open", out string value) && value == (open ? "true" : "false"));
        return Send(new ClientboundBlockUpdatePacket(position, state));
    }

    internal async Task Forget()
    {
        if (_protocol == 47)
        {
            var bytes = ChunkCodecs.EncodeStructural(new LegacyChunkWireBody(0, 0, true, 0, [], new byte[256]));
            var codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, Context));
            Assert.True(packet.FullChunk);
            Assert.Equal(0, packet.ChunkX);
            Assert.Equal(0, packet.ChunkZ);
            Assert.Equal(bytes, Encode(codec, packet));
            await Send(packet);
        }
        else
            await Send(new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        Assert.Empty(await Barrier());
        Assert.False(Client.State.EntityMembership.HasColumn(Target));
    }

    private Task Features(ProtocolPhase phase) => SendFeatures(phase, _featureFlags ? [Identifier.Minecraft("vanilla"), Identifier.Minecraft("update_1_20"), Identifier.Minecraft("update_1_21"), Identifier.Minecraft("winter_drop")] : [Identifier.Minecraft("vanilla")]);

    private async Task SendFeatures(ProtocolPhase phase, Identifier[] flags)
    {
        if (_protocol < 761)
            return;
        var registry = _version.Protocol.GetRegistry(phase, PacketFlow.Clientbound);
        var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft("update_enabled_features"));
        Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
        var packet = new ClientboundConfigUpdateEnabledFeaturesPacket(flags);
        byte[] bytes = Encode(codec, packet);
        var observed = Assert.IsType<ClientboundConfigUpdateEnabledFeaturesPacket>(codec.Decode(bytes, Context));
        Assert.Equal(packet.Features, observed.Features);
        Assert.Equal(bytes, Encode(codec, observed));
        await Server.SendFrameAsync(pair.WireId, bytes, Token);
    }





    internal async Task Reconfigure(Identifier[]? flags)
    {
        Assert.Empty(await Barrier());
        await Send(new ClientboundStartConfigurationPacket());
        InboundFrame acknowledged = await Server.NextFrameAsync(Token);
        var play = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        Assert.True(play.TryGetInbound(acknowledged.WireId, out BoundPacketCodec acknowledgmentCodec));
        Assert.Equal("configuration_acknowledged", acknowledgmentCodec.Type.Id.Path);
        object ack = acknowledgmentCodec.Decode(acknowledged.Payload, Context);
        Assert.Equal(acknowledged.Payload.ToArray(), Encode(acknowledgmentCodec, ack));
        Server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        if (flags is not null)
            await SendFeatures(ProtocolPhase.Configuration, flags);
        await ScriptedServer.SendAsync(Server, _version.Protocol, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), Token);
        InboundFrame finished = await Server.NextFrameAsync(Token);
        var config = _version.Protocol.GetRegistry(ProtocolPhase.Configuration, PacketFlow.Serverbound);
        Assert.True(config.TryGetInbound(finished.WireId, out BoundPacketCodec finishCodec));
        Assert.Equal("finish_configuration", finishCodec.Type.Id.Path);
        object finish = finishCodec.Decode(finished.Payload, Context);
        Assert.Equal(finished.Payload.ToArray(), Encode(finishCodec, finish));
        Server.ServerConnection.SetPhase(ProtocolPhase.Play);
        Assert.Empty(await Barrier());
    }

    internal async Task Respawn()
    {
        var spawn = new CommonPlayerSpawnInfo(0, "minecraft:the_nether", 0, 0, -1, false, false, null, 0, 63);
        await Send(new ClientboundRespawnPacket(spawn, 0, null));
        Assert.Empty(await Barrier());
    }

    internal Task Tick() => Ticks.Fire(Token);

    internal async Task<List<IPacket>> Use(Hand hand)
    {
        Task action = Client.Actions.Interaction.PlaceBlockAsync(Target, Direction.Up, new Vec3d(.5, 1, .5), hand, ct: Token);
        await Client.InvokeAsync(_ => true, Token);
        List<IPacket> packets = await Barrier();
        if (_protocol >= 759 && packets.Count > 0)
        {
            ServerboundUseItemOnPacket use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.False(action.IsCompleted);
            await Client.InvokeAsync(_ => true, Token);
            await Send(new ClientboundBlockChangedAckPacket(use.Sequence));
        }
        await action.WaitAsync(Token);
        return packets;
    }

    internal async Task Terrain(int chunkX = 0, int chunkZ = 0)
    {
        byte[] bytes;
        if (_protocol == 47)
            bytes = ChunkCodecs.EncodeStructural(new LegacyChunkWireBody(0, 0, true, 16, [new int[4096]], new byte[4096 + 256]));
        else if (_protocol >= 757)
        {
            var empty = new ChunkPalettedContainerWire(0, 0, null, []);
            var sections = Enumerable.Range(0, (Client.State.World.Dimension.MaxY - Client.State.World.Dimension.MinY) / 16)
                .Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
            byte[] tail = new byte[_protocol <= 762 ? 8 : 7];
            ChunkWireBody body = _protocol >= 770
                ? new ModernChunkWireBody(0, 0, _protocol >= 775, [], sections, [], tail)
                : _protocol >= 768 ? new NbtHeightmapChunkWireBody(0, 0, new NbtCompound(), sections, [], tail)
                : new Protocol764To767ChunkWireBody(0, 0, new NbtCompound(), sections, [], tail, _protocol >= 764 ? NbtWireFormat.JavaUnnamedRoot : NbtWireFormat.JavaNamedRoot);
            bytes = ChunkCodecs.EncodeStructural(body);
        }
        else if (_protocol >= 735)
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            writer.WriteInt(0);
            writer.WriteInt(0);
            if (_protocol < 755)
                writer.WriteBool(true);
            if (_protocol <= 736)
                writer.WriteBool(false);
            writer.WriteVarInt(0);
            writer.WriteNbt(new NbtCompound(), NbtWireFormat.JavaNamedRoot);
            if (_protocol <= 736)
                writer.WriteBytes(new byte[4096]);
            else
            {
                writer.WriteVarInt(1024);
                writer.WriteBytes(new byte[1024]);
            }
            writer.WriteVarInt(0);
            writer.WriteVarInt(0);
            bytes = output.WrittenSpan.ToArray();
        }
        else if (_protocol >= 477)
            bytes = ChunkCodecs.EncodeStructural(new Pre1_18ChunkWireBody(0, 0, true, 0, new NbtCompound(), [], _protocol < 573 ? new byte[1024] : [], [0], _protocol >= 573 ? new byte[4096] : []));
        else
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteBool(true);
            writer.WriteVarInt(0);
            int biomes = _protocol >= 393 ? 1024 : 256;
            writer.WriteVarInt(biomes);
            writer.WriteBytes(new byte[biomes]);
            if (_protocol >= 110)
                writer.WriteVarInt(0);
            bytes = output.WrittenSpan.ToArray();
        }
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), chunkX);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), chunkZ);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
        var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, Context));
        Assert.True(packet.FullChunk);
        Assert.Equal(chunkX, packet.ChunkX);
        Assert.Equal(chunkZ, packet.ChunkZ);
        Assert.Equal(0, packet.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
        Assert.Equal(bytes, Encode(codec, packet));
        Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
        await Send(packet);
        Assert.Empty(await Barrier());
        var cell = new BlockPos(chunkX * 16 + 1, 65, chunkZ * 16 + 1);
        Assert.NotNull(Client.State.World.GetColumn(cell));
        Assert.True(Client.State.EntityMembership.HasColumn(cell));
    }

    internal async Task Send(IPacket packet)
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
            case ClientboundAddPlayerPacket player:
                var added = Assert.IsType<ClientboundAddPlayerPacket>(decoded);
                Assert.Equal(player.EntityId, added.EntityId);
                Assert.Equal(player.Uuid, added.Uuid);
                Assert.Equal(new Vec3d(player.X, player.Y, player.Z), new Vec3d(added.X, added.Y, added.Z));
                Assert.Equal(player.Yaw, added.Yaw);
                Assert.Equal(player.Pitch, added.Pitch);
                break;
            case ClientboundSetEntityLinkPacket link:
                Assert.Equal(link, Assert.IsType<ClientboundSetEntityLinkPacket>(decoded));
                break;
            case ClientboundTeleportEntityPacket teleport:
                var moved = Assert.IsType<ClientboundTeleportEntityPacket>(decoded);
                Assert.Equal(teleport.EntityId, moved.EntityId);
                Assert.Equal(new Vec3d(teleport.X, teleport.Y, teleport.Z), new Vec3d(moved.X, moved.Y, moved.Z));
                Assert.Equal(teleport.Yaw, moved.Yaw);
                Assert.Equal(teleport.Pitch, moved.Pitch);
                break;
            case ClientboundRespawnPacket { Legacy: not null } legacyRespawn:
                var nativeRespawn = Assert.IsType<ClientboundRespawnPacket>(decoded);
                Assert.Equal(legacyRespawn.Legacy, nativeRespawn.Legacy);
                Assert.Null(nativeRespawn.SpawnInfo);
                break;
            case ClientboundRespawnPacket respawn:
                var changed = Assert.IsType<ClientboundRespawnPacket>(decoded);
                var expectedSpawn = Assert.IsType<CommonPlayerSpawnInfo>(respawn.SpawnInfo);
                var actualSpawn = Assert.IsType<CommonPlayerSpawnInfo>(changed.SpawnInfo);
                Assert.Equal(expectedSpawn.Dimension, actualSpawn.Dimension);
                Assert.Equal(expectedSpawn.GameType, actualSpawn.GameType);
                Assert.Equal(respawn.DataToKeep, changed.DataToKeep);
                break;
            case ClientboundForgetLevelChunkPacket forget:
                Assert.Equal(forget, Assert.IsType<ClientboundForgetLevelChunkPacket>(decoded));
                break;
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
                Assert.Equal(slot.Item.Components.HasNoPatch, item.Components.HasNoPatch);
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

    private sealed class PipeFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTicks : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        public async Task Fire(CancellationToken ct)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completed, ct);
            await completed.Task.WaitAsync(ct);
        }
        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            await foreach (TaskCompletionSource completed in _ticks.Reader.ReadAllAsync(ct))
            {
                yield return ++sequence;
                completed.TrySetResult();
            }
        }
    }
}
