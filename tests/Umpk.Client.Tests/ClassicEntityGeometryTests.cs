using System.IO.Compression;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ClassicEntityGeometryTests
{
    public static IEnumerable<object[]> Releases()
    {
        foreach (string fixture in new[] { "ClassicVillagerGeometry", "ClassicFarmGeometry" })
        {
            using JsonDocument document = ReadNative(fixture);
            foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            {
                string entity = release.TryGetProperty("entity", out JsonElement value) ? value.GetString()!.Split(':')[1] : "villager";
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("case").GetInt32(), entity, fixture];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public async Task SignedAgeBytesMatchEveryNativeChildPredicate(string release, int caseIndex, string entity, string fixture)
    {
        using JsonDocument document = ReadNative(fixture);
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        await using Session session = await Session.CreateAsync(release, entity);
        var point = new Vec3d(1.5, 64, 1.5);
        await session.LoadAroundAsync(point);
        await session.SpawnAsync(point);
        foreach (JsonElement row in native.GetProperty("predicates").EnumerateArray())
        {
            await session.AgeAsync(row.GetProperty("value").GetSByte());
            await session.TickAsync();
            await session.ReapplyAsync();
            bool baby = row.GetProperty("child").GetBoolean();
            JsonElement size = native.GetProperty("rows").EnumerateArray().First(value => (value.GetProperty("from").GetInt32() < 0) == baby).GetProperty("before_size");
            float width = size[0].GetSingle(), height = size[1].GetSingle();
            Assert.Equal(new Aabb(point.X - width / 2F, point.Y, point.Z - width / 2F, point.X + width / 2F, point.Y + height, point.Z + width / 2F), await session.BoundsAsync());
        }
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public async Task MetadataPositionAndSessionTickMatchEveryNativeSizePhase(string release, int caseIndex, string entity, string fixture)
    {
        using JsonDocument document = ReadNative(fixture);
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        await using Session session = await Session.CreateAsync(release, entity);
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            JsonElement value = native.GetProperty("positions")[row.GetProperty("position").GetInt32()];
            var point = new Vec3d(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
            await session.LoadAroundAsync(point);
            await session.SpawnAsync(point);
            await session.AgeAsync(row.GetProperty("from").GetSByte());
            await session.TickAsync();
            await session.ReapplyAsync();
            Assert.Equal(Box(row.GetProperty("before")), await session.BoundsAsync());
            await session.AgeAsync(row.GetProperty("to").GetSByte());
            Assert.Equal(Box(row.GetProperty("after_metadata")), await session.BoundsAsync());
            await session.ReapplyAsync();
            Assert.Equal(Box(row.GetProperty("before_phase_position")), await session.BoundsAsync());
            await session.TickAsync();
            Assert.Equal(Box(row.GetProperty("after_phase")), await session.BoundsAsync());
            await session.ReapplyAsync();
            Assert.Equal(Box(row.GetProperty("after_position")), await session.BoundsAsync());
        }
    }

    private static Aabb Box(JsonElement box) => new(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble());

    [Theory]
    [InlineData(1.5, 1.5)]
    [InlineData(-17.25, -31.75)]
    public async Task MissingCornerColumnDefersTheAgePhaseUntilItsInclusiveAreaLoads(double x, double z)
    {
        await using Session session = await Session.CreateAsync("1.8.9");
        var point = new Vec3d(x, 64, z);
        await session.LoadAroundAsync(point);
        await session.SpawnAsync(point);
        Aabb adult = await session.BoundsAsync();
        await session.AgeAsync(-1);
        await session.UnloadCornerAsync(point);
        await session.TickAsync();
        Assert.Equal(adult, await session.BoundsAsync());
        await session.LoadAroundAsync(point);
        await session.TickAsync();
        Assert.Equal(new Aabb(adult.MinX, adult.MinY, adult.MinZ, adult.MinX + .3F, adult.MinY + .9F, adult.MinZ + .3F), await session.BoundsAsync());
    }

    private static JsonDocument ReadNative(string fixture)
    {
        using Stream stream = typeof(ClassicEntityGeometryTests).Assembly.GetManifestResourceStream($"Umpk.Client.Tests.Fixtures.{fixture}.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(60));
        private readonly FakeJavaServer _server = FakeJavaServer.Create();
        private readonly ObservedTickSource _ticks = new();
        private readonly JavaVersion _version;
        private readonly string _entityName;
        private long _marker;

        private Session(JavaVersion version, string entityName)
        {
            _version = version;
            _entityName = entityName;
            Client = new UmpkClientBuilder()
                .UseVersion(version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "ClassicGeometry"))
                .UseConnectionFactory(new PipeConnectionFactory(_server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(47))
                .UseTickSource(_ticks)
                .ConfigureFeatures(features =>
                {
                    features.Physics = false;
                    features.Pathfinding = false;
                })
                .Build();
        }

        private UmpkClient Client { get; }

        internal static async Task<Session> CreateAsync(string release, string entityName = "villager")
        {
            Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
            Assert.Equal(47, version!.Version.Protocol);
            var session = new Session(version, entityName);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session._budget.Token), ScriptedServer.DriveToPlayAsync(session._server, version, session._budget.Token));
                await session.Client.PostAsync(client => WorldFactory.InstallWorld(client.State, new CommonWorldSetup("minecraft:overworld", 0), 47), session._budget.Token);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        internal Task LoadAroundAsync(Vec3d point) => Client.PostAsync(client =>
        {
            for (int x = (int)Math.Floor(point.X - 32) >> 4; x <= (int)Math.Floor(point.X + 32) >> 4; x++)
                for (int z = (int)Math.Floor(point.Z - 32) >> 4; z <= (int)Math.Floor(point.Z + 32) >> 4; z++)
                    client.State.World.LoadColumn(new ChunkPos(x, z));
        }, _budget.Token);

        internal Task UnloadCornerAsync(Vec3d point) => Client.PostAsync(client => Assert.True(client.State.World.UnloadColumn(new ChunkPos((int)Math.Floor(point.X + 32) >> 4, (int)Math.Floor(point.Z + 32) >> 4))), _budget.Token);

        internal Task SpawnAsync(Vec3d point)
        {
            Assert.True(JavaGameData.Registries(47).EntityTypes.TryGet(Identifier.Minecraft(_entityName), out RegistryEntry<EntityTypeDefinition> type));
            return SendAsync(new ClientboundAddMobPacket(42, type.NetworkId, point.X, point.Y, point.Z, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        }

        internal Task AgeAsync(sbyte value) => SendAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(12, MetadataValue.Byte(value))], [])));
        internal Task ReapplyAsync() => SendAsync(new ClientboundMoveEntityPosPacket(42, 0, 0, 0, false));
        internal Task TickAsync() => _ticks.FireAsync(_budget.Token);

        internal Task<Aabb> BoundsAsync() => Client.InvokeAsync(client =>
        {
            Entity entity = Assert.IsType<Entity>(client.State.Entities.Get(42));
            Assert.True(client.State.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(47), out Aabb bounds, out bool blocksBuilding), "Classic entity geometry is unavailable.");
            Assert.True(blocksBuilding);
            return bounds;
        }, _budget.Token);

        private async Task SendAsync(IPacket packet)
        {
            await ScriptedServer.SendAsync(_server, _version.Protocol, ProtocolPhase.Play, packet, _budget.Token);
            long marker = ++_marker;
            await ScriptedServer.SendAsync(_server, _version.Protocol, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(marker), _budget.Token);
            PhaseRegistry registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
            while (true)
            {
                InboundFrame frame = await _server.NextFrameAsync(_budget.Token);
                if (registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec) && codec.Type.Id == PlayPackets.Serverbound.KeepAlive.Id)
                {
                    var response = Assert.IsType<ServerboundPlayKeepAlivePacket>(codec.Decode(frame.Payload, PacketCodecContext.Registryless));
                    if (response.Id == marker)
                        return;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _server.DisposeAsync();
            _budget.Dispose();
        }
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTickSource : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        internal async Task FireAsync(CancellationToken ct)
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
