using Umpk;
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.IntegrationTests;

/// <summary>Against real vanilla servers: a painting with <c>minecraft:painting/variant</c> must survive both the live slot update and the reconnect-time full inventory packet.</summary>
[Collection(LiveServerCollection.Name)]
public sealed class PaintingVariantLiveTests
{
    private const string Username = "UmpkPaintingTest";

    private static readonly Guid ProfileId = Guid.Parse("b2f324ac-106f-4079-b729-a4d7085545c1");

    private readonly ITestOutputHelper _out;

    public PaintingVariantLiveTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<string, int> Versions => new()
    {
        { "1.21.5", 770 },
        { "1.21.6", 771 },
        { "1.21.8", 772 },
        { "1.21.10", 773 },
        { "1.21.11", 774 },
        { "26.1", 775 },
        { "26.2", 776 },
        { "26.3", 777 },
    };

    [NightlyTheory]
    [MemberData(nameof(Versions))]
    public async Task PaintingVariant_SurvivesSlotUpdateAndReconnectInventory(string versionName, int protocol)
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
        UmpkClient seededClient = await ConnectAsync(version!, protocol, logs, ct.Token);
        await using (seededClient)
        {
            await WaitForSpawnAsync(seededClient, versionName, ct.Token);
            await server.SendConsoleAsync($"gamemode creative {Username}", ct.Token);

            await server.SendConsoleAsync($"give {Username} minecraft:stone 1", ct.Token);
            Assert.True(
                await WaitUntilAsync(() => AnySlotFilled(seededClient), TimeSpan.FromSeconds(15), ct.Token),
                $"{versionName}: the control item never reached the inventory.");

            await server.SendConsoleAsync($"clear {Username}", ct.Token);
            Assert.True(
                await WaitUntilAsync(() => !AnySlotFilled(seededClient), TimeSpan.FromSeconds(15), ct.Token),
                $"{versionName}: the control item was not cleared.");

            await server.SendConsoleAsync(
                $"give {Username} minecraft:painting[minecraft:painting/variant=alban] 1", ct.Token);
            Assert.True(
                await WaitUntilAsync(
                    () => FindPaintingVariant(seededClient) is not null,
                    TimeSpan.FromSeconds(20),
                    ct.Token),
                $"{versionName}: the painting variant did not decode from the live slot update.");
            Assert.Equal(ClientStatus.Playing, seededClient.Status);
            await seededClient.DisconnectAsync(CancellationToken.None);
        }

        UmpkClient reconnectedClient = await ConnectAsync(version!, protocol, logs, ct.Token);
        await using (reconnectedClient)
        {
            await WaitForSpawnAsync(reconnectedClient, versionName, ct.Token);
            PaintingVariantComponent? variant = null;
            Assert.True(
                await WaitUntilAsync(
                    () => (variant = FindPaintingVariant(reconnectedClient)) is not null,
                    TimeSpan.FromSeconds(20),
                    ct.Token),
                $"{versionName}: the painting variant did not decode from reconnect inventory content.");
            Assert.Equal(ClientStatus.Playing, reconnectedClient.Status);
            Assert.NotNull(variant!.HolderId);
            Assert.Null(variant.Direct);
            _out.WriteLine(
                $"[{versionName}/{protocol}] painting holder={variant.HolderId}, status={reconnectedClient.Status}");

            await Task.Delay(TimeSpan.FromSeconds(2), ct.Token);
            Assert.Equal(ClientStatus.Playing, reconnectedClient.Status);
        }
    }

    private static async Task WaitForSpawnAsync(
        UmpkClient client, string versionName, CancellationToken ct)
    {
        Assert.Equal(ClientStatus.Playing, client.Status);
        Assert.True(
            await WaitUntilAsync(() => client.State.Self.HasSpawned, TimeSpan.FromSeconds(30), ct),
            $"{versionName}: never reached spawn.");
    }

    private static PaintingVariantComponent? FindPaintingVariant(UmpkClient client)
    {
        if (!client.State.Features.Inventory)
            return null;

        foreach (ItemStack slot in client.State.Inventory.PlayerSlots)
            if (!slot.IsEmpty &&
                slot.Components.TryGet(
                    DataComponents.PaintingVariant,
                    out PaintingVariantComponent? variant))
                return variant;

        return null;
    }

    private static bool AnySlotFilled(UmpkClient client)
    {
        if (!client.State.Features.Inventory)
            return false;

        foreach (ItemStack slot in client.State.Inventory.PlayerSlots)
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
                .UseProfile(new GameProfile(ProfileId, Username))
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
