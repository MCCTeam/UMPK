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

/// <summary>The configuration phase must install the server's <c>minecraft:enchantment</c> table.</summary>
/// <remarks>
/// <para>From protocol 767 the server synchronizes the enchantment registry as datapack data. Earlier protocols use a built-in registry. The dataset mirrors that boundary.</para>
/// <para>So on 767+ nothing anywhere could name an enchantment: a component-era stack carries only a numeric holder id, <c>ItemCodecPrimitives.ResolveEnchantment</c> resolves it against <c>PacketCodecContext.Registries.Enchantments</c>, and that was empty on every 767+ session because the config-phase sync installed dimension types and chat types only. Every enchantment decoded to a default, unbound handle that cannot report even its numeric id.</para>
/// </remarks>
public sealed class EnchantmentRegistryInstallTests
{
    /// <summary>1.21, the first protocol whose enchantment registry is server-sent.</summary>
    private const int SyncedProtocol = 767;

    /// <summary>1.20.6, the last protocol with a built-in enchantment registry.</summary>
    private const int BuiltInProtocol = 766;

    private const int SelfEntityId = 1;

    /// <summary>The 46-slot player-window index of the boots slot (<c>PlayerInventorySlotMap</c>).</summary>
    private const int BootsMenuSlot = 8;

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static readonly Identifier DepthStrider = Identifier.Minecraft("depth_strider");
    private static readonly Identifier Sharpness = Identifier.Minecraft("sharpness");

    // The install

    /// <summary>Packed-entry order defines network ids. The expected ids are independent literals, so mapping every entry to one id cannot pass.</summary>
    [Fact]
    public async Task ConfigurationRegistryData_InstallsTheEnchantmentTable()
    {
        ApplierHarness harness = Build(SyncedProtocol);

        await harness.ApplyAsync(RegistryData(
            Entry("minecraft:aqua_affinity", maxLevel: 1),
            Entry("minecraft:depth_strider", maxLevel: 3),
            Entry("minecraft:sharpness", maxLevel: 5)));

        Registry<EnchantmentDefinition> enchantments = harness.State.Registries!.Enchantments;
        Assert.Equal(3, enchantments.Count);
        Assert.True(enchantments.TryGetNetworkId(Identifier.Minecraft("aqua_affinity"), out int aqua));
        Assert.Equal(0, aqua);
        Assert.True(enchantments.TryGetNetworkId(DepthStrider, out int strider));
        Assert.Equal(1, strider);
        Assert.True(enchantments.TryGetNetworkId(Sharpness, out int sharp));
        Assert.Equal(2, sharp);
    }

    /// <summary>A 1.20.5+ server that agreed a known-pack set answers with entries carrying NO element at all (<c>PackedRegistryEntry.Data</c> null), meaning "you already have this one". The identifier and its id are still the server's, and the name-to-id association is the whole of what an enchantment holder needs, so an elided entry must keep its slot rather than be dropped - dropping it would shift every id after it.</summary>
    [Fact]
    public async Task AnElidedEntry_KeepsItsSlotSoTheIdsAfterItDoNotShift()
    {
        ApplierHarness harness = Build(SyncedProtocol);

        await harness.ApplyAsync(RegistryData(
            Elided("minecraft:aqua_affinity"),
            Elided("minecraft:depth_strider"),
            Entry("minecraft:sharpness", maxLevel: 5)));

        Registry<EnchantmentDefinition> enchantments = harness.State.Registries!.Enchantments;
        Assert.Equal(3, enchantments.Count);
        Assert.True(enchantments.TryGetNetworkId(DepthStrider, out int strider));
        Assert.Equal(1, strider);
        Assert.True(enchantments.TryGetNetworkId(Sharpness, out int sharp));
        Assert.Equal(2, sharp);
    }

    /// <summary><c>max_level</c> is a top-level key because the enchantment definition fields are inlined into the element compound rather than nested. An elided element carries no level and keeps <c>EnchantmentDefinition</c>'s declared default rather than inventing one.</summary>
    [Fact]
    public async Task TheElementsMaxLevel_IsReadWhenPresentAndDefaultedWhenElided()
    {
        ApplierHarness harness = Build(SyncedProtocol);

        await harness.ApplyAsync(RegistryData(
            Entry("minecraft:depth_strider", maxLevel: 3),
            Elided("minecraft:sharpness")));

        Registry<EnchantmentDefinition> enchantments = harness.State.Registries!.Enchantments;
        Assert.True(enchantments.TryGetValue(DepthStrider, out EnchantmentDefinition? strider));
        Assert.Equal(3, strider!.MaxLevel);
        Assert.True(enchantments.TryGetValue(Sharpness, out EnchantmentDefinition? sharp));
        Assert.Equal(1, sharp!.MaxLevel);
    }

    /// <summary>The control: every OTHER registry in this packet is still deliberately dropped. Without this, a change that installed whatever arrived would pass every case above.</summary>
    [Fact]
    public async Task ARegistryWithNoConsumer_IsStillDropped()
    {
        ApplierHarness harness = Build(SyncedProtocol);

        await harness.ApplyAsync(new ClientboundConfigRegistryDataPacket(
            Identifier.Minecraft("banner_pattern"),
            [new PackedRegistryEntry(Identifier.Minecraft("base"), null)]));

        Assert.False(harness.State.Registries!.TryGetRegistry(
            Identifier.Minecraft("banner_pattern"), out IRegistry _));
    }

    /// <summary>The session's registry view is what now reaches the codec context at the configuration-to-play pause, so the claim that this changes nothing but enchantments has to be checked rather than asserted in prose. The codecs read three registries - <c>Items</c>, <c>Enchantments</c>, <c>Attributes</c> - and after a full configuration phase two of the three must still be the very same instances the static snapshot carried.</summary>
    [Fact]
    public async Task TheSessionRegistryView_DiffersFromTheStaticOneOnlyInWhatTheServerSent()
    {
        RegistryAccess statics = JavaGameData.Registries(SyncedProtocol);
        ApplierHarness harness = Build(SyncedProtocol);
        harness.State.Registries = statics;

        await harness.ApplyAsync(RegistryData(Entry("minecraft:depth_strider", maxLevel: 3)));

        RegistryAccess merged = harness.State.Registries!;
        Assert.NotSame(statics, merged);
        Assert.Same(statics.Items, merged.Items);
        Assert.Same(statics.Attributes, merged.Attributes);
        Assert.NotSame(statics.Enchantments, merged.Enchantments);
    }

    // The era split, pinned against the dataset

    /// <summary>The built-in table exists up to 766 and stops at 767, which is why the install above is needed on 767+ and is inert below it. Literal expectations, not derived from the value being checked.</summary>
    [Theory]
    [InlineData(765, true)]
    [InlineData(766, true)]
    [InlineData(767, false)]
    [InlineData(774, false)]
    public void TheBuiltInEnchantmentTable_StopsAt767(int protocol, bool populated)
    {
        Assert.Equal(populated, JavaGameData.Registries(protocol).Enchantments.Count > 0);
    }

    /// <summary>The legacy band needs no registry at all and is untouched by any of this: a 1.16.5 stack carries its enchantments as NBT in <c>DataComponents.LegacyNbt</c> and <see cref="ItemStack.TryGetEnchantmentLevel"/> reads them by identifier with no lookup. Pinned here so the install cannot quietly become a precondition for the band that never needed it.</summary>
    [Fact]
    public void TheLegacyNbtPath_NeedsNoEnchantmentRegistryAndIsUnchanged()
    {
        var nbt = new NbtCompound
        {
            ["Enchantments"] = new NbtList(NbtTagType.Compound)
            {
                new NbtCompound
                {
                    ["id"] = new NbtString("minecraft:depth_strider"),
                    ["lvl"] = new NbtShort(2),
                },
            },
        };

        Assert.True(JavaGameData.Registries(754).Items.TryGetValue(
            Identifier.Minecraft("diamond_boots"), out ItemDefinition? boots));
        Assert.True(JavaGameData.Registries(754).Items.TryGet(
            Identifier.Minecraft("diamond_boots"), out RegistryEntry<ItemDefinition> handle));
        _ = boots;

        var stack = new ItemStack(
            handle, 1, DataComponentMap.Empty.With(DataComponents.LegacyNbt, new LegacyNbtComponent(nbt)));

        Assert.Empty(stack.Enchantments);
        Assert.True(stack.TryGetEnchantmentLevel(
            DepthStrider, JavaGameData.LegacyItemBridgeEra, JavaGameData.LegacyItemBridgeSource, out int level));
        Assert.Equal(2, level);
    }

    // Packet in, readout out: the whole chain over a real connection

    /// <summary>The proof the install has a consumer. A real client joins a real (fake) server; the server sends its <c>minecraft:enchantment</c> registry during configuration, one entry of it with an ELIDED payload; then it sends the player's boots as a normal <c>container_set_slot</c> whose enchantment component names its enchantment only by the holder id that registry defined. The client has to come out the other side able to say the boots carry depth strider III.</summary>
    /// <remarks>The registry the server encodes with is the same one it announced, so the holder id on the wire is the server's and nothing else. Before the install the client's codec context had an EMPTY enchantment registry on this protocol, so the id resolved to a default handle: the level survived the decode and the identity did not.</remarks>
    [Fact]
    public async Task AServerSentEnchantmentTable_LetsA1_21WireLayoutItemReportItsLevelByIdentifier()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTicks();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = LiveClient(server, ticks);

        // The server's own table, announced with an elided first entry, then used to build the item.
        Registry<EnchantmentDefinition> serverTable = ServerEnchantmentTable();
        await JoinAsync(client, server, ct);

        Assert.True(serverTable.TryGet(DepthStrider, out RegistryEntry<EnchantmentDefinition> strider));
        await SendAsync(server, ProtocolPhase.Play, BootsSlot(strider, level: 3), ct);

        await WaitForAsync(
            async () => !(await ClientBootsAsync(client, ct)).IsEmpty, ct);

        ItemStack boots = await ClientBootsAsync(client, ct);
        Assert.Equal(Identifier.Minecraft("diamond_boots"), boots.Item.Id);
        EnchantmentInstance decoded = Assert.Single(boots.Enchantments);
        Assert.False(decoded.Enchantment.IsDefault);
        Assert.Equal(DepthStrider, decoded.Enchantment.Id);
        Assert.Equal(3, decoded.Level);
        Assert.True(boots.TryGetEnchantmentLevel(
            DepthStrider, JavaGameData.LegacyItemBridgeEra, JavaGameData.LegacyItemBridgeSource, out int level));
        Assert.Equal(3, level);
    }

    // Support

    private static ClientboundConfigRegistryDataPacket RegistryData(params PackedRegistryEntry[] entries) =>
        new(RegistryIds.Enchantment, entries);

    private static PackedRegistryEntry Entry(string id, int maxLevel) =>
        new(Identifier.Parse(id), new NbtCompound { ["max_level"] = new NbtInt(maxLevel) });

    private static PackedRegistryEntry Elided(string id) => new(Identifier.Parse(id), null);

    private static ApplierHarness Build(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Terrain = true });
        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.Self.EntityId = SelfEntityId;
        return harness;
    }

    /// <summary>A three-entry enchantment registry standing in for the server's datapack table, with ids that deliberately do NOT match 1.20.6's built-in numbering, so a client resolving against a built-in table would name the wrong enchantment.</summary>
    private static Registry<EnchantmentDefinition> ServerEnchantmentTable()
    {
        var builder = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment, 3);
        builder.Add(0, Identifier.Minecraft("aqua_affinity"), new EnchantmentDefinition(1));
        builder.Add(1, DepthStrider, new EnchantmentDefinition(3));
        builder.Add(2, Sharpness, new EnchantmentDefinition(5));
        return builder.Build();
    }

    private static ClientboundContainerSetSlotPacket BootsSlot(
        RegistryEntry<EnchantmentDefinition> enchantment, int level)
    {
        Assert.True(JavaGameData.Registries(LiveVersion.Version.Protocol).Items.TryGet(
            Identifier.Minecraft("diamond_boots"), out RegistryEntry<ItemDefinition> boots));
        var stack = new ItemStack(
            boots,
            1,
            DataComponentMap.Empty.With(
                DataComponents.Enchantments,
                new EnchantmentsComponent([new EnchantmentInstance(enchantment, level)])));
        return new ClientboundContainerSetSlotPacket(0, 0, BootsMenuSlot, stack);
    }

    private static Task<ItemStack> ClientBootsAsync(UmpkClient client, CancellationToken ct) =>
        client.InvokeAsync(c => c.State.Inventory.PlayerSlots[BootsMenuSlot], ct);

    private static JavaVersion LiveVersion => JavaVersions.V1_21_11;

    private static UmpkClient LiveClient(FakeJavaServer server, ITickSource ticks) =>
        new UmpkClientBuilder()
            .UseVersion(LiveVersion)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(LiveVersion.Version.Protocol))
            .UseTickSource(ticks)
            .ConfigureFeatures(f =>
            {
                f.Terrain = true;
                f.Inventory = true;
                f.Physics = false;
                f.Pathfinding = false;
            })
            .Build();

    private static async Task JoinAsync(UmpkClient client, FakeJavaServer server, CancellationToken ct)
    {
        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information

        // The registry the whole test turns on, announced exactly as a 1.21+ server announces it, with one entry whose payload the known-pack handshake elided.
        await SendAsync(server, ProtocolPhase.Configuration, RegistryData(
            Elided("minecraft:aqua_affinity"),
            Entry("minecraft:depth_strider", maxLevel: 3),
            Entry("minecraft:sharpness", maxLevel: 5)), ct);

        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, LiveVersion.Protocol, ct);
        await connect.WaitAsync(Budget, ct);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                    _ = await server.NextFrameAsync(ct).ConfigureAwait(false);

            }
            catch (Exception)
            {
                // The pipe closing at the end of the test is the expected way out.
            }
        }, ct);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await SendAsync(server, ProtocolPhase.Play, new ClientboundLoginPacket(
            PlayerId: SelfEntityId, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false,
            Legacy: null), ct);
        await joined.Task.WaitAsync(Budget, ct);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, CancellationToken ct)
    {
        while (!await condition().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
    {
        ProtocolDescriptor descriptor = LiveVersion.Protocol;
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != packet.Type.Id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented, $"{packet.Type.Id} is a marker in {phase}.");
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
            await server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
            return;
        }

        throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }

    /// <summary>A tick source the test advances by hand; nothing here needs a tick, but the client wants one.</summary>
    private sealed class ManualTicks : ITickSource
    {
        private readonly Channel<long> _ticks = Channel.CreateUnbounded<long>();

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public IAsyncEnumerable<long> Ticks(CancellationToken cancellationToken = default) =>
            _ticks.Reader.ReadAllAsync(cancellationToken);
    }
}
