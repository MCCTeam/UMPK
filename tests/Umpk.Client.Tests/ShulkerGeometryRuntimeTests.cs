using System.Text.Json;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ShulkerGeometryRuntimeTests
{
    [Theory]
    [MemberData(nameof(VanillaShulkerGeometryTests.NativeScenarios), MemberType = typeof(VanillaShulkerGeometryTests))]
    public async Task TrackedShulkerBoundsFollowNativeMetadataAndPeekPhases(string version, int protocol, int index)
    {
        using Stream stream = typeof(ShulkerGeometryRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.ShulkerGeometry.native.json")!;
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement scenario = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()][index];
        JsonElement input = scenario.GetProperty("input");
        var position = new Vec3d(input[0].GetDouble(), input[1].GetDouble(), input[2].GetDouble());
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol));
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(Internal.WorldFactory.InstallWorld(harness.State, new Internal.CommonWorldSetup("minecraft:overworld", 0), protocol));
        harness.State.World.SetBlockStateId(new BlockPos((int)Math.Floor(position.X), (int)Math.Floor(position.Y), (int)Math.Floor(position.Z)), 0);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("shulker"), out RegistryEntry<EntityTypeDefinition> type));
        await harness.ApplyAsync(new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
        Entity entity = Assert.IsType<Entity>(harness.State.Entities.Get(42));
        Assert.Equal(position, harness.State.PacketBases.Get(entity));
        await Scale(scenario.GetProperty("scale").GetDouble());
        await Face(scenario.GetProperty("face"));
        await Position(position);
        AssertState(scenario.GetProperty("initial"));

        foreach (JsonElement step in scenario.GetProperty("steps").EnumerateArray())
        {
            switch (step[0].GetInt32())
            {
                case 0:
                    await Peek((sbyte)step[1].GetInt32());
                    break;
                case 1:
                    Tick();
                    break;
                case 2:
                    await Position(new Vec3d(position.X + 2, position.Y + .7, position.Z - 3));
                    break;
                case 3:
                    await Peek(100);
                    for (int count = 0; count < step[1].GetInt32(); count++)
                        Tick();
                    break;
                case 4:
                    await Scale(step[1].GetDouble());
                    break;
                case 5:
                    await Face(step[1]);
                    break;
                default:
                    throw new InvalidOperationException("Unknown native shulker operation.");
            }
            AssertState(step[2]);
        }

        ValueTask Peek(sbyte value) => harness.ApplyAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(17, MetadataValue.Byte(value))], [])));
        ValueTask Face(JsonElement value) => harness.ApplyAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(16, MetadataValue.Direction(Enum.Parse<Direction>(value.GetString()!, ignoreCase: true)))], [])));
        ValueTask Scale(double value)
        {
            Assert.True(harness.State.Registries.Attributes.TryGetNetworkId(Identifier.Minecraft("scale"), out int holder));
            return harness.ApplyAsync(new ClientboundUpdateAttributesPacket(42, [new AttributeSnapshot(null, holder, value, [])]));
        }
        ValueTask Position(Vec3d value) => harness.ApplyAsync(new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(value, Vec3d.Zero, 0, 0), false));
        void Tick() => harness.State.EntityGeometry.TickLivingDimensions(harness.State.Entities, harness.State.World);
        void AssertState(JsonElement expected)
        {
            Assert.Equal(new Vec3d(expected[0].GetDouble(), expected[1].GetDouble(), expected[2].GetDouble()), entity.Position);
            Assert.True(harness.State.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(protocol), out Aabb box, out bool blocking), "The tracked shulker has no native geometry.");
            Assert.True(blocking);
            Assert.Equal(new Aabb(expected[3].GetDouble(), expected[4].GetDouble(), expected[5].GetDouble(), expected[6].GetDouble(), expected[7].GetDouble(), expected[8].GetDouble()), box);
            sbyte rawPeek = entity.Metadata.TryGet(17, out MetadataValue raw) ? raw.AsByte() : (sbyte)0;
            Assert.Equal((sbyte)expected[11].GetInt32(), rawPeek);
        }
    }
}
