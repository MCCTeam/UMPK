using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Game.World;
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

public sealed class OrdinaryLivingScaleRuntimeTests
{
    public static IEnumerable<object[]> NativeFamilies()
    {
        using JsonDocument document = Native();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            foreach (string family in Families)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), release.GetProperty("case").GetInt32(), family];
    }

    private static string[] Families => ["villager", "cat", "cow", "horse", "iron_golem", "chicken", "pig", "sheep", "player"];

    [Theory]
    [MemberData(nameof(NativeFamilies))]
    public async Task OriginalAttributeFramesRetainNativeCachedBoundsBeforeTheLivingPhase(string release, int protocol, int caseIndex, string family)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        Assert.Equal(protocol, version.Version.Protocol);
        using JsonDocument document = Native();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray().Where(row => row.GetProperty("family").GetString() == family))
        {
            var harness = new ApplierHarness(version);
            harness.State.Registries = JavaGameData.Registries(protocol);
            var type = harness.State.Registries.EntityTypes[Identifier.Minecraft(family)];
            Vec3d point = Point(row.GetProperty("point"));
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_entity", new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, point.X, point.Y, point.Z, 0, 0, 0, 0, 0, 0, 0, null)));
            Entity entity = Assert.IsType<Entity>(harness.State.Entities.Get(42));
            AssertBox(harness.State, entity, protocol, row.GetProperty("before"));
            byte[] body = Body(native, row);
            ClientboundUpdateAttributesPacket packet = DecodeAttribute(protocol, body, row.GetProperty("input").GetString()!);
            await harness.ApplyAsync(packet);

            AssertBox(harness.State, entity, protocol, row.GetProperty("after_packet"));
            harness.State.EntityGeometry.ObservePosition(entity);
            AssertBox(harness.State, entity, protocol, row.GetProperty("after_position"));
        }
    }

    public static IEnumerable<object[]> SessionCases()
    {
        foreach (int protocol in Enumerable.Range(766, 12))
            foreach (string family in Families)
                yield return [protocol, family];
    }

    [Theory]
    [MemberData(nameof(SessionCases))]
    public async Task RealSessionTickRefreshesFramedScaleIntoObservableBounds(int protocol, string family)
    {
        using JsonDocument document = Native();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().First(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement native = document.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
        JsonElement row = native.GetProperty("rows").EnumerateArray().First(row => row.GetProperty("family").GetString() == family && row.GetProperty("input").GetString() == "2.0" && !row.GetProperty("baby").GetBoolean() && row.GetProperty("target_pose").GetString() == "STANDING" && Point(row.GetProperty("point")) == new Vec3d(1.5, 64, 1.5));
        await using var session = await Session.Create(protocol);
        var type = session.Client.State.Registries!.EntityTypes[Identifier.Minecraft(family)];
        await session.Send(new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, 1.5, 64, 1.5, 0, 0, 0, 0, 0, 0, 0, null));
        await session.Raw("update_attributes", Body(native, row));
        await session.Client.InvokeAsync(client => AssertBox(client.State, client.State.Entities.Get(42)!, protocol, row.GetProperty("after_packet")), session.Token);
        await session.Tick();
        await session.Client.InvokeAsync(client => AssertBox(client.State, client.State.Entities.Get(42)!, protocol, row.GetProperty("after_explicit_refresh")), session.Token);
    }

    [Theory]
    [MemberData(nameof(SessionCases))]
    public async Task PacketColumnMembershipControlsOrdinaryScaleButRetainsPlayerExemption(int protocol, string family)
    {
        using JsonDocument document = Native();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().First(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement native = document.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
        JsonElement original = Find("2.0"), half = Find("0.5");
        await using var session = await Session.Create(protocol, terrain: false);
        var type = session.Client.State.Registries!.EntityTypes[Identifier.Minecraft(family)];
        await session.Send(new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, 1.5, 64, 1.5, 0, 0, 0, 0, 0, 0, 0, null));
        await session.Raw("update_attributes", Body(native, original));
        await session.Tick();
        await Check(family == "player" ? original.GetProperty("after_explicit_refresh") : original.GetProperty("after_packet"));
        await session.Terrain();
        await session.Tick();
        await Check(original.GetProperty("after_explicit_refresh"));
        await session.Send(new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        await session.Raw("update_attributes", Body(native, half), expectedScale: "0.5");
        await session.Tick();
        await Check((family == "player" ? half : original).GetProperty("after_explicit_refresh"));
        await session.Terrain();
        await session.Tick();
        await Check(half.GetProperty("after_explicit_refresh"));
        return;

        JsonElement Find(string input) => native.GetProperty("rows").EnumerateArray().First(row => row.GetProperty("family").GetString() == family && row.GetProperty("input").GetString() == input && !row.GetProperty("baby").GetBoolean() && row.GetProperty("target_pose").GetString() == "STANDING" && Point(row.GetProperty("point")) == new Vec3d(1.5, 64, 1.5));
        Task<bool> Check(JsonElement expected) => session.Client.InvokeAsync(client => AssertBox(client.State, client.State.Entities.Get(42)!, protocol, expected), session.Token);
    }

    public static IEnumerable<object[]> AgeCases()
    {
        foreach (int protocol in Enumerable.Range(766, 12))
            foreach (string family in Families.Where(family => family is not ("player" or "iron_golem")))
                yield return [protocol, family];
    }

    [Theory]
    [MemberData(nameof(AgeCases))]
    public async Task NativeBabyCallbackAppliesCurrentScaleBeforeTheDeferredTick(int protocol, string family)
    {
        await Callback(protocol, family, EntityPose.Standing, baby: true);
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task NativePlayerPoseCallbackAppliesCurrentScale(int protocol)
    {
        foreach (EntityPose pose in new[] { EntityPose.Crouching, EntityPose.Swimming, EntityPose.FallFlying, EntityPose.SpinAttack })
            await Callback(protocol, "player", pose, baby: false);
    }

    [Theory]
    [InlineData(766)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task FixedSleepingAndDyingPlayerDimensionsBypassScale(int protocol)
    {
        await Callback(protocol, "player", EntityPose.Sleeping, baby: false);
        await Callback(protocol, "player", EntityPose.Dying, baby: false);
    }

    private static async Task Callback(int protocol, string family, EntityPose pose, bool baby)
    {
        using JsonDocument document = Native();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().First(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement native = document.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
        string name = pose switch { EntityPose.Standing => "STANDING", EntityPose.Sleeping => "SLEEPING", EntityPose.Dying => "DYING", EntityPose.Crouching => "CROUCHING", EntityPose.Swimming => "SWIMMING", EntityPose.FallFlying => "FALL_FLYING", _ => "SPIN_ATTACK" };
        JsonElement row = native.GetProperty("rows").EnumerateArray().First(row => row.GetProperty("family").GetString() == family && row.GetProperty("input").GetString() == "2.0" && row.GetProperty("baby").GetBoolean() == baby && row.GetProperty("target_pose").GetString() == name && Point(row.GetProperty("point")) == new Vec3d(1.5, 64, 1.5));
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol));
        harness.State.Registries = JavaGameData.Registries(protocol);
        var type = harness.State.Registries.EntityTypes[Identifier.Minecraft(family)];
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_entity", new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, 1.5, 64, 1.5, 0, 0, 0, 0, 0, 0, 0, null)));
        await harness.ApplyAsync(DecodeAttribute(protocol, Body(native, row), "2.0"));
        Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetRule(type.Id, out EntityGeometryRule rule));
        if (baby)
        {
            Assert.True(rule.MetadataIndex >= 0);
            var data = new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(rule.MetadataIndex, MetadataValue.Boolean(true))], []));
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "set_entity_data", data));
        }
        if (pose != EntityPose.Standing)
        {
            var data = new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(rule.PoseMetadataIndex, MetadataValue.Pose(pose))], []));
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "set_entity_data", data));
        }
        AssertBox(harness.State, harness.State.Entities.Get(42)!, protocol, row.GetProperty("after_pose"));
    }

    public static IEnumerable<object[]> NativeMotionCases()
    {
        using JsonDocument document = MotionNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            for (int row = 0; row < 30; row++)
                yield return [release.GetProperty("protocol").GetInt32(), release.GetProperty("case").GetInt32(), row];
    }

    [Theory]
    [MemberData(nameof(NativeMotionCases))]
    public async Task OriginalScaledMotionFramesReachNativeBoundsThroughTheSessionClock(int protocol, int caseIndex, int rowIndex)
    {
        using JsonDocument document = MotionNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        JsonElement row = native.GetProperty("rows")[rowIndex];
        string context = row.GetProperty("context").GetString()!, change = row.GetProperty("change").GetString()!;
        bool far = row.GetProperty("far_sync").GetBoolean();
        await using var session = await Session.Create(protocol);
        await session.Spawn(42, "player", new Vec3d(.5, 65, .5));
        await Check(row.GetProperty("before"));
        JsonElement[] frames = row.GetProperty("frames").EnumerateArray().ToArray();
        int index = 0;
        if (change != "scale_after_target")
        {
            await Frame(frames[index++]);
            await session.Tick();
            await Check(row.GetProperty("after_warm_tick"));
        }
        await Frame(frames[index++]);
        await Check(row.GetProperty("after_move_packet"));
        if (far)
        {
            await Frame(frames[index++]);
            await Check(row.GetProperty("after_sync_packet"));
        }
        int queryX = protocol == 770 ? far ? 202 : 2 : far ? 101 : 1;
        int queryY = protocol == 770 ? 130 : 65;
        await session.Terrain(queryX >> 4);
        if (context == "stone")
            await session.Send(new ClientboundBlockUpdatePacket(new BlockPos(queryX, queryY, 0), session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId));
        else if (context == "border")
            await session.Send(new ClientboundInitializeBorderPacket(far ? 96 : .5, .5, far ? 10 : 2, far ? 10 : 2, 0, 29999984, 5, 15));
        else if (context is "boat" or "shulker")
            await session.Spawn(43, context == "boat" ? "oak_boat" : "shulker", new Vec3d(queryX + .5, queryY, .5));
        if (change == "scale_after_target")
        {
            await Frame(frames[index++]);
            await Check(row.GetProperty("after_attribute_packet"));
        }
        bool unknown = false;
        foreach (JsonElement tick in row.GetProperty("ticks").EnumerateArray())
        {
            if (tick.TryGetProperty("after_attribute_packet", out JsonElement afterAttribute))
            {
                await Frame(frames[index++], "0.5");
                if (!unknown)
                    await Check(afterAttribute);
            }
            await session.Tick();
            unknown |= protocol >= 770 && context == "boat" && tick.GetProperty("collision_queries").GetArrayLength() != 0;
            if (unknown)
                await session.Client.InvokeAsync(client => { Assert.False(client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol)); return true; }, session.Token);
            else
                await Check(tick.GetProperty("after"));
        }
        Assert.Equal(frames.Length, index);
        return;

        Task Frame(JsonElement frame, string scale = "2.0")
        {
            if (frame.TryGetProperty("action", out JsonElement action))
                return session.Raw(action.GetString()!, Convert.FromHexString(native.GetProperty("attributePacketBodies").GetProperty(frame.GetProperty("sha256").GetString()!).GetString()!), expectedScale: scale);
            return session.Raw(frame.GetProperty("kind").GetString()!, Convert.FromHexString(frame.GetProperty("body_hex").GetString()!));
        }
        Task Check(JsonElement expected) => session.Client.InvokeAsync(client =>
        {
            Entity entity = client.State.Entities.Get(42)!;
            Assert.Equal(Point(expected.GetProperty("position")), entity.Position);
            Assert.Equal(Point(expected.GetProperty("packet_base")), client.State.PacketBases.Get(entity));
            return AssertBox(client.State, entity, protocol, expected.GetProperty("geometry"));
        }, session.Token);
    }

    private static JsonDocument MotionNative()
    {
        using Stream source = typeof(OrdinaryLivingScaleRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.OrdinaryLivingScaleMotion.native.json.gz")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task PlacementUsesTheCachedThenRefreshedScaledPlayerBounds(int protocol)
    {
        await using var session = await Session.Create(protocol, actions: true);
        await session.Spawn(42, "player", new Vec3d(.5, 65, 1.5));
        Assert.Equal(protocol == 777 ? 1 : 2, (await session.Use()).Count);
        int scaleId = session.Client.State.Registries!.Attributes.First(entry => AttributeIds.Canonical(entry.Id) == Identifier.Minecraft("scale")).NetworkId;
        await session.Send(new ClientboundUpdateAttributesPacket(42, [new(null, scaleId, 2, [])]));
        Assert.Equal(protocol == 777 ? 1 : 2, (await session.Use()).Count);
        await session.Tick();
        await session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out bool blocking, protocol));
            Assert.True(blocking);
            Assert.True(bounds.Intersects(Aabb.BlockAt(1, 65, 1)));
            return true;
        }, session.Token);
        Assert.Single(await session.Use());
        await session.Send(new ClientboundUpdateAttributesPacket(42, [new(null, scaleId, .5, [])]), expectedScale: "0.5");
        Assert.Single(await session.Use());
        await session.Tick();
        Assert.Equal(protocol == 777 ? 1 : 2, (await session.Use()).Count);
    }

    [Theory]
    [MemberData(nameof(SessionCases))]
    public async Task FrozenClockRetainsPendingScaleExceptForTheNativePlayerExemption(int protocol, string family)
    {
        await using var session = await Session.Create(protocol);
        await session.Spawn(42, family, new Vec3d(1.5, 64, 1.5));
        int scaleId = session.Client.State.Registries!.Attributes.First(entry => AttributeIds.Canonical(entry.Id) == Identifier.Minecraft("scale")).NetworkId;
        await session.Send(new ClientboundTickingStatePacket(20, true));
        await session.Send(new ClientboundUpdateAttributesPacket(42, [new(null, scaleId, 2, [])]));
        Aabb before = await Bounds();
        await session.Tick();
        Aabb frozen = await Bounds();
        Assert.Equal(family == "player" ? before.XSize * 2 : before.XSize, frozen.XSize);
        await session.Send(new ClientboundTickingStepPacket(1));
        await session.Tick();
        Assert.Equal(before.XSize * 2, (await Bounds()).XSize);
        return;

        Task<Aabb> Bounds() => session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(protocol), out Aabb box, out _, protocol));
            return box;
        }, session.Token);
    }

    [Theory]
    [MemberData(nameof(SessionCases))]
    public async Task ReplacedEntityIdsDoNotInheritPendingScale(int protocol, string family)
    {
        await using var session = await Session.Create(protocol);
        await session.Spawn(42, family, new Vec3d(1.5, 64, 1.5));
        Aabb before = await Bounds();
        int scaleId = session.Client.State.Registries!.Attributes.First(entry => AttributeIds.Canonical(entry.Id) == Identifier.Minecraft("scale")).NetworkId;
        await session.Send(new ClientboundUpdateAttributesPacket(42, [new(null, scaleId, 2, [])]));
        await session.Send(new ClientboundRemoveEntitiesPacket([42]));
        await session.Spawn(42, family, new Vec3d(1.5, 64, 1.5));
        await session.Tick();
        Assert.Equal(before, await Bounds());
        return;

        Task<Aabb> Bounds() => session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityGeometry.TryGet(client.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(protocol), out Aabb box, out _, protocol));
            return box;
        }, session.Token);
    }

    public static IEnumerable<object[]> NativeSelfCases()
    {
        using JsonDocument document = LocalNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            foreach (JsonElement row in release.GetProperty("rows").EnumerateArray().Where(row => row.GetProperty("pose").GetString() == "STANDING"))
                yield return [release.GetProperty("protocol").GetInt32(), row.GetProperty("input").GetDouble()];
    }

    [Theory]
    [MemberData(nameof(NativeSelfCases))]
    public async Task FramedSelfScaleReachesTheCachedEyeOnlyAfterTheExistingPhysicsStep(int protocol, double scale)
    {
        using JsonDocument document = LocalNative();
        JsonElement native = document.RootElement.GetProperty("releases").EnumerateArray().First(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement row = native.GetProperty("rows").EnumerateArray().First(row => row.GetProperty("pose").GetString() == "STANDING" && row.GetProperty("input").GetDouble() == scale);
        await using var session = await Session.Create(protocol, actions: true, selfId: 42);
        var before = await session.Client.Snapshots.SelfAsync(session.Token);
        Assert.Equal(row.GetProperty("before").GetProperty("eye_cached_double").GetDouble(), before.EyeHeight);
        string key = row.GetProperty("frames")[0].GetProperty("sha256").GetString()!;
        byte[] body = Convert.FromHexString(native.GetProperty("packetBodies").GetProperty(key).GetString()!);
        await session.Raw("update_attributes", body, expectedScale: scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        var afterPacket = await session.Client.Snapshots.SelfAsync(session.Token);
        Assert.Equal(before.EyeHeight, afterPacket.EyeHeight);
        Assert.Equal(0, await session.Client.InvokeAsync(client => client.State.Entities.All.Count(), session.Token));
        await session.Tick();
        var afterTick = await session.Client.Snapshots.SelfAsync(session.Token);
        double expectedEye = row.GetProperty("after_explicit_refresh").GetProperty("eye_cached_double").GetDouble();
        Assert.Equal(expectedEye, afterTick.EyeHeight);
        Assert.Equal(afterTick.Position.Add(0, expectedEye, 0), afterTick.EyePosition);
        Assert.Equal(expectedEye, await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token));
        var target = new BlockPos((int)Math.Floor(afterTick.Position.X), (int)Math.Floor(afterTick.EyePosition.Y), (int)Math.Floor(afterTick.Position.Z) + 2);
        await session.Send(new ClientboundBlockUpdatePacket(target, session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId));
        BlockHitResult? hit = await session.Client.Snapshots.RaycastViewAsync(4.5, ct: session.Token);
        Assert.NotNull(hit);
        Assert.Equal(target, hit.Value.BlockPos);
        Assert.Equal(afterTick.EyePosition.Y, hit.Value.Point.Y);
    }

    private static JsonDocument LocalNative()
    {
        using Stream source = typeof(OrdinaryLivingScaleRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LocalPlayerScale.native.json.gz")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    public static IEnumerable<object[]> NativeRespawnCases()
    {
        using JsonDocument document = RespawnNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            for (int row = 0; row < 12; row++)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), row];
    }

    [Theory]
    [MemberData(nameof(NativeRespawnCases))]
    public async Task FramedRespawnCopiesOnlyTheNativeScaleValuesIntoTheNewLocalOwner(string release, int protocol, int rowIndex)
    {
        using JsonDocument document = RespawnNative();
        JsonElement native = document.RootElement.GetProperty("releases").EnumerateArray().First(row => row.GetProperty("version").GetString() == release);
        JsonElement row = native.GetProperty("rows")[rowIndex];
        await using var session = await Session.Create(protocol, actions: true, selfId: 42);
        string key = row.GetProperty("frames")[0].GetProperty("sha256").GetString()!;
        byte[] body = Convert.FromHexString(native.GetProperty("packetBodies").GetProperty(key).GetString()!);
        await session.Raw("update_attributes", body, expectedScale: row.GetProperty("base").GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture), expectedModifierCount: 1);
        await session.Tick();
        JsonElement withModifiers = native.GetProperty("rows").EnumerateArray().First(candidate => candidate.GetProperty("base").GetDouble() == row.GetProperty("base").GetDouble() && candidate.GetProperty("mask").GetInt32() == 1);
        Assert.Equal(withModifiers.GetProperty("new_after_explicit_refresh").GetProperty("eye_cached_double").GetDouble(), (await session.Client.Snapshots.SelfAsync(session.Token)).EyeHeight);
        byte mask = (byte)row.GetProperty("mask").GetInt32();
        var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
        await session.Send(new ClientboundRespawnPacket(spawn, mask, null));
        Assert.Equal(row.GetProperty("new_after_transfer").GetProperty("effective_attribute").GetDouble(), await session.Client.InvokeAsync(client => client.State.Self.Attributes.Value(Identifier.Minecraft("scale"), double.NaN), session.Token));
        Assert.Equal(row.GetProperty("new_after_transfer").GetProperty("eye_cached_double").GetDouble(), (await session.Client.Snapshots.SelfAsync(session.Token)).EyeHeight);
        await session.Send(new ClientboundPlayerPositionPacket(2.5, 65, 2.5, 0, 0, 0, 2, new PositionMoveRotation(new Vec3d(2.5, 65, 2.5), Vec3d.Zero, 0, 0)));
        await session.Tick();
        Assert.Equal(row.GetProperty("new_after_explicit_refresh").GetProperty("eye_cached_double").GetDouble(), (await session.Client.Snapshots.SelfAsync(session.Token)).EyeHeight);
        Assert.Equal(0, await session.Client.InvokeAsync(client => client.State.Entities.All.Count(), session.Token));
    }

    private static JsonDocument RespawnNative()
    {
        using Stream source = typeof(OrdinaryLivingScaleRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LocalScaleRespawn.native.json.gz")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static byte[] Body(JsonElement native, JsonElement row) => Convert.FromHexString(native.GetProperty("packetBodies").GetProperty(row.GetProperty("frames")[0].GetProperty("sha256").GetString()!).GetString()!);

    private static ClientboundUpdateAttributesPacket DecodeAttribute(int protocol, byte[] body, string input, int modifierCount = 0)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "update_attributes");
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var packet = Assert.IsType<ClientboundUpdateAttributesPacket>(codec.Decode(body, context));
        Assert.Equal(42, packet.EntityId);
        AttributeSnapshot attribute = Assert.Single(packet.Attributes);
        Assert.Equal(modifierCount, attribute.Modifiers.Count);
        if (modifierCount == 1)
        {
            AttributeModifierEntry modifier = Assert.Single(attribute.Modifiers);
            Assert.Equal(0, modifier.Operation);
            Assert.True(modifier.Amount is .5 or -.5);
            if (protocol == 766)
                Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000007"), modifier.LegacyUuid);
            else
                Assert.Equal("minecraft:native_scale_copy", modifier.ModernId);
        }
        Assert.Equal(input switch { "Infinity" => double.PositiveInfinity, "-Infinity" => double.NegativeInfinity, "NaN" => double.NaN, _ => double.Parse(input, System.Globalization.CultureInfo.InvariantCulture) }, attribute.BaseValue);
        var registered = JavaGameData.Registries(protocol).Attributes[attribute.ModernId];
        Assert.Equal(Identifier.Minecraft("scale"), AttributeIds.Canonical(registered.Id));
        var encoded = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(encoded);
        codec.Encode(ref writer, packet, context);
        Assert.Equal(body, encoded.WrittenSpan.ToArray());
        return packet;
    }

    private static bool AssertBox(ClientState state, Entity entity, int protocol, JsonElement native)
    {
        Assert.True(state.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(protocol), out Aabb box, out bool blocking, protocol));
        Assert.True(blocking);
        JsonElement expected = native.GetProperty("body");
        Assert.Equal(new Aabb(expected[0].GetDouble(), expected[1].GetDouble(), expected[2].GetDouble(), expected[3].GetDouble(), expected[4].GetDouble(), expected[5].GetDouble()), box);
        return true;
    }

    private static Vec3d Point(JsonElement point) => new(point[0].GetDouble(), point[1].GetDouble(), point[2].GetDouble());

    private static JsonDocument Native()
    {
        using Stream source = typeof(OrdinaryLivingScaleRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.OrdinaryLivingScale.native.json.gz")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly JavaVersion _version;
        private readonly FakeJavaServer _server = FakeJavaServer.Create();
        private readonly ObservedTicks _ticks = new();
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(30));
        private long _nonce;
        public UmpkClient Client { get; }
        public CancellationToken Token => _budget.Token;

        private Session(int protocol, bool actions)
        {
            _version = BoundPacketApplierHarness.Version(protocol);
            Client = new UmpkClientBuilder().UseVersion(_version).UseProfile(new GameProfile(Guid.NewGuid(), "ScaleProbe"))
                .UseConnectionFactory(new PipeFactory(_server.ClientPipe)).UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(_ticks)
                .ConfigureFeatures(features => { features.Entities = true; features.Terrain = true; features.Physics = actions; features.Pathfinding = false; })
                .Build();
        }

        public static async Task<Session> Create(int protocol, bool terrain = true, bool actions = false, int selfId = 1)
        {
            var session = new Session(protocol, actions);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session.Token), ScriptedServer.DriveToPlayAsync(session._server, session._version, session.Token));
                var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable subscription = session.Client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
                await session.Send(new ClientboundLoginPacket(selfId, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63), false, false, null), barrier: false);
                await joined.Task.WaitAsync(session.Token);
                await session.Barrier();
                if (terrain)
                    await session.Terrain();
                if (actions)
                {
                    await session.Send(new ClientboundPlayerPositionPacket(2.5, 65, 2.5, 0, 0, 0, 1, new PositionMoveRotation(new Vec3d(2.5, 65, 2.5), Vec3d.Zero, 0, 0)));
                    var stone = session.Client.State.Registries!.Items[Identifier.Minecraft("stone")];
                    await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, new ItemStack(stone, 1)));
                    await session.Send(new ClientboundBlockUpdatePacket(new BlockPos(1, 64, 1), session.Client.State.Registries.Blocks[Identifier.Minecraft("dirt")].Value.DefaultStateId));
                    await session.Tick();
                }
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async Task Terrain(int columnX = 0)
        {
            int protocol = _version.Version.Protocol;
            var empty = new ChunkPalettedContainerWire(0, 0, null, []);
            int count = WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), Client.State.Registries, protocol).SectionCount;
            var sections = Enumerable.Range(0, count).Select(_ => new ChunkSectionWire(0, protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
            ChunkWireBody column = protocol >= 770
                ? new ModernChunkWireBody(columnX, 0, protocol >= 775, [], sections, [], new byte[7])
                : protocol >= 768 ? new NbtHeightmapChunkWireBody(columnX, 0, new NbtCompound(), sections, [], new byte[7])
                : new Protocol764To767ChunkWireBody(columnX, 0, new NbtCompound(), sections, [], new byte[7]);
            byte[] bytes = ChunkCodecs.EncodeStructural(column);
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "level_chunk_with_light");
            var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty)));
            Assert.True(packet.FullChunk);
            Assert.Equal(columnX, packet.ChunkX);
            Assert.Equal(0, packet.ChunkZ);
            Assert.Equal(count, packet.Column.SectionCount);
            Assert.Equal(0, packet.Column.GetBlockStateId(1, 64, 1));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
            await Raw("level_chunk_with_light", bytes);
        }

        public async Task<List<object>> Use()
        {
            Task action = Client.Actions.Interaction.PlaceBlockAsync(new BlockPos(1, 64, 1), Direction.Up, new Vec3d(.5, 1, .5), ct: Token);
            List<object> sent = [];
            ServerboundUseItemOnPacket? use = null;
            var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
            while (use is null)
            {
                InboundFrame frame = await _server.NextFrameAsync(Token);
                if (!registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec))
                    continue;
                object packet = codec.Decode(frame.Payload, new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty));
                if (packet is ServerboundUseItemOnPacket or ServerboundSwingPacket)
                    sent.Add(packet);
                use = packet as ServerboundUseItemOnPacket;
            }
            await Send(new ClientboundBlockChangedAckPacket(use.Sequence), barrier: false);
            await action.WaitAsync(Token);
            sent.AddRange(await Barrier());
            Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
            if (sent.Count == 2)
                Assert.IsType<ServerboundSwingPacket>(sent[1]);
            return sent;
        }

        public Task Spawn(int id, string family, Vec3d point)
        {
            var type = Client.State.Registries!.EntityTypes[Identifier.Minecraft(family)];
            return Send(new ClientboundAddEntityPacket(id, Guid.Empty, type.NetworkId, point.X, point.Y, point.Z, 0, 0, 0, 0, 0, 0, 0, null));
        }

        public Task Tick() => _ticks.Fire(Token);

        public Task Send(IPacket packet, bool barrier = true, string expectedScale = "2.0")
        {
            var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == packet.Type.Id);
            Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty));
            return Raw(packet.Type.Id.Path, buffer.WrittenSpan.ToArray(), barrier, expectedScale);
        }

        public async Task Raw(string name, byte[] body, bool barrier = true, string expectedScale = "2.0", int expectedModifierCount = 0)
        {
            var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft(name));
            Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
            var context = new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty);
            object decoded = codec.Decode(body, context);
            var repeated = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(repeated);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(body, repeated.WrittenSpan.ToArray());
            if (decoded is ClientboundMoveEntityPosPacket move)
            {
                Assert.Equal(42, move.EntityId);
                Assert.Equal((short)4096, move.DeltaX);
                Assert.Equal((short)0, move.DeltaY);
                Assert.Equal((short)0, move.DeltaZ);
                Assert.False(move.OnGround);
                Assert.Empty(move.Steps);
            }
            if (decoded is ClientboundEntityPositionSyncPacket sync)
            {
                Assert.Equal(42, sync.EntityId);
                Assert.Equal(new Vec3d(100.5, 65, .5), sync.Values.Position);
                Assert.Equal(Vec3d.Zero, sync.Values.DeltaMovement);
                Assert.False(sync.OnGround);
            }
            if (decoded is ClientboundRespawnPacket respawn)
            {
                Assert.NotNull(respawn.SpawnInfo);
                Assert.Equal("minecraft:overworld", respawn.SpawnInfo.Dimension);
                Assert.InRange(respawn.DataToKeep, (byte)0, (byte)3);
            }
            if (name == "update_attributes")
                _ = DecodeAttribute(_version.Version.Protocol, body, expectedScale, expectedModifierCount);
            await _server.SendFrameAsync(pair.WireId, body, Token);
            if (barrier)
                await Barrier();
        }

        private async Task<List<object>> Barrier()
        {
            List<object> observed = [];
            long nonce = ++_nonce;
            await Send(new ClientboundPlayKeepAlivePacket(nonce), barrier: false);
            var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
            while (true)
            {
                InboundFrame frame = await _server.NextFrameAsync(Token);
                if (!registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec))
                    continue;
                object packet = codec.Decode(frame.Payload, new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty));
                if (packet is not ServerboundPlayKeepAlivePacket)
                {
                    if (packet is ServerboundUseItemOnPacket or ServerboundSwingPacket)
                        observed.Add(packet);
                    continue;
                }
                var reply = Assert.IsType<ServerboundPlayKeepAlivePacket>(codec.Decode(frame.Payload, new PacketCodecContext(Client.State.Registries!, IConnectionCodecState.Empty)));
                if (reply.Id == nonce)
                    return observed;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await Client.DisposeAsync(); }
            finally { await _server.DisposeAsync(); _budget.Dispose(); }
        }
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
            long tick = 0;
            await foreach (TaskCompletionSource completed in _ticks.Reader.ReadAllAsync(ct))
            {
                yield return ++tick;
                completed.TrySetResult();
            }
        }
    }
}
