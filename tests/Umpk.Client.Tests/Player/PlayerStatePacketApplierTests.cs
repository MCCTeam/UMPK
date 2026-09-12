using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.PacketApplication.Player;

/// <summary>Player state changes delivered through version-bound packet codecs.</summary>
public sealed class PlayerStatePacketApplierTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task PlayerAbilities_SetEveryFlightFlagAndSpeed(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        AbilitiesChanged? seen = null;
        harness.Events.Subscribe<AbilitiesChanged>(value => seen = value);

        await BoundPacketApplierHarness.RoundTripAndApplyAsync(
            harness,
            protocol,
            "player_abilities",
            new ClientboundPlayerAbilitiesPacket(0x0F, 0.075f, 0.13f));

        Assert.NotNull(seen);
        Assert.True(harness.State.Self.Flying);
        Assert.True(harness.State.Self.MayFly);
        Assert.True(harness.State.Self.Invulnerable);
        Assert.True(harness.State.Self.InstantBuild);
        Assert.Equal(0.075f, harness.State.Self.FlyingSpeed);
        Assert.Equal(0.13f, harness.State.Self.WalkingSpeed);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task SetCamera_UpdatesCameraEntity(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        await BoundPacketApplierHarness.RoundTripAndApplyAsync(
            harness,
            protocol,
            "set_camera",
            new ClientboundSetCameraPacket(9001));
        Assert.Equal(9001, harness.State.Self.CameraEntityId);
    }
}
