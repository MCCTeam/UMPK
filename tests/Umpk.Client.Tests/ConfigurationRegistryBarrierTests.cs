using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Real-driver coverage for known-pack honesty and the configuration-to-play registry barrier.</summary>
public sealed class ConfigurationRegistryBarrierTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly Identifier CustomWorld = new("umpk_test", "skyblock");
    private static readonly Identifier CustomType = new("umpk_test", "thin_sky");
    private static readonly Identifier DepthStrider = Identifier.Minecraft("depth_strider");
    private const int BootsMenuSlot = 8;

    private static JavaVersion Version => JavaVersions.V1_21_11;

    [Fact]
    public async Task UnownedKnownPack_IsDeclinedAndFullRegistriesReachTheFirstPlayPacket()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server);

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await DriveToConfigurationAsync(server, ct);

        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundSelectKnownPacksPacket(
            [new KnownPack("minecraft", "core", "1.21.11")]), PacketCodecContext.Registryless, ct);
        var selected = Assert.IsType<ServerboundSelectKnownPacksPacket>(
            DecodeServerbound(await server.NextFrameAsync(ct), ProtocolPhase.Configuration));
        Assert.Empty(selected.Packs);

        await SendAsync(server, ProtocolPhase.Configuration, DimensionRegistry(
            Dimension("minecraft:overworld", -64, 384, hasSkylight: true),
            Dimension(CustomType.ToString(), 48, 128, hasSkylight: true)), PacketCodecContext.Registryless, ct);
        await SendAsync(server, ProtocolPhase.Configuration, EnchantmentRegistry(), PacketCodecContext.Registryless, ct);

        Registry<EnchantmentDefinition> serverEnchantments = ServerEnchantments();
        Assert.True(serverEnchantments.TryGet(DepthStrider, out RegistryEntry<EnchantmentDefinition> strider));

        // Queue the first PLAY frame before accepting the finish acknowledgement. The driver's terminal read gate must install the session registries before it can decode this numeric enchantment id.
        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(),
            PacketCodecContext.Registryless, ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await SendAsync(server, ProtocolPhase.Play, BootsSlot(strider, level: 3),
            new PacketCodecContext(JavaGameData.Registries(Version.Version.Protocol), IConnectionCodecState.Empty), ct);

        Assert.IsType<ServerboundFinishConfigurationPacket>(
            DecodeServerbound(await server.NextFrameAsync(ct), ProtocolPhase.Configuration));
        await connect.WaitAsync(Budget, ct);

        ItemStack firstPlayBoots = await client.InvokeAsync(c => c.State.Inventory.PlayerSlots[BootsMenuSlot], ct);
        EnchantmentInstance enchantment = Assert.Single(firstPlayBoots.Enchantments);
        Assert.Equal(DepthStrider, enchantment.Enchantment.Id);
        Assert.Equal(3, enchantment.Level);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        await SendAsync(server, ProtocolPhase.Play, Join(CustomWorld.ToString(), dimensionTypeId: 1),
            PacketCodecContext.Registryless, ct);
        await joined.Task.WaitAsync(Budget, ct);

        (int MinY, int Height, string Type) world = await client.InvokeAsync(
            c => (c.State.World.Dimension.MinY, c.State.World.Dimension.Height,
                c.State.World.Dimension.Type.Id.ToString()), ct);
        Assert.Equal(48, world.MinY);
        Assert.Equal(128, world.Height);
        Assert.Equal(CustomType.ToString(), world.Type);
    }

    [Fact]
    public async Task MissingCustomDimensionDefinition_EndsTheSessionWithoutHalfApplyingJoin()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server);
        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Disconnected>(e => disconnected.TrySetResult(e.Info));

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await DriveToConfigurationAsync(server, ct);
        await SendAsync(server, ProtocolPhase.Configuration, DimensionRegistry(
            Dimension("minecraft:overworld", -64, 384, hasSkylight: true)), PacketCodecContext.Registryless, ct);
        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(),
            PacketCodecContext.Registryless, ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, Version.Protocol, ct);
        _ = await server.NextFrameAsync(ct); // finish_configuration
        await connect.WaitAsync(Budget, ct);

        await SendAsync(server, ProtocolPhase.Play, Join(CustomWorld.ToString(), dimensionTypeId: 7),
            PacketCodecContext.Registryless, ct);
        DisconnectInfo failure = await disconnected.Task.WaitAsync(Budget, ct);

        Assert.Equal(CloseReason.ProtocolViolation, failure.Reason);
        InvalidDataException cause = Assert.IsType<InvalidDataException>(failure.Fault);
        Assert.Contains("network id 7", cause.Message, StringComparison.Ordinal);
        Assert.False(client.State.Self.HasSpawned);
        Assert.False(client.State.HasWorld);
        Assert.Equal(ClientStatus.Disconnected, client.Status);
    }

    [Fact]
    public async Task ReentryInstallsReplacementRegistriesBeforeItsFirstPlayPacket()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server);

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await DriveToConfigurationAsync(server, ct);
        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(),
            PacketCodecContext.Registryless, ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, Version.Protocol, ct);
        Assert.IsType<ServerboundFinishConfigurationPacket>(
            DecodeServerbound(await server.NextFrameAsync(ct), ProtocolPhase.Configuration));
        await connect.WaitAsync(Budget, ct);

        await SendAsync(server, ProtocolPhase.Play, new ClientboundStartConfigurationPacket(),
            PacketCodecContext.Registryless, ct);
        Assert.IsType<ServerboundConfigurationAcknowledgedPacket>(
            DecodeServerbound(await server.NextFrameAsync(ct), ProtocolPhase.Play));
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);

        await SendAsync(server, ProtocolPhase.Configuration, EnchantmentRegistry(), PacketCodecContext.Registryless, ct);
        Registry<EnchantmentDefinition> serverEnchantments = ServerEnchantments();
        Assert.True(serverEnchantments.TryGet(DepthStrider, out RegistryEntry<EnchantmentDefinition> strider));

        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(),
            PacketCodecContext.Registryless, ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await SendAsync(server, ProtocolPhase.Play, BootsSlot(strider, level: 2),
            new PacketCodecContext(JavaGameData.Registries(Version.Version.Protocol), IConnectionCodecState.Empty), ct);
        Assert.IsType<ServerboundFinishConfigurationPacket>(
            DecodeServerbound(await server.NextFrameAsync(ct), ProtocolPhase.Configuration));

        ItemStack? boots = null;
        while (boots is null || boots.IsEmpty)
        {
            boots = await client.InvokeAsync(c => c.State.Inventory.PlayerSlots[BootsMenuSlot], ct);
            if (boots.IsEmpty)
                await Task.Delay(5, ct);
        }

        EnchantmentInstance enchantment = Assert.Single(boots.Enchantments);
        Assert.Equal(DepthStrider, enchantment.Enchantment.Id);
        Assert.Equal(2, enchantment.Level);
    }

    [Fact]
    public async Task InvalidRespawnDimension_EndsTheSessionAndKeepsThePreviousWorldCoherent()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server);

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await DriveToConfigurationAsync(server, ct);
        await SendAsync(server, ProtocolPhase.Configuration, DimensionRegistry(
            Dimension("minecraft:overworld", -64, 384, hasSkylight: true),
            Dimension(CustomType.ToString(), 48, 128, hasSkylight: true)), PacketCodecContext.Registryless, ct);
        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(),
            PacketCodecContext.Registryless, ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, Version.Protocol, ct);
        _ = await server.NextFrameAsync(ct); // finish_configuration
        await connect.WaitAsync(Budget, ct);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        await SendAsync(server, ProtocolPhase.Play, Join(CustomWorld.ToString(), dimensionTypeId: 1),
            PacketCodecContext.Registryless, ct);
        await joined.Task.WaitAsync(Budget, ct);

        int respawns = 0;
        client.Events.Subscribe<Respawned>(_ => Interlocked.Increment(ref respawns));
        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Disconnected>(e => disconnected.TrySetResult(e.Info));
        await SendAsync(server, ProtocolPhase.Play,
            new ClientboundRespawnPacket(Spawn(CustomWorld.ToString(), dimensionTypeId: 9), DataToKeep: 0, Legacy: null),
            PacketCodecContext.Registryless, ct);

        DisconnectInfo failure = await disconnected.Task.WaitAsync(Budget, ct);
        Assert.Equal(CloseReason.ProtocolViolation, failure.Reason);
        InvalidDataException cause = Assert.IsType<InvalidDataException>(failure.Fault);
        Assert.Contains("network id 9", cause.Message, StringComparison.Ordinal);
        Assert.Equal(0, Volatile.Read(ref respawns));
        Assert.True(client.State.Self.HasSpawned);
        Assert.True(client.State.HasWorld);
        Assert.Equal(48, client.State.World.Dimension.MinY);
        Assert.Equal(128, client.State.World.Dimension.Height);
        Assert.Equal(CustomType, client.State.World.Dimension.Type.Id);
        Assert.Equal(ClientStatus.Disconnected, client.Status);
    }

    [Fact]
    public async Task ValidReplacementRegistry_SupersedesThePreviousDimensionDefinition()
    {
        var harness = new ApplierHarness(Version, new ClientFeatures { Terrain = true });
        harness.State.Registries = JavaGameData.Registries(Version.Version.Protocol);

        await harness.ApplyAsync(DimensionRegistry(
            Dimension(CustomType.ToString(), 48, 128, hasSkylight: true)));
        Registry<DimensionTypeDefinition> first = harness.State.Registries!.DimensionTypes;
        Assert.Equal(48, first[0].Value.MinY);

        await harness.ApplyAsync(DimensionRegistry(
            Dimension(CustomType.ToString(), 64, 64, hasSkylight: false)));
        Registry<DimensionTypeDefinition> replacement = harness.State.Registries!.DimensionTypes;

        Assert.NotSame(first, replacement);
        Assert.Equal(64, replacement[0].Value.MinY);
        Assert.Equal(64, replacement[0].Value.Height);
        Assert.False(replacement[0].Value.HasSkylight);
    }

    [Fact]
    public async Task InvalidRequiredReplacement_FailsWithoutRetainingItAsAUsableUpdate()
    {
        var harness = new ApplierHarness(Version, new ClientFeatures { Terrain = true });
        harness.State.Registries = JavaGameData.Registries(Version.Version.Protocol);
        await harness.ApplyAsync(DimensionRegistry(
            Dimension(CustomType.ToString(), 48, 128, hasSkylight: true)));
        RegistryAccess before = harness.State.Registries!;

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await harness.ApplyAsync(DimensionRegistry(
                Dimension(CustomType.ToString(), 3, 7, hasSkylight: false))));

        Assert.Contains("could not be decoded atomically", error.Message, StringComparison.Ordinal);
        Assert.Same(before, harness.State.Registries);
        Assert.Equal(48, harness.State.Registries!.DimensionTypes[0].Value.MinY);
    }

    private static UmpkClient Client(FakeJavaServer server) => new UmpkClientBuilder()
        .UseVersion(Version)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
        .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
        .UseTickSource(new ManualTicks())
        .ConfigureFeatures(features =>
        {
            features.Terrain = true;
            features.Inventory = true;
            features.Physics = false;
            features.Pathfinding = false;
        })
        .Build();

    private static async Task DriveToConfigurationAsync(FakeJavaServer server, CancellationToken ct)
    {
        _ = await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        _ = await server.NextFrameAsync(ct); // hello
        await SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null),
            PacketCodecContext.Registryless, ct);
        _ = await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        _ = await server.NextFrameAsync(ct); // client_information
    }

    private static ClientboundConfigRegistryDataPacket DimensionRegistry(params PackedRegistryEntry[] entries) =>
        new(RegistryIds.DimensionType, entries);

    private static PackedRegistryEntry Dimension(string id, int minY, int height, bool hasSkylight)
    {
        var data = new NbtCompound();
        data.PutInt("min_y", minY);
        data.PutInt("height", height);
        data.PutBool("has_skylight", hasSkylight);
        return new PackedRegistryEntry(Identifier.Parse(id), data);
    }

    private static ClientboundConfigRegistryDataPacket EnchantmentRegistry() => new(
        RegistryIds.Enchantment,
        [
            new PackedRegistryEntry(Identifier.Minecraft("aqua_affinity"), null),
            new PackedRegistryEntry(DepthStrider, new NbtCompound { ["max_level"] = new NbtInt(3) }),
        ]);

    private static Registry<EnchantmentDefinition> ServerEnchantments() =>
        new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment, 2)
            .Add(0, Identifier.Minecraft("aqua_affinity"), new EnchantmentDefinition(1))
            .Add(1, DepthStrider, new EnchantmentDefinition(3))
            .Build();

    private static ClientboundContainerSetSlotPacket BootsSlot(
        RegistryEntry<EnchantmentDefinition> enchantment, int level)
    {
        Assert.True(JavaGameData.Registries(Version.Version.Protocol).Items.TryGet(
            Identifier.Minecraft("diamond_boots"), out RegistryEntry<ItemDefinition> boots));
        return new ClientboundContainerSetSlotPacket(
            0,
            0,
            BootsMenuSlot,
            new ItemStack(boots, 1, DataComponentMap.Empty.With(
                DataComponents.Enchantments,
                new EnchantmentsComponent([new EnchantmentInstance(enchantment, level)]))));
    }

    private static ClientboundLoginPacket Join(string dimension, int dimensionTypeId)
    {
        return new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: [dimension], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: Spawn(dimension, dimensionTypeId), OnlineMode: false,
            EnforcesSecureChat: false, Legacy: null);
    }

    private static CommonPlayerSpawnInfo Spawn(string dimension, int dimensionTypeId) => new(
        DimensionTypeId: dimensionTypeId, Dimension: dimension, Seed: 0, GameType: 0, PreviousGameType: -1,
        IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);

    private static object DecodeServerbound(InboundFrame frame, ProtocolPhase phase)
    {
        Assert.True(Version.Protocol.TryGetRegistry(phase, PacketFlow.Serverbound, out PhaseRegistry registry));
        Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
        return codec.Decode(frame.Payload, PacketCodecContext.Registryless);
    }

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server,
        ProtocolPhase phase,
        TPacket packet,
        PacketCodecContext context,
        CancellationToken ct)
        where TPacket : class, IPacket
    {
        Assert.True(Version.Protocol.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != packet.Type.Id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented, $"{packet.Type.Id} is a marker in {phase}.");
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, context);
            await server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
            return;
        }

        throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) =>
            ValueTask.FromResult(pipe);
    }

    private sealed class ManualTicks : ITickSource
    {
        private readonly Channel<long> _ticks = Channel.CreateUnbounded<long>();

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public IAsyncEnumerable<long> Ticks(CancellationToken cancellationToken = default) =>
            _ticks.Reader.ReadAllAsync(cancellationToken);
    }
}
