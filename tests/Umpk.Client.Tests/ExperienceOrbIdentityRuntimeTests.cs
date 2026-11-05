using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ExperienceOrbIdentityRuntimeTests
{
    private const int OrbId = 4242;
    private static readonly Vec3d SpawnPosition = new(133.5, 65, -37.75);

    public static TheoryData<int, string> DedicatedProtocols => new()
    {
        { 47, "experience_orb" },
        { 107, "xp_orb" },
        { 108, "xp_orb" },
        { 109, "xp_orb" },
        { 110, "xp_orb" },
        { 210, "xp_orb" },
        { 315, "xp_orb" },
        { 316, "xp_orb" },
        { 335, "xp_orb" },
        { 338, "xp_orb" },
        { 340, "xp_orb" },
        { 393, "experience_orb" },
        { 401, "experience_orb" },
        { 404, "experience_orb" },
        { 477, "experience_orb" },
        { 480, "experience_orb" },
        { 485, "experience_orb" },
        { 490, "experience_orb" },
        { 498, "experience_orb" },
        { 573, "experience_orb" },
        { 575, "experience_orb" },
        { 578, "experience_orb" },
        { 735, "experience_orb" },
        { 736, "experience_orb" },
        { 751, "experience_orb" },
        { 753, "experience_orb" },
        { 754, "experience_orb" },
        { 755, "experience_orb" },
        { 756, "experience_orb" },
        { 757, "experience_orb" },
        { 758, "experience_orb" },
        { 759, "experience_orb" },
        { 760, "experience_orb" },
        { 761, "experience_orb" },
        { 762, "experience_orb" },
        { 763, "experience_orb" },
        { 764, "experience_orb" },
        { 765, "experience_orb" },
        { 766, "experience_orb" },
        { 767, "experience_orb" },
        { 768, "experience_orb" },
        { 769, "experience_orb" },
    };

    [Theory]
    [MemberData(nameof(DedicatedProtocols))]
    public async Task DedicatedPacketResolvesTheExactCanonicalOrbBeforeSpawnPublication(int protocol, string canonical)
    {
        ApplierHarness harness = Harness(protocol);
        Identifier? published = null;
        harness.Events.Subscribe<EntitySpawned>(spawn => published = spawn.Entity.Type.Id);
        var packet = new ClientboundAddExperienceOrbPacket(OrbId, SpawnPosition.X, SpawnPosition.Y, SpawnPosition.Z, 1234);

        byte[] expectedWire = Convert.FromHexString(protocol == 47
            ? "9221000010b000000820fffffb4804d2"
            : "92214060b000000000004050400000000000c042e0000000000004d2");
        var decoded = Assert.IsType<ClientboundAddExperienceOrbPacket>(RoundTrip(protocol, "add_experience_orb", packet, expectedWire));
        Assert.Equal(packet, decoded);
        await harness.ApplyAsync(decoded);

        AssertCanonicalIdentity(harness, canonical, published, Guid.Empty);
    }

    [Theory]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task ModernGenericPacketKeepsItsCanonicalOrbTypeAndHasNoDedicatedBinding(int protocol)
    {
        ApplierHarness harness = Harness(protocol);
        Assert.True(harness.State.Registries!.EntityTypes.TryGet(Identifier.Minecraft("experience_orb"), out var type));
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        Assert.True(version!.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        Assert.DoesNotContain(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft("add_experience_orb"));
        var uuid = Guid.Parse("a947cb04-b7e5-4dcb-971f-0a355198c543");
        Identifier? published = null;
        harness.Events.Subscribe<EntitySpawned>(spawn => published = spawn.Entity.Type.Id);
        var packet = new ClientboundAddEntityPacket(OrbId, uuid, type.NetworkId,
            SpawnPosition.X, SpawnPosition.Y, SpawnPosition.Z, 0, 0, 0, 0, 0, 0, 0, protocol >= 773 ? [0] : null);

        var decoded = Assert.IsType<ClientboundAddEntityPacket>(RoundTrip(protocol, "add_entity", packet));
        Assert.Equal(OrbId, decoded.EntityId);
        Assert.Equal(uuid, decoded.Uuid);
        Assert.Equal(type.NetworkId, decoded.TypeId);
        Assert.Equal(SpawnPosition, new Vec3d(decoded.X, decoded.Y, decoded.Z));
        await harness.ApplyAsync(decoded);

        AssertCanonicalIdentity(harness, "experience_orb", published, uuid);
    }

    [Fact]
    public void DirectCanonicalEntryWinsOverTheLegacyAlias()
    {
        RegistryAccess registries = Registries(
            (2, "minecraft:xp_orb"), (67, "minecraft:experience_orb"));

        var resolved = new EntityTypeResolver().ResolveByKey(registries, Identifier.Minecraft("experience_orb"));

        Assert.Equal(Identifier.Minecraft("experience_orb"), resolved.Id);
        Assert.Equal(67, resolved.NetworkId);
        Assert.Same(registries.EntityTypes[67].Value, resolved.Value);
    }

    [Fact]
    public void FallbackUsesTheActualCanonicalEntryWithoutInventingANumericType()
    {
        RegistryAccess registries = Registries(
            (2, "minecraft:zombie"), (67, "minecraft:xp_orb"));

        var resolved = new EntityTypeResolver().ResolveByKey(registries, Identifier.Minecraft("experience_orb"));

        Assert.Equal(Identifier.Minecraft("xp_orb"), resolved.Id);
        Assert.Equal(67, resolved.NetworkId);
        Assert.Same(registries.EntityTypes[67].Value, resolved.Value);
    }

    [Fact]
    public void MissingRegistryRetainsUnknown()
        => Assert.Equal(Identifier.Minecraft("unknown"), new EntityTypeResolver().ResolveByKey(null, Identifier.Minecraft("experience_orb")).Id);

    [Theory]
    [InlineData("example:experience_orb")]
    [InlineData("minecraft:experience_orb_extra")]
    [InlineData("minecraft:player")]
    public void UnrelatedMissingKeysCannotUseTheXpAlias(string requested)
    {
        RegistryAccess registries = Registries((2, "minecraft:xp_orb"));

        var resolved = new EntityTypeResolver().ResolveByKey(registries, Identifier.Parse(requested));

        Assert.Equal(Identifier.Minecraft("unknown"), resolved.Id);
    }

    [Fact]
    public void MissingXpKeysCannotUseNumericTwo()
    {
        RegistryAccess registries = Registries((2, "minecraft:zombie"));

        var resolved = new EntityTypeResolver().ResolveByKey(registries, Identifier.Minecraft("experience_orb"));

        Assert.Equal(Identifier.Minecraft("unknown"), resolved.Id);
    }

    [Fact]
    public void AliasIsNotAppliedInReverse()
    {
        RegistryAccess registries = Registries((67, "minecraft:experience_orb"));

        var resolved = new EntityTypeResolver().ResolveByKey(registries, Identifier.Minecraft("xp_orb"));

        Assert.Equal(Identifier.Minecraft("unknown"), resolved.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceProvenOrbNonparticipationAllowsPlacementWithoutOwnedMotion(bool overlapsPlacement)
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        await session.Block(BlockUseSession.Target.Above(), "iron_door", upper: true);
        await session.Block(BlockUseSession.Target.Offset(Direction.West), "air");
        await session.Hold("stone");
        Vec3d position = overlapsPlacement
            ? new Vec3d(.5, 65.125, 1.5)
            : new Vec3d(14.5, 65.125, 14.5);
        var packet = new ClientboundAddExperienceOrbPacket(OrbId, position.X, position.Y, position.Z, 7);
        Assert.Equal(packet, Assert.IsType<ClientboundAddExperienceOrbPacket>(RoundTrip(315, "add_experience_orb", packet)));
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity orb = Assert.Single(client.State.Entities.All);
            Assert.Equal(Identifier.Minecraft("xp_orb"), orb.Type.Id);
            Assert.Equal(position, orb.Position);
            var source = JavaGameData.EntityGeometryData(315);
            Assert.False(source.TryGetRule(orb.Type.Id, out _));
            Assert.False(client.State.EntityGeometry.TryGet(orb, source, out _, out _, 315));
            Assert.True(JavaGameData.BlockInteractionData(315).IsAlwaysNonObstructingEntity(orb.Type.Id));
            return true;
        }, session.Token);

        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(BlockUseSession.Target, Direction.West, new Vec3d(0, .5, .5), Hand.Main, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> sent = await session.Barrier();
        await action.WaitAsync(session.Token);

        Assert.Collection(sent,
            packet =>
            {
                var use = Assert.IsType<ServerboundUseItemOnPacket>(packet);
                Assert.Equal(BlockUseSession.Target, use.Position);
                Assert.Equal((int)Direction.West, use.Face);
                Assert.Equal((int)Hand.Main, use.Hand);
                Assert.Equal(0F, use.CursorX);
                Assert.Equal(.5F, use.CursorY);
                Assert.Equal(.5F, use.CursorZ);
            },
            packet => Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packet).Hand));
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Offset(Direction.West)).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    private static ApplierHarness Harness(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Entities = true });
        harness.State.Registries = JavaGameData.Registries(protocol);
        return harness;
    }

    private static void AssertCanonicalIdentity(ApplierHarness harness, string canonical, Identifier? published, Guid uuid)
    {
        Entity entity = Assert.Single(harness.State.Entities.All);
        Identifier expected = Identifier.Minecraft(canonical);
        Assert.Equal(expected, entity.Type.Id);
        Assert.Equal<Identifier?>(expected, published);
        Assert.Equal(OrbId, entity.Id);
        Assert.Equal(uuid, entity.Uuid);
        Assert.Equal(SpawnPosition, entity.Position);
        Assert.True(harness.State.Registries!.EntityTypes.TryGet(expected, out var native));
        Assert.Equal(native.NetworkId, entity.Type.NetworkId);
        Assert.Same(native.Value, entity.Type.Value);
    }

    private static RegistryAccess Registries(params (int Id, string Key)[] entries)
    {
        var types = new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType);
        foreach ((int id, string key) in entries)
            types.Add(id, Identifier.Parse(key), new EntityTypeDefinition(.5F, .5F));
        return RegistryAccess.FromSnapshot(JavaGameData.Registries(315).Snapshot.With(types.Build()));
    }

    private static object RoundTrip(int protocol, string identifier, IPacket packet, byte[]? expectedWire = null)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, identifier);
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        byte[] original = Encode(codec, packet, context);
        if (expectedWire is not null)
            Assert.Equal(expectedWire, original);
        object decoded = codec.Decode(original, context);
        Assert.Equal(original, Encode(codec, decoded, context));
        return decoded;
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }
}
