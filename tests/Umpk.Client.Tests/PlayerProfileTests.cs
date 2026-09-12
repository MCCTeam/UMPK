using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Player entities must carry their profile, so a consumer can resolve a player entity by name. <c>Entity.PlayerProfile</c> was declared and never assigned, which forced consumers to go name -> tab-list uuid -> entity by hand.</summary>
/// <remarks>A spawn packet carries the uuid but never the name, so the name has to come from the tab list, and the two packets arrive in either order. Both orders are covered per era here, and the spawn frame is round-tripped through the codec the catalog binds for that protocol: on 1.14-1.20.1 <c>add_player</c> was a marker, so the spawn frame was discarded and no player entity existed at all on those bands, which no amount of applier-level testing would have surfaced.</remarks>
public sealed class PlayerProfileTests
{
    // 47 and 340 are the pre-1.14 add_player codecs that already existed; 477 is the 1.14 form with metadata; 578, 755 and 762 are the 1.15+ form without it; all of 477-763 were markers before this change. 772 has no add_player at all: players arrive through add_entity there.
    public static TheoryData<int> LegacySpawnProtocols => [47, 340, 477, 578, 755, 762];

    public static TheoryData<int> ModernSpawnProtocols => [764, 772, 776];

    [Theory]
    [MemberData(nameof(LegacySpawnProtocols))]
    public async Task TabList_Then_Spawn_Populates_Profile(int protocol)
    {
        ApplierHarness harness = Harness(protocol);
        Guid uuid = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await harness.ApplyAsync(TabListAdd(protocol, uuid, "Notch"));
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            protocol, "add_player", new ClientboundAddPlayerPacket(
                EntityId: 55, Uuid: uuid, X: 12.5, Y: 64, Z: -8.5, Yaw: 0, Pitch: 0, CurrentItem: 0,
                Metadata: new EntityMetadataList([], []))));

        AssertProfile(harness, entityId: 55, uuid, "Notch");
    }

    /// <summary>The ordering case: the spawn beats its own tab-list entry onto the wire. The entity exists with no resolvable name until the entry lands, and must pick the profile up when it does.</summary>
    [Theory]
    [MemberData(nameof(LegacySpawnProtocols))]
    public async Task Spawn_Then_TabList_Backfills_Profile(int protocol)
    {
        ApplierHarness harness = Harness(protocol);
        Guid uuid = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            protocol, "add_player", new ClientboundAddPlayerPacket(
                EntityId: 56, Uuid: uuid, X: 1, Y: 65, Z: 2, Yaw: 0, Pitch: 0, CurrentItem: 0,
                Metadata: new EntityMetadataList([], []))));

        Assert.True(harness.State.Entities.TryGet(56, out Game.Entities.Entity? spawned));
        Assert.Null(spawned.PlayerProfile);

        await harness.ApplyAsync(TabListAdd(protocol, uuid, "Herobrine"));

        AssertProfile(harness, entityId: 56, uuid, "Herobrine");
    }

    /// <summary>From 1.20.2 add_player is gone from the protocol and players spawn through add_entity with the player entity type, so the profile hook has to live on the generic spawn path too.</summary>
    [Theory]
    [MemberData(nameof(ModernSpawnProtocols))]
    public async Task Modern_AddEntity_Player_Spawn_Populates_Profile(int protocol)
    {
        ApplierHarness harness = Harness(protocol);
        Guid uuid = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

        await harness.ApplyAsync(TabListAdd(protocol, uuid, "Steve"));
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            protocol, "add_entity", new ClientboundAddEntityPacket(
                EntityId: 57, Uuid: uuid, TypeId: PlayerTypeId(harness), X: 3.5, Y: 70, Z: 9.5,
                XRot: 0, YRot: 0, YHeadRot: 0, Data: 0, VelocityX: 0, VelocityY: 0, VelocityZ: 0,
                ModernVelocityRaw: null)));

        AssertProfile(harness, entityId: 57, uuid, "Steve");
    }

    /// <summary>A non-player spawn on the same generic path must not be given a profile.</summary>
    [Fact]
    public async Task Modern_AddEntity_NonPlayer_Leaves_Profile_Null()
    {
        ApplierHarness harness = Harness(772);
        Guid uuid = Guid.NewGuid();

        await harness.ApplyAsync(new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.AddPlayer,
            [Entry(uuid, "NotAnEntity")]));

        // A cow, not a player, sharing the uuid that happens to be in the tab list.
        int cow = CowTypeId(harness);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            772, "add_entity", new ClientboundAddEntityPacket(
                EntityId: 58, Uuid: uuid, TypeId: cow, X: 0, Y: 64, Z: 0,
                XRot: 0, YRot: 0, YHeadRot: 0, Data: 0, VelocityX: 0, VelocityY: 0, VelocityZ: 0,
                ModernVelocityRaw: null)));

        Assert.True(harness.State.Entities.TryGet(58, out Game.Entities.Entity? entity));
        Assert.Null(entity.PlayerProfile);
    }

    /// <summary>The registration guard for the 22 bands where add_player was registered but never implemented. Resolution through the catalog requires every covered band to use a concrete codec.</summary>
    [Theory]
    [InlineData(477)]
    [InlineData(480)]
    [InlineData(485)]
    [InlineData(490)]
    [InlineData(498)]
    [InlineData(573)]
    [InlineData(575)]
    [InlineData(578)]
    [InlineData(735)]
    [InlineData(736)]
    [InlineData(751)]
    [InlineData(753)]
    [InlineData(754)]
    [InlineData(755)]
    [InlineData(756)]
    [InlineData(757)]
    [InlineData(758)]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(763)]
    public void AddPlayer_Is_Bound_On_Every_Band_That_Carries_It(int protocol)
    {
        Guid uuid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var decoded = (ClientboundAddPlayerPacket)BoundDescriptorCodec.RoundTrip(
            protocol, "add_player", new ClientboundAddPlayerPacket(
                EntityId: 4242, Uuid: uuid, X: 100.25, Y: 65, Z: -200.75, Yaw: 90, Pitch: 45, CurrentItem: 0,
                Metadata: new EntityMetadataList([], [])));

        Assert.Equal(4242, decoded.EntityId);
        Assert.Equal(uuid, decoded.Uuid);
        Assert.Equal(100.25, decoded.X);
        Assert.Equal(65, decoded.Y);
        Assert.Equal(-200.75, decoded.Z);
        // Yaw and pitch ride the wire as angle bytes, so they come back quantized to 360/256 steps.
        Assert.Equal(90, decoded.Yaw, 0);
        Assert.Equal(45, decoded.Pitch, 0);
    }

    private static void AssertProfile(ApplierHarness harness, int entityId, Guid uuid, string name)
    {
        Assert.True(harness.State.Entities.TryGet(entityId, out Game.Entities.Entity? entity));
        Assert.NotNull(entity.PlayerProfile);
        Assert.Equal(name, entity.PlayerProfile!.Name);
        Assert.Equal(uuid, entity.PlayerProfile.Id);

        // The point of the property: resolve a player entity by name without walking the tab list.
        Game.Entities.Entity? byName = harness.State.Entities.All
            .FirstOrDefault(e => string.Equals(e.PlayerProfile?.Name, name, StringComparison.Ordinal));
        Assert.NotNull(byName);
        Assert.Equal(entityId, byName!.Id);
    }

    private static ApplierHarness Harness(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Entities = true });
        harness.State.Registries = JavaGameData.Registries(protocol);
        return harness;
    }

    private static int PlayerTypeId(ApplierHarness harness) => TypeId(harness, "player");

    private static int CowTypeId(ApplierHarness harness) => TypeId(harness, "cow");

    private static int TypeId(ApplierHarness harness, string key)
    {
        Assert.NotNull(harness.State.Registries);
        Assert.True(
            harness.State.Registries!.EntityTypes.TryGet(
                Identifier.Minecraft(key), out RegistryEntry<EntityTypeDefinition> entry),
            $"minecraft:{key} missing from the entity-type registry");
        return entry.NetworkId;
    }

    /// <summary>The tab-list add for the era: 1.19.3 split the single legacy player_info packet into player_info_update plus player_info_remove, so the profile has to land through whichever the era actually sends.</summary>
    private static object TabListAdd(int protocol, Guid uuid, string name) =>
        protocol >= 761
            ? new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.AddPlayer, [Entry(uuid, name)])
            : new ClientboundLegacyPlayerListItemPacket(
                LegacyPlayerListAction.AddPlayer,
                [new LegacyPlayerListEntry(uuid, name, null, GameMode: 0, Latency: 12, DisplayName: null)]);

    private static PlayerInfoEntry Entry(Guid uuid, string name) =>
        new(uuid, name, null, HasChatSession: false, ChatSession: null, GameMode.Survival, Listed: true,
            Latency: 12, DisplayName: null, ListOrder: 0, ShowHat: true);
}
