using System.Buffers;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyExperienceOrbPickingSessionTests
{
    private static readonly BlockPos Target = new(2, 66, 5);
    private const int OrbId = 4242;

    public static IEnumerable<object[]> Nonparticipants()
    {
        foreach (int protocol in new[] { 47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393 })
            foreach (bool far in new[] { false, true })
                foreach (bool held in new[] { false, true })
                    yield return [protocol, far, held];
    }

    [Theory]
    [MemberData(nameof(Nonparticipants))]
    public async Task NativeXpNonparticipantsLeaveInitialAndHeldViewMiningActive(int protocol, bool far, bool held)
    {
        await using BlockUseSession session = await Prepare(protocol);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task? mining = null;
        try
        {
            if (held)
            {
                mining = session.Client.Actions.Interaction.DigBlockInViewAsync(Target, ct: caller.Token);
                await session.Client.InvokeAsync(_ => true, session.Token);
                AssertStarted(await Tick(session), protocol);
            }
            await Spawn(session, far, unknown: false);
            mining ??= session.Client.Actions.Interaction.DigBlockInViewAsync(Target, ct: caller.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> packets = await Tick(session);
            if (held)
                AssertContinued(packets, protocol);
            else
                AssertStarted(packets, protocol);
            AssertContinued(await Tick(session), protocol);
            Assert.False(mining.IsCompleted);
            Assert.Equal(Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
            Assert.False(session.Client.State.World.GetBlock(Target).IsAir);
        }
        finally
        {
            caller.Cancel();
            if (mining is not null)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    public static IEnumerable<object[]> UnknownControls()
    {
        foreach (int protocol in new[] { 107, 340 })
            foreach (bool held in new[] { false, true })
                yield return [protocol, held];
    }

    [Theory]
    [MemberData(nameof(UnknownControls))]
    public async Task MissingCanonicalIdentityStillEndsViewMiningWithoutInventingAnAttack(int protocol, bool held)
    {
        await using BlockUseSession session = await Prepare(protocol);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task? mining = null;
        try
        {
            if (held)
            {
                mining = session.Client.Actions.Interaction.DigBlockInViewAsync(Target, ct: caller.Token);
                await session.Client.InvokeAsync(_ => true, session.Token);
                AssertStarted(await Tick(session), protocol);
            }
            await Spawn(session, far: false, unknown: true);
            mining ??= session.Client.Actions.Interaction.DigBlockInViewAsync(Target, ct: caller.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> packets = await Tick(session);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
            if (held)
            {
                var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(packets));
                Assert.Equal(1, abort.Action);
                Assert.Equal(Target, abort.Position);
                Assert.Equal((byte)Direction.Down, abort.Direction);
                Assert.Null(abort.Sequence);
            }
            else
                Assert.Empty(packets);
            Assert.Empty(await Tick(session));
            Assert.Equal(Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
        }
        finally
        {
            caller.Cancel();
            if (mining is not null && !mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    [Theory]
    [InlineData(107, false)]
    [InlineData(107, true)]
    [InlineData(340, false)]
    [InlineData(340, true)]
    public async Task ExplicitCoordinateMiningKeepsItsContractWithKnownOrUnknownXpIdentity(int protocol, bool unknown)
    {
        await using BlockUseSession session = await Prepare(protocol);
        await Spawn(session, far: false, unknown);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Client.Actions.Interaction.DigBlockAsync(Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            AssertStarted(await Tick(session), protocol);
            AssertContinued(await Tick(session), protocol);
            Assert.False(mining.IsCompleted);
            Assert.False(session.Client.State.World.GetBlock(Target).IsAir);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    private static async Task<BlockUseSession> Prepare(int protocol)
    {
        BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        try
        {
            await session.Block(Target, "stone");
            await session.Tick();
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.Self.OnGround);
            Assert.Equal(new Vec3d(2.5, 65, 2.5), session.Client.State.Self.Position);
            Assert.True(session.Client.State.EntityMembership.HasColumn(Target));
            Assert.Equal(Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task Spawn(BlockUseSession session, bool far, bool unknown)
    {
        if (unknown)
        {
            await session.Client.InvokeAsync(client =>
            {
                var types = new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType);
                types.Add(2, Identifier.Minecraft("zombie"), new EntityTypeDefinition(.6F, 1.8F));
                client.State.Registries = RegistryAccess.FromSnapshot(client.State.Registries!.Snapshot.With(types.Build()));
                return true;
            }, session.Token);
        }
        Vec3d position = far ? new(14.5, 65.125, 14.5) : new(2.5, 66.5, 4);
        var packet = new ClientboundAddExperienceOrbPacket(OrbId, position.X, position.Y, position.Z, 7);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, "add_experience_orb");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<ClientboundAddExperienceOrbPacket>(codec.Decode(bytes, context));
        Assert.Equal(packet, decoded);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity orb = Assert.Single(client.State.Entities.All);
            string canonical = unknown ? "unknown" : session.Protocol is >= 107 and <= 340 ? "xp_orb" : "experience_orb";
            Assert.Equal(Identifier.Minecraft(canonical), orb.Type.Id);
            Assert.Equal(position, orb.Position);
            if (!far)
            {
                Assert.Equal(2.5, client.SelfEyePosition.X);
                Assert.InRange(client.SelfEyePosition.Y, 66.5, 67);
                Assert.True(client.SelfEyePosition.Z < position.Z - .25);
            }
            var source = JavaGameData.EntityGeometryData(session.Protocol);
            Assert.False(source.TryGetRule(orb.Type.Id, out _));
            Assert.False(client.State.EntityGeometry.TryGet(orb, source, out _, out _, session.Protocol));
            return true;
        }, session.Token);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }

    private static async Task<List<IPacket>> Tick(BlockUseSession session)
    {
        int beforePackets = session.ObservedOutbound.Count;
        long beforeTick = session.Client.State.SessionTick;
        await session.Tick();
        await session.Barrier();
        Assert.Equal(beforeTick + 1, session.Client.State.SessionTick);
        return session.ObservedOutbound.Skip(beforePackets).Where(packet => packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundInteractPacket).ToList();
    }

    private static void AssertStarted(IReadOnlyList<IPacket> packets, int protocol)
    {
        Assert.Equal(3, packets.Count);
        int startIndex = protocol == 47 ? 1 : 0;
        var start = Assert.IsType<ServerboundPlayerActionPacket>(packets[startIndex]);
        Assert.Equal(0, start.Action);
        Assert.Equal(Target, start.Position);
        Assert.Equal((byte)Direction.North, start.Direction);
        Assert.Null(start.Sequence);
        AssertSwing(packets[startIndex == 0 ? 1 : 0], protocol);
        AssertSwing(packets[2], protocol);
    }

    private static void AssertContinued(IReadOnlyList<IPacket> packets, int protocol)
        => AssertSwing(Assert.Single(packets), protocol);

    private static void AssertSwing(IPacket packet, int protocol)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.Equal(0, swing.Hand);
        Assert.Equal(protocol >= 107, swing.HasHand);
    }
}
