using Umpk;
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.IntegrationTests;

/// <summary>Against a real server: a filled map in the bot's inventory must not disconnect it.</summary>
/// <remarks>An ordinary item first proves that inventory updates are working. A filled map then verifies that <c>minecraft:map_id</c> decodes without ending the session.</remarks>
[Collection(LiveServerCollection.Name)]
public sealed class FilledMapLiveTests
{
    private const string Username = "UmpkMapTest";

    private readonly ITestOutputHelper _out;

    public FilledMapLiveTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<string, int> Versions => new()
    {
        { "1.20.6", 766 },
        { "1.21.5", 770 },
        { "26.2", 776 },
    };

    [NightlyTheory]
    [MemberData(nameof(Versions))]
    public async Task FilledMapInInventory_DoesNotDisconnect(string versionName, int protocol)
    {
        string serverDir = IntegrationConfig.ServerDir(versionName);
        Assert.True(
            Directory.Exists(serverDir),
            $"Server directory for {versionName} not provisioned ({serverDir}). Set UMPK_SERVER_ROOT.");
        Assert.True(
            JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version) && version is not null,
            $"No JavaVersion for protocol {protocol}.");

        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        await using LocalServer server = await LocalServer.StartAsync(
            serverDir, JavaRuntimes.ForProtocol(protocol), TimeSpan.FromSeconds(180), ct.Token);

        var logs = new CapturingLoggerFactory();
        await using UmpkClient client = await ConnectAsync(version!, protocol, logs, ct.Token);

        Assert.Equal(ClientStatus.Playing, client.Status);
        Assert.True(
            await WaitUntilAsync(() => client.State.Self.HasSpawned, TimeSpan.FromSeconds(30), ct.Token),
            $"{versionName}: never reached spawn.");

        await server.SendConsoleAsync($"gamemode creative {Username}", ct.Token);

        // An ordinary item confirms the give-and-observe path before the map-specific assertion.
        await server.SendConsoleAsync($"give {Username} minecraft:stone 7", ct.Token);
        Assert.True(
            await WaitUntilAsync(() => AnySlotFilled(client), TimeSpan.FromSeconds(15), ct.Token),
            $"{versionName}: the control item never reached the inventory.");
        Assert.Equal(ClientStatus.Playing, client.Status);

        // The filled map exercises the component-bearing form introduced in 1.20.5.
        await server.SendConsoleAsync(
            $"give {Username} minecraft:filled_map[minecraft:map_id=1234] 1", ct.Token);

        bool sawMap = await WaitUntilAsync(
            () => FindMapId(client) is not null, TimeSpan.FromSeconds(20), ct.Token);

        // The session verdict comes first: it must stay connected. Decoding the id additionally proves the component was actually modeled rather than merely tolerated.
        Assert.Equal(ClientStatus.Playing, client.Status);
        Assert.True(sawMap, $"{versionName}: the filled map never decoded into a slot.");
        _out.WriteLine($"[{versionName}/{protocol}] map_id={FindMapId(client)}, status={client.Status}");

        // Hold the session open: a fault that tears the connection down does so within a tick or two of the offending packet, so surviving a window after it is the real "did not disconnect" proof.
        await Task.Delay(TimeSpan.FromSeconds(5), ct.Token);
        Assert.Equal(ClientStatus.Playing, client.Status);
    }

    private static int? FindMapId(UmpkClient client)
    {
        if (!client.State.Features.Inventory)
            return null;

        foreach (var slot in client.State.Inventory.PlayerSlots)
            if (!slot.IsEmpty && slot.Components.TryGet(DataComponents.MapId, out MapIdComponent? mapId))
                return mapId.Id;

        return null;
    }

    private static bool AnySlotFilled(UmpkClient client)
    {
        if (!client.State.Features.Inventory)
            return false;

        foreach (var slot in client.State.Inventory.PlayerSlots)
            if (!slot.IsEmpty)
                return true;

        return false;
    }

    private static async Task<UmpkClient> ConnectAsync(
        JavaVersion version, int protocol, CapturingLoggerFactory logs, CancellationToken ct)
    {
        var endpoint = new ServerEndpoint("127.0.0.1", (ushort)LocalServer.Port);
        Exception? last = null;
        for (int attempt = 0; attempt < 25; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            UmpkClient client = new UmpkClientBuilder()
                .UseVersion(version)
                .UseProfile(new GameProfile(Guid.NewGuid(), Username))
                .UseStaticRegistries(JavaGameData.Registries(protocol))
                .UseLoggerFactory(logs)
                .ConfigureFeatures(f => { f.Physics = false; f.Pathfinding = false; })
                .ConfigureOptions(o => o.AutoSendPosition = false)
                .Build();

            try
            {
                await client.ConnectAsync(endpoint, ct).ConfigureAwait(false);
                return client;
            }
            catch (Exception ex)
            {
                last = ex;
                await client.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(1500, ct).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Could not connect after retries.", last);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return condition();
    }
}
