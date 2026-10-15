using System.Text.Json;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class SpawnMetadataTests
{
    public static TheoryData<int> InlineProtocols => [47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404, 477, 480, 485, 490, 498];

    [Theory]
    [MemberData(nameof(InlineProtocols))]
    public async Task MobSpawnRetainsItsInitialMetadataBeforePublishing(int protocol)
        => await AssertInitialMetadataAsync(protocol, player: false);

    [Theory]
    [MemberData(nameof(InlineProtocols))]
    public async Task PlayerSpawnRetainsItsInitialMetadataBeforePublishing(int protocol)
        => await AssertInitialMetadataAsync(protocol, player: true);

    public static IEnumerable<object[]> NativeSpawns
    {
        get
        {
            using JsonDocument document = ReadNativeSpawns();
            foreach (JsonElement leg in document.RootElement.GetProperty("legs").EnumerateArray())
                yield return [leg.GetProperty("version").GetString()!, leg.GetProperty("protocol").GetInt32()];
        }
    }

    [Theory]
    [MemberData(nameof(NativeSpawns))]
    public async Task CapturedSpawnInstallsAllFieldsBeforeAnyLaterMetadataFrame(string release, int protocol)
    {
        using JsonDocument document = ReadNativeSpawns();
        JsonElement leg = document.RootElement.GetProperty("legs").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release);
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        var context = new PacketCodecContext(harness.State.Registries, IConnectionCodecState.Empty);
        var spawn = Assert.IsType<ClientboundAddMobPacket>(BoundDescriptorCodec.Clientbound(protocol, "add_mob").Decode(Convert.FromHexString(leg.GetProperty("spawn_body_hex").GetString()!), context));
        bool inline = leg.GetProperty("inline").GetBoolean();
        Assert.Equal(leg.GetProperty("entity_id").GetInt32(), spawn.EntityId);
        Assert.Equal(inline, spawn.Metadata.Entries.Count > 0);
        string? nameAtSpawn = null;
        using IDisposable subscription = harness.Events.Subscribe<EntitySpawned>(evt => nameAtSpawn = evt.Entity.CustomName?.ToPlainText());

        await harness.ApplyAsync(spawn);

        Entity entity = Assert.IsType<Entity>(harness.State.Entities.Get(spawn.EntityId));
        Assert.Equal(spawn.Metadata.Entries.Count, entity.Metadata.Count);
        foreach (EntityDataEntry entry in spawn.Metadata.Entries)
        {
            Assert.True(entity.Metadata.TryGet(entry.Index, out MetadataValue stored));
            Assert.Equal(entry.Value, stored);
        }
        Assert.Equal(inline ? "SPAWNPROOF" : null, nameAtSpawn);
        Assert.Equal(nameAtSpawn, entity.CustomName?.ToPlainText());

        var update = Assert.IsType<ClientboundSetEntityDataPacket>(BoundDescriptorCodec.Clientbound(protocol, "set_entity_data").Decode(Convert.FromHexString(leg.GetProperty("name_update_body_hex").GetString()!), context));
        await harness.ApplyAsync(update);

        Assert.Equal("SPAWNPROOF", entity.CustomName?.ToPlainText());
    }

    private static JsonDocument ReadNativeSpawns()
    {
        using Stream stream = typeof(SpawnMetadataTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.SpawnMetadata.native.json")!;
        return JsonDocument.Parse(stream);
    }

    private static async Task AssertInitialMetadataAsync(int protocol, bool player)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("cow"), out RegistryEntry<EntityTypeDefinition> cow));
        MetadataValue name = protocol <= 340 ? MetadataValue.String("Initial") : MetadataValue.OptionalComponent(Component.Text("Initial"));
        List<EntityDataEntry> entries = [new(0, MetadataValue.Byte(0x20)), new(2, name)];
        if (protocol >= 477)
            entries.Add(new EntityDataEntry(6, MetadataValue.Pose(EntityPose.Dying)));
        var metadata = new EntityMetadataList(entries, []);
        IPacket spawn = player
            ? new ClientboundAddPlayerPacket(42, Guid.Empty, 8.5, 64, -12.25, 0, 0, 0, metadata)
            : new ClientboundAddMobPacket(42, cow.NetworkId, 8.5, 64, -12.25, 0, 0, 0, 0, 0, 0, metadata);

        // Pass through the bound codec so every theory leg supplies a valid spawn record for its actual wire era.
        object decoded = BoundDescriptorCodec.RoundTrip(protocol, player ? "add_player" : "add_mob", spawn);
        string? nameAtSpawn = null;
        EntityPose? poseAtSpawn = null;
        int spawned = 0;
        using IDisposable subscription = harness.Events.Subscribe<EntitySpawned>(evt =>
        {
            nameAtSpawn = evt.Entity.CustomName?.ToPlainText();
            poseAtSpawn = evt.Entity.Pose;
            spawned++;
        });

        await harness.ApplyAsync(decoded);

        Entity entity = Assert.IsType<Entity>(harness.State.Entities.Get(42));
        Assert.True(entity.Metadata.TryGet(0, out MetadataValue flags));
        Assert.Equal(0x20, flags.AsByte());
        Assert.Equal("Initial", entity.CustomName?.ToPlainText());
        Assert.Equal("Initial", nameAtSpawn);
        Assert.Equal(1, spawned);
        if (protocol >= 477)
        {
            Assert.Equal(EntityPose.Dying, entity.Pose);
            Assert.Equal(EntityPose.Dying, poseAtSpawn);
        }

        await harness.ApplyAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(0, MetadataValue.Byte(0x08))], [])));

        Assert.True(entity.Metadata.TryGet(0, out flags));
        Assert.Equal(0x08, flags.AsByte());
        Assert.Equal("Initial", entity.CustomName?.ToPlainText());
    }
}
