using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Applier-level cover for the connection-flow obligations: the kick reason reaching <see cref="DisconnectInfo.Message"/>, the pre-1.17 window-transaction echo, the 1.21.4+ level-loaded announcement, and the 1.20.5+ cookie/transfer handling.</summary>
/// <remarks>Every inbound packet is round-tripped through the codec the catalog binds for the era under test (see <see cref="BoundDescriptorCodec"/>), not hand-constructed, so a packet that is still a marker on that era fails here the way a live session would silently drop the frame.</remarks>
public sealed class ConnectionObligationApplierTests
{
    private static JavaVersion Version(int protocol)
    {
        Assert.True(Umpk.Data.Java.JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    // the kick reason

    /// <summary>One protocol per component era of the disconnect payload, plus the 1.8 floor.</summary>
    public static TheoryData<int> DisconnectProtocols => [47, 340, 763, 765, 769, 770, 776];

    [Theory]
    [MemberData(nameof(DisconnectProtocols))]
    public async Task A_Server_Kick_Reaches_DisconnectInfo_Message(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));
        object kick = BoundDescriptorCodec.RoundTrip(
            protocol, "disconnect", new ClientboundDisconnectPacket(Component.Text("You are banned")));

        await harness.ApplyAsync(kick);

        Assert.NotNull(harness.Disconnect);
        Assert.Equal(CloseReason.DisconnectMessage, harness.Disconnect!.Reason);
        Assert.Equal("You are banned", Assert.IsType<TextContent>(harness.Disconnect.Message!.Content).Text);
        Assert.False(harness.Disconnect.WasLocal);
    }

    /// <summary>The server writes the disconnect frame and then closes the socket, so the receive loop's transport-close record arrives immediately behind it. The session keeps the first reason recorded, which is why the applier has to record rather than the loop: were it the other way round the message would be overwritten by a bare socket-EOF every single time.</summary>
    [Fact]
    public async Task The_Kick_Reason_Survives_The_Socket_Close_That_Follows_It()
    {
        var harness = new ApplierHarness(Version(776));
        object kick = BoundDescriptorCodec.RoundTrip(
            776, "disconnect", new ClientboundDisconnectPacket(Component.Text("Server restarting")));

        await harness.ApplyAsync(kick);

        // What UmpkClient's receive loop does a moment later when the socket goes away.
        harness.RecordTransportClose(new DisconnectInfo { Reason = CloseReason.SocketEof });

        Assert.Equal(CloseReason.DisconnectMessage, harness.Disconnect!.Reason);
        Assert.Equal("Server restarting", Assert.IsType<TextContent>(harness.Disconnect.Message!.Content).Text);
    }

    /// <summary>The configuration-phase kick lands in the same place, on the same close reason.</summary>
    [Theory]
    [InlineData(764)]
    [InlineData(769)]
    [InlineData(776)]
    public async Task A_Configuration_Phase_Kick_Also_Reaches_DisconnectInfo_Message(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));

        await harness.ApplyAsync(new ClientboundConfigDisconnectPacket(Component.Text("Whitelist")));

        Assert.Equal(CloseReason.DisconnectMessage, harness.Disconnect!.Reason);
        Assert.Equal("Whitelist", Assert.IsType<TextContent>(harness.Disconnect.Message!.Content).Text);
    }

    // container_ack

    /// <summary>The band that spells the window transaction <c>container_ack</c> in the dataset, plus the 1.8 floor that spells it <c>transaction</c>. The alias resolves both names onto one timeline, so the descriptor reports the packet identity as <c>minecraft:transaction</c> on every protocol; the The 107-754 band must resolve a concrete codec rather than a marker.</summary>
    public static TheoryData<int> TransactionProtocols => [47, 107, 340, 404, 498, 578, 754];

    [Theory]
    [MemberData(nameof(TransactionProtocols))]
    public async Task A_Rejected_Transaction_Is_Echoed_Back(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));
        object rejected = BoundDescriptorCodec.RoundTrip(
            protocol,
            "transaction",
            new ClientboundTransactionPacket(ContainerId: 2, ActionNumber: 17, Accepted: false));

        await harness.ApplyAsync(rejected);

        ServerboundTransactionPacket echo = Assert.Single(
            harness.Recorder.Packets.OfType<ServerboundTransactionPacket>());
        Assert.Equal(2, echo.ContainerId);
        Assert.Equal((short)17, echo.ActionNumber);
        Assert.True(echo.Accepted);
    }

    [Fact]
    public async Task An_Accepted_Transaction_Is_Not_Echoed()
    {
        // Vanilla's client only echoes the rejection; the server records an expected uid only on that path, so an unsolicited echo would be dropped.
        var harness = new ApplierHarness(Version(340));

        await harness.ApplyAsync(new ClientboundTransactionPacket(2, 17, Accepted: true));

        Assert.Empty(harness.Recorder.Packets.OfType<ServerboundTransactionPacket>());
    }

    [Fact]
    public async Task The_Transaction_Echo_Does_Not_Require_The_Inventory_Feature()
    {
        // The un-synched menu blocks the CLICK handler, not an inventory view, so the echo has to work with the Inventory feature off. This is the chunk-batch reasoning applied to containers.
        var harness = new ApplierHarness(Version(340), new ClientFeatures { Inventory = false });

        await harness.ApplyAsync(new ClientboundTransactionPacket(2, 17, Accepted: false));

        Assert.Single(harness.Recorder.Packets.OfType<ServerboundTransactionPacket>());
    }

    // player_loaded

    [Theory]
    [InlineData(769)]
    [InlineData(776)]
    public async Task Joining_DoesNotAnnounceBeforePlacementAndTerrainReadiness(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));

        await JoinAsync(harness);

        Assert.Empty(harness.Recorder.Packets.OfType<ServerboundPlayerLoadedPacket>());
    }

    [Theory]
    [InlineData(769)]
    [InlineData(776)]
    public async Task Respawning_DoesNotAnnounceBeforeReplacementTerrainReadiness(int protocol)
    {
        // Vanilla resets the player to un-loaded on every respawn and dimension change, not only on join, so a client that announces once ends up frozen for three seconds after each death.
        var harness = new ApplierHarness(Version(protocol));

        await JoinAsync(harness);
        await harness.ApplyAsync(new ClientboundRespawnPacket(Spawn(), DataToKeep: 0, Legacy: null));

        Assert.Empty(harness.Recorder.Packets.OfType<ServerboundPlayerLoadedPacket>());
    }

    [Theory]
    [InlineData(47)]
    [InlineData(763)]
    [InlineData(768)]
    public async Task Versions_Without_PlayerLoaded_Send_Nothing(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));

        await JoinAsync(harness);

        Assert.Empty(harness.Recorder.Packets.OfType<ServerboundPlayerLoadedPacket>());
    }

    // cookies and transfer

    [Theory]
    [InlineData(766)]
    [InlineData(776)]
    public async Task A_Play_Phase_Cookie_Request_Is_Answered_From_The_Store(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));
        var key = Identifier.Minecraft("velocity_forwarding");
        harness.Cookies.Set(key, [1, 2, 3]);

        object request = BoundDescriptorCodec.RoundTrip(
            protocol, "cookie_request", new ClientboundCookieRequestPacket(key));
        await harness.ApplyAsync(request);

        ServerboundCookieResponsePacket response = Assert.Single(
            harness.Recorder.Packets.OfType<ServerboundCookieResponsePacket>());
        Assert.Equal(key, response.Key);
        Assert.Equal<byte[]>([1, 2, 3], response.Payload!);
    }

    [Fact]
    public async Task An_Unknown_Cookie_Is_Still_Answered_With_An_Absent_Payload()
    {
        // Silence is the failure mode that stalls a proxy handshake; vanilla's client always replies.
        var harness = new ApplierHarness(Version(776));

        await harness.ApplyAsync(new ClientboundCookieRequestPacket(Identifier.Minecraft("never_stored")));

        ServerboundCookieResponsePacket response = Assert.Single(
            harness.Recorder.Packets.OfType<ServerboundCookieResponsePacket>());
        Assert.Null(response.Payload);
    }

    [Fact]
    public async Task A_Configuration_Phase_Cookie_Request_Is_Answered_With_The_Configuration_Record()
    {
        var harness = new ApplierHarness(Version(776));
        var key = Identifier.Minecraft("proxy_session");
        harness.Cookies.Set(key, [9]);

        await harness.ApplyAsync(new ClientboundConfigCookieRequestPacket(key));

        ServerboundConfigCookieResponsePacket response = Assert.Single(
            harness.Recorder.Packets.OfType<ServerboundConfigCookieResponsePacket>());
        Assert.Equal<byte[]>([9], response.Payload!);
    }

    [Theory]
    [InlineData(766)]
    [InlineData(776)]
    public async Task A_Stored_Cookie_Is_Kept_And_Answers_A_Later_Request(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));
        var key = Identifier.Minecraft("session_token");

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            protocol, "store_cookie", new ClientboundStoreCookiePacket(key, [7, 7])));
        await harness.ApplyAsync(new ClientboundCookieRequestPacket(key));

        Assert.Equal<byte[]>(
            [7, 7],
            harness.Recorder.Packets.OfType<ServerboundCookieResponsePacket>().Single().Payload!);
    }

    [Theory]
    [InlineData(766)]
    [InlineData(776)]
    public async Task A_Transfer_Is_Surfaced_As_An_Event(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));
        ServerTransferRequested? seen = null;
        using IDisposable _ = harness.Events.Subscribe<ServerTransferRequested>(e => seen = e);

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            protocol, "transfer", new ClientboundTransferPacket("lobby.example.invalid", 25566)));

        Assert.NotNull(seen);
        Assert.Equal("lobby.example.invalid", seen!.Host);
        Assert.Equal(25566, seen.Port);
    }

    [Fact]
    public async Task A_Configuration_Phase_Transfer_Raises_The_Same_Event()
    {
        var harness = new ApplierHarness(Version(776));
        ServerTransferRequested? seen = null;
        using IDisposable _ = harness.Events.Subscribe<ServerTransferRequested>(e => seen = e);

        await harness.ApplyAsync(new ClientboundConfigTransferPacket("lobby.example.invalid", 25566));

        Assert.Equal(25566, seen!.Port);
    }

    // helpers

    private static CommonPlayerSpawnInfo Spawn() => new(
        DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
        IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);

    private static async Task JoinAsync(ApplierHarness harness) =>
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: Spawn(), OnlineMode: false, EnforcesSecureChat: false,
            Legacy: null));
}
