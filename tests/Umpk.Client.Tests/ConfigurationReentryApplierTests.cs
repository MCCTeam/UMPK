using System.Collections.Immutable;
using Umpk.Client.Commands;
using Umpk.Client.Tests.Support;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.Scoreboard;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The play-to-configuration re-entry (<c>minecraft:start_configuration</c>, 1.20.2+). The codec for this packet requires an acknowledgement and a handoff to the configuration driver.
/// <para>These tests assert the acknowledgement on the wire and the phase transition. Observing state alone cannot prove that the server received its required reply.</para>
/// </summary>
public sealed class ConfigurationReentryApplierTests
{
    /// <summary>Every protocol carrying the re-entry pair: 1.20.2 through 26.2.</summary>
    public static TheoryData<int> ReentryProtocols => new()
    {
        764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776,
    };

    /// <summary>Decodes an empty <c>start_configuration</c> body through the bound descriptor for the protocol, so what reaches the applier chain is what the wire produces rather than a hand-built record.</summary>
    private static object DecodeStartConfiguration(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version), $"unknown protocol {protocol}");
        Assert.True(
            version!.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));

        Identifier id = PlayPackets.Clientbound.StartConfiguration.Id;
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented, $"{id} is a marker at protocol {protocol}");
            return codec.Decode(ReadOnlySpan<byte>.Empty, PacketCodecContext.Registryless);
        }

        Assert.Fail($"{id} is not registered inbound at protocol {protocol}");
        return null!;
    }

    private static JavaVersion VersionOf(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    // A start_configuration frame must produce a configuration_acknowledged frame before the server can switch its inbound protocol.
    [Theory]
    [MemberData(nameof(ReentryProtocols))]
    public async Task StartConfiguration_SendsTheAcknowledgement(int protocol)
    {
        var harness = new ApplierHarness(VersionOf(protocol));

        bool owned = await harness.TryApplyAsync(DecodeStartConfiguration(protocol));

        Assert.True(owned, "start_configuration must be owned by an applier, not fall through the chain.");
        object sent = Assert.Single(harness.Recorder.Packets);
        Assert.IsType<ServerboundConfigurationAcknowledgedPacket>(sent);
    }

    // The acknowledgement alone is insufficient. The client must also leave play and run the configuration phase, or it acknowledges and then decodes configuration frames with the play descriptor. The live client hands this seam the connection hop plus the shared driver.
    [Theory]
    [MemberData(nameof(ReentryProtocols))]
    public async Task StartConfiguration_EntersTheConfigurationPhase(int protocol)
    {
        var harness = new ApplierHarness(VersionOf(protocol));

        await harness.ApplyAsync(DecodeStartConfiguration(protocol));

        Assert.Equal(1, harness.ConfigurationEntries);
    }

    // Ordering, from vanilla: the acknowledgement is a PLAY packet and goes out while the connection is still in play; only then does the client switch protocols. Encoding it after the hop would look for a play identifier in the configuration table and find nothing.
    [Fact]
    public async Task Acknowledgement_IsSentBeforeTheConfigurationHop()
    {
        var harness = new ApplierHarness(VersionOf(774));
        int sentWhenEntered = -1;
        harness.EnterConfiguration = _ =>
        {
            sentWhenEntered = harness.Recorder.Packets.Count;
            return ValueTask.CompletedTask;
        };

        await harness.ApplyAsync(DecodeStartConfiguration(774));

        Assert.Equal(1, sentWhenEntered);
    }

    // Vanilla's configuration-phase transition calls clear client level behavior, which nulls the level and the player, and installs a brand new listener, which drops the player info map, the command dispatcher, the recipe container and the advancements. Everything below is one of those two, so a client that came back from configuration holding any of it would be reporting entities, players, objectives and maps that vanilla does not.
    [Fact]
    public async Task StartConfiguration_TearsDownThePlayPhaseState()
    {
        var harness = new ApplierHarness(VersionOf(774));
        harness.State.Registries = JavaGameData.Registries(774);

        // Join for real, so the world and the spawned flag are set the way a live session sets them.
        await JoinAsync(harness);
        Assert.True(harness.State.HasWorld);
        Assert.True(harness.State.Self.HasSpawned);

        Assert.True(harness.State.Registries!.EntityTypes.TryGet(0, out RegistryEntry<EntityTypeDefinition> type));
        harness.State.Entities.Add(new Entity(7, Guid.NewGuid(), type));
        harness.State.TabList.Upsert(new TabListEntry(new GameProfile(Guid.NewGuid(), "someone")));
        harness.State.TabList.Header = Component.Text("header");
        harness.State.BossBars.Add(new BossBar(
            Guid.NewGuid(), Component.Text("bar"), 1f, BossBarColor.Red, BossBarOverlay.Progress, BossBarFlags.None));
        harness.State.Scoreboard.PutObjective(new Objective("obj", Component.Text("obj"), ObjectiveRenderType.Integer));
        harness.State.Maps.GetOrCreate(3);
        harness.State.Advancements.PutAdvancement(new Advancement(
            Identifier.Minecraft("story/root"), null, null, ["criterion"], []));
        harness.State.ServerCommands.Update(ServerCommandTree.Build(
            new CommandTreeData([new CommandNodeData(CommandNodeKind.Root, 0x00, ImmutableArray<int>.Empty, -1, null, null)], 0),
            ArgumentTypeRegistry.V1_21_5));

        await harness.ApplyAsync(DecodeStartConfiguration(774));

        Assert.Empty(harness.State.Entities.All);
        Assert.Empty(harness.State.TabList.Entries);
        Assert.Null(harness.State.TabList.Header);
        Assert.Empty(harness.State.BossBars.Bars);
        Assert.Empty(harness.State.Scoreboard.Objectives);
        Assert.Empty(harness.State.Maps.Maps);
        Assert.Empty(harness.State.Advancements.Advancements);
        Assert.Null(harness.State.ServerCommands.Tree);
        Assert.False(harness.State.Self.HasSpawned);
        Assert.False(harness.State.HasWorld);
    }

    private static async Task JoinAsync(ApplierHarness harness)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null));
    }

    // The other half of the same table, and the one that would fail silently and late. Re-entry runs
    // return to world behavior, which queues only PrepareSpawnTask (sends
    // nothing) and JoinWorldTask (sends finish_configuration and nothing else). The registry synchronisation and the brand belong to the login-only startConfiguration(), so nothing resends them. Dropping them here would leave the client with no dimension types for the rest of the session and no way to notice.
    [Fact]
    public async Task StartConfiguration_KeepsWhatTheServerWillNotResend()
    {
        var harness = new ApplierHarness(VersionOf(774));
        RegistryAccess registries = JavaGameData.Registries(774);
        harness.State.Registries = registries;
        harness.State.Server.Brand = "vanilla";
        harness.State.Self.Username = "tester";
        harness.Cookies.Set(Identifier.Minecraft("proxy_token"), [1, 2, 3]);

        await harness.ApplyAsync(DecodeStartConfiguration(774));

        Assert.Same(registries, harness.State.Registries);
        Assert.Equal("vanilla", harness.State.Server.Brand);
        Assert.Equal("tester", harness.State.Self.Username);
        Assert.Equal([1, 2, 3], harness.Cookies.Get(Identifier.Minecraft("proxy_token")));
    }
}
