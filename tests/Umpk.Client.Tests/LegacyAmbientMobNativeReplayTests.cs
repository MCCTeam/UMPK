using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyAmbientMobNativeReplayTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument native = ReadNative();
        foreach (JsonElement row in native.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetProperty("id").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CompletedNativeRowsKeepPoseVelocityCachedBoxAndMembershipAtOwnerBoundaries(string scenario)
    {
        using JsonDocument document = ReadNative();
        JsonElement row = document.RootElement.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("id").GetString() == scenario);
        var version = BoundPacketApplierHarness.Version(315);
        var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(315);
        WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), 315);
        var context = new PacketCodecContext(harness.State.Registries, IConnectionCodecState.Empty);
        JsonElement[] packets = row.GetProperty("packets").EnumerateArray().ToArray();
        var actors = new Dictionary<int, Entity>();
        int nextPacket = 0, ticks = 0, reentries = 0;
        foreach (JsonElement expected in row.GetProperty("observations").EnumerateArray())
        {
            string phase = expected.GetProperty("phase").GetString()!;
            int id = expected.GetProperty("actor_id").GetInt32();
            int identity = expected.GetProperty("identity").GetInt32();
            if (phase.StartsWith("after_packet_", StringComparison.Ordinal))
                await ThroughPacket(phase["after_packet_".Length..]);
            else if (phase == "removed_old_actor")
                await ThroughPacket("remove_actor");
            else if (phase.StartsWith("after_original_entity_traversal_", StringComparison.Ordinal))
            {
                harness.State.TickEntities(315, JavaGameData.BlockShapes(315), [], new Aabb(-.3, 0, -.3, .3, 1.8, .3));
                Assert.Equal(++ticks, expected.GetProperty("logical_tick").GetInt32());
            }
            else if (phase.StartsWith("after_original_client_world_reentry_", StringComparison.Ordinal))
            {
                harness.State.EntityMembership.FinishWorldPhase();
                Assert.Equal(++reentries, expected.GetProperty("logical_tick").GetInt32());
            }
            else if (phase is "native_world_actor_enter" or "native_world_actor_exit")
                continue;
            else if (phase == "after_unchanged_position_setter")
                harness.State.EntityGeometry.ObservePosition(actors[identity]);

            if (expected.GetProperty("id_lookup_is_actor").GetBoolean())
            {
                Entity actor = Assert.IsType<Entity>(harness.State.Entities.Get(id));
                if (actors.TryGetValue(identity, out Entity? prior))
                    Assert.Same(prior, actor);
                else
                {
                    Assert.DoesNotContain(actors.Values, existing => ReferenceEquals(existing, actor));
                    actors.Add(identity, actor);
                }
                AssertState(actor, expected);
            }
            else if (actors.TryGetValue(identity, out Entity? removed))
            {
                Assert.NotSame(removed, harness.State.Entities.Get(id));
                Assert.Equal(Point(expected.GetProperty("position")), removed.Position);
                Assert.Equal(Point(expected.GetProperty("velocity")), removed.Velocity);
                Assert.False(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(removed, out _));
                Assert.False(harness.State.EntityMembership.TryGetLegacyMembership(removed, out _));
            }
        }
        Assert.Equal(packets.Length, nextPacket);
        Assert.Equal(scenario.EndsWith("/velocity", StringComparison.Ordinal) ? 9 : 8, ticks);
        Assert.Equal(ticks, reentries);
        return;

        async Task ThroughPacket(string phase)
        {
            bool found = false;
            while (nextPacket < packets.Length)
            {
                JsonElement packet = packets[nextPacket++];
                string packetPhase = packet.GetProperty("phase").GetString()!;
                object decoded;
                if (packetPhase == "controlled_native_block_input")
                {
                    JsonElement position = packet.GetProperty("position");
                    int nativeState = packet.GetProperty("native_state_id").GetInt32();
                    // Original Block.getStateId uses id | metadata << 12; this era's block-update packet uses id << 4 | metadata.
                    int wireState = ((nativeState & 4095) << 4) | ((nativeState >> 12) & 15);
                    decoded = BoundDescriptorCodec.RoundTrip(315, "block_update", new ClientboundBlockUpdatePacket(
                        new BlockPos(position[0].GetInt32(), position[1].GetInt32(), position[2].GetInt32()), wireState));
                }
                else
                {
                    byte[] body = Convert.FromHexString(packet.GetProperty("body_hex").GetString()!);
                    Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).TryGetInbound(packet.GetProperty("native_wire_id").GetInt32(), out BoundPacketCodec codec));
                    Assert.True(codec.IsImplemented);
                    decoded = codec.Decode(body, context);
                    var output = new ArrayBufferWriter<byte>();
                    var writer = new PacketWriter(output);
                    codec.Encode(ref writer, decoded, context);
                    Assert.Equal(body, output.WrittenSpan.ToArray());
                }
                await harness.ApplyAsync(decoded);
                if (packetPhase == phase)
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, phase);
        }

        void AssertState(Entity actor, JsonElement expected)
        {
            string label = scenario + " " + expected.GetProperty("phase").GetString() + " tick " + expected.GetProperty("logical_tick").GetInt32();
            Assert.True(Point(expected.GetProperty("position")) == actor.Position, label + " position, expected " + Point(expected.GetProperty("position")) + ", actual " + actor.Position);
            Assert.True(Point(expected.GetProperty("velocity")) == actor.Velocity, label + " velocity");
            AssertVectorBits(expected.GetProperty("position"), actor.Position, label + " position");
            AssertVectorBits(expected.GetProperty("velocity"), actor.Velocity, label + " velocity");
            Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out EntityMotionState.LegacyAmbientInterpolationSnapshot motion), label + " owned interpolation");
            Assert.True(motion.PositionKnown, label + " proven complete position");
            AssertVectorBits(expected.GetProperty("interpolation_target"), motion.Target, label + " pending target");
            Assert.Equal(expected.GetProperty("interpolation_steps").GetInt32(), motion.RemainingSteps);
            Assert.True(harness.State.EntityMembership.TryGetLegacyMembership(actor, out EntityMembershipState.LegacyMembershipSnapshot membership), label + " owned membership");
            Assert.True(membership.RegistrationKnown, label + " proven native registration");
            Assert.Equal(expected.GetProperty("in_entity_list").GetBoolean(), membership.InEntityList);
            Assert.Equal(expected.GetProperty("added_to_chunk").GetBoolean(), membership.Added);
            Assert.Equal(expected.GetProperty("removed").GetBoolean(), membership.Removed);
            JsonElement registered = expected.GetProperty("registered_chunk_section");
            Assert.Equal((registered[0].GetInt32(), registered[1].GetInt32(), registered[2].GetInt32()), (membership.X, membership.Y, membership.Z));
            Assert.Equal(expected.GetProperty("actual_registered_column_sections").EnumerateArray().Select(value => value.GetInt32()).ToArray(), membership.ActualSection is { } section ? [section] : []);
            Assert.True(membership.HealthKnown);
            Assert.Equal(expected.GetProperty("health").GetSingle(), membership.Health);
            Assert.Equal(expected.GetProperty("on_ground").GetBoolean(), actor.OnGround);
            Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(315), out Aabb box, out bool blocks, 315), label + " known cached box");
            Assert.True(blocks);
            JsonElement cached = expected.GetProperty("cached_box");
            double[] actual = [box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ];
            for (int index = 0; index < actual.Length; index++)
                Assert.True(cached[index].GetProperty("raw64").GetString() == unchecked((ulong)BitConverter.DoubleToInt64Bits(actual[index])).ToString("x16", System.Globalization.CultureInfo.InvariantCulture), label + " cached box component " + index);
            bool member = !expected.GetProperty("removed").GetBoolean() && expected.GetProperty("actual_registered_column_sections").GetArrayLength() != 0;
            Assert.Equal(member, harness.State.EntityMembership.IsWorldQueryMember(actor));
            Vec3d packetBase = harness.State.PacketBases.Get(actor);
            JsonElement nativeBase = expected.GetProperty("packet_base");
            Assert.Equal(nativeBase[0].GetInt64(), (long)Math.Floor(packetBase.X * 4096));
            Assert.Equal(nativeBase[1].GetInt64(), (long)Math.Floor(packetBase.Y * 4096));
            Assert.Equal(nativeBase[2].GetInt64(), (long)Math.Floor(packetBase.Z * 4096));
        }
    }

    internal static JsonDocument ReadNative()
    {
        using Stream resource = typeof(LegacyAmbientMobNativeReplayTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyAmbientMob315.native.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static Vec3d Point(JsonElement value) => new(value[0].GetProperty("value").GetDouble(), value[1].GetProperty("value").GetDouble(), value[2].GetProperty("value").GetDouble());

    private static void AssertVectorBits(JsonElement expected, Vec3d actual, string label)
    {
        double[] components = [actual.X, actual.Y, actual.Z];
        for (int index = 0; index < components.Length; index++)
            Assert.True(expected[index].GetProperty("raw64").GetString() == unchecked((ulong)BitConverter.DoubleToInt64Bits(components[index])).ToString("x16", System.Globalization.CultureInfo.InvariantCulture), label + " component " + index);
    }
}
