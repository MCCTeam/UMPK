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

public sealed class LegacyBatFluidNativeReplayTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument native = ReadNative();
        foreach (JsonElement row in native.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetProperty("id").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalFluidRowsDriveBoundPacketsThroughNativeBodyVelocityAndMembership(string scenario)
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
            await ThroughPackets(expected.GetProperty("packet_count_before").GetInt32());
            if (phase.StartsWith("after_original_entity_traversal_", StringComparison.Ordinal))
            {
                harness.State.TickEntities(315, JavaGameData.BlockShapes(315), [], new Aabb(-.3, 0, -.3, .3, 1.8, .3));
                Assert.Equal(++ticks, expected.GetProperty("logical_tick").GetInt32());
            }
            else if (phase.StartsWith("after_original_client_world_reentry_", StringComparison.Ordinal))
            {
                harness.State.EntityMembership.FinishWorldPhase();
                Assert.Equal(++reentries, expected.GetProperty("logical_tick").GetInt32());
            }

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

        }
        Assert.Equal(packets.Length, nextPacket);
        Assert.Equal(4, ticks);
        Assert.Equal(ticks, reentries);
        return;

        async Task ThroughPackets(int count)
        {
            Assert.InRange(count, nextPacket, packets.Length);
            while (nextPacket < count)
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
                    var expectedBlock = new ClientboundBlockUpdatePacket(new BlockPos(position[0].GetInt32(), position[1].GetInt32(), position[2].GetInt32()), wireState);
                    BoundPacketCodec blockCodec = BoundDescriptorCodec.Clientbound(315, "block_update");
                    var body = new ArrayBufferWriter<byte>();
                    var blockWriter = new PacketWriter(body);
                    blockCodec.Encode(ref blockWriter, expectedBlock, context);
                    decoded = blockCodec.Decode(body.WrittenSpan, context);
                    Assert.Equal(expectedBlock, Assert.IsType<ClientboundBlockUpdatePacket>(decoded));
                    var blockReadback = new ArrayBufferWriter<byte>();
                    var blockReadbackWriter = new PacketWriter(blockReadback);
                    blockCodec.Encode(ref blockReadbackWriter, decoded, context);
                    Assert.Equal(body.WrittenSpan.ToArray(), blockReadback.WrittenSpan.ToArray());
                }
                else
                {
                    byte[] body = Convert.FromHexString(packet.GetProperty("body_hex").GetString()!);
                    Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).TryGetInbound(packet.GetProperty("native_wire_id").GetInt32(), out BoundPacketCodec codec));
                    Assert.True(codec.IsImplemented);
                    decoded = codec.Decode(body, context);
                    AssertDecoded(packet.GetProperty("decoded_values"), decoded);
                    var output = new ArrayBufferWriter<byte>();
                    var writer = new PacketWriter(output);
                    codec.Encode(ref writer, decoded, context);
                    Assert.Equal(body, output.WrittenSpan.ToArray());
                }
                await harness.ApplyAsync(decoded);
            }
            Assert.Equal(count, nextPacket);
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
        using Stream resource = typeof(LegacyBatFluidNativeReplayTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyBatFluid315.native.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void AssertDecoded(JsonElement expected, object decoded)
    {
        switch (decoded)
        {
            case ClientboundLevelChunkPacket chunk:
                Assert.Equal(expected.GetProperty("chunk")[0].GetInt32(), chunk.ChunkX);
                Assert.Equal(expected.GetProperty("chunk")[1].GetInt32(), chunk.ChunkZ);
                Assert.Equal(expected.GetProperty("full").GetBoolean(), chunk.FullChunk);
                Assert.Equal(new ChunkPos(chunk.ChunkX, chunk.ChunkZ), chunk.Column.Position);
                Assert.Equal(expected.GetProperty("block_entity_entries").GetInt32(), chunk.BlockEntities.Count);
                break;
            case ClientboundAddMobPacket spawn:
                Assert.Equal(expected.GetProperty("id").GetInt32(), spawn.EntityId);
                Assert.Equal(expected.GetProperty("type").GetInt32(), spawn.TypeId);
                Assert.Equal(Guid.Parse(expected.GetProperty("uuid").GetString()!), spawn.Uuid);
                AssertVectorBits(expected.GetProperty("position"), new Vec3d(spawn.X, spawn.Y, spawn.Z), "decoded spawn position");
                Assert.Equal(expected.GetProperty("velocity_raw_short").EnumerateArray().Select(v => v.GetInt16()), new[] { spawn.VelocityX, spawn.VelocityY, spawn.VelocityZ });
                Assert.Equal(expected.GetProperty("angle_bytes")[0].GetInt32() * 360F / 256, spawn.Yaw);
                Assert.Equal(expected.GetProperty("angle_bytes")[1].GetInt32() * 360F / 256, spawn.Pitch);
                Assert.Equal(expected.GetProperty("angle_bytes")[2].GetInt32() * 360F / 256, spawn.HeadPitch);
                AssertMetadata(expected.GetProperty("metadata"), spawn.Metadata);
                break;
            case ClientboundSetEntityDataPacket metadata:
                Assert.Equal(expected.GetProperty("id").GetInt32(), metadata.EntityId);
                AssertMetadata(expected.GetProperty("metadata"), metadata.Metadata);
                break;
            case ClientboundSetEntityMotionPacket velocity:
                Assert.Equal(expected.GetProperty("id").GetInt32(), velocity.EntityId);
                Assert.Equal(expected.GetProperty("velocity_raw_short").EnumerateArray().Select(v => v.GetInt16()), new[] { velocity.VelocityX, velocity.VelocityY, velocity.VelocityZ });
                Assert.Null(velocity.ModernVelocityRaw);
                break;
            case ClientboundMoveEntityPosRotPacket movement:
                Assert.Equal(expected.GetProperty("id").GetInt32(), movement.EntityId);
                Assert.Equal(expected.GetProperty("delta_raw_short").EnumerateArray().Select(v => v.GetInt16()), new[] { movement.DeltaX, movement.DeltaY, movement.DeltaZ });
                Assert.Equal(expected.GetProperty("angles_raw_byte")[0].GetInt32() * 360F / 256, movement.Yaw);
                Assert.Equal(expected.GetProperty("angles_raw_byte")[1].GetInt32() * 360F / 256, movement.Pitch);
                Assert.True(expected.GetProperty("has_rotation").GetBoolean());
                Assert.Equal(expected.GetProperty("on_ground").GetBoolean(), movement.OnGround);
                Assert.Empty(movement.Steps);
                break;
            default:
                Assert.Fail("Unreviewed fluid-fixture packet: " + decoded.GetType().Name);
                break;
        }
    }

    private static void AssertMetadata(JsonElement expected, EntityMetadataList actual)
    {
        Assert.Empty(actual.RawTail);
        Assert.Equal(expected.GetArrayLength(), actual.Entries.Count);
        foreach (JsonElement entry in expected.EnumerateArray())
        {
            EntityDataEntry decoded = Assert.Single(actual.Entries, candidate => candidate.Index == entry.GetProperty("index").GetInt32());
            MetadataValue value = decoded.Value;
            JsonElement native = entry.GetProperty("value");
            Assert.Equal(entry.GetProperty("serializer_id").GetInt32(), decoded.WireSerializerId);
            switch (entry.GetProperty("serializer_id").GetInt32())
            {
                case 0:
                    Assert.Equal(native.GetSByte(), value.AsByte());
                    break;
                case 1:
                    Assert.Equal(native.GetInt32(), value.AsVarInt());
                    break;
                case 2:
                    Assert.Equal(entry.GetProperty("raw32").GetString(), unchecked((uint)BitConverter.SingleToInt32Bits(value.AsFloat())).ToString("x8", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case 3:
                    Assert.Equal(native.GetString(), value.AsString());
                    break;
                case 6:
                    Assert.Equal(native.GetBoolean(), value.AsBoolean());
                    break;
                default:
                    Assert.Fail("Unreviewed fluid-fixture metadata serializer");
                    break;
            }
        }
    }

    private static Vec3d Point(JsonElement value) => new(value[0].GetProperty("value").GetDouble(), value[1].GetProperty("value").GetDouble(), value[2].GetProperty("value").GetDouble());

    private static void AssertVectorBits(JsonElement expected, Vec3d actual, string label)
    {
        double[] components = [actual.X, actual.Y, actual.Z];
        for (int index = 0; index < components.Length; index++)
            Assert.True(expected[index].GetProperty("raw64").GetString() == unchecked((ulong)BitConverter.DoubleToInt64Bits(components[index])).ToString("x16", System.Globalization.CultureInfo.InvariantCulture), label + " component " + index);
    }
}
