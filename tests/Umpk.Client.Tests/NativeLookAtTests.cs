using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Fixtures;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class NativeLookAtTests
{
    [Fact]
    public async Task StandingAimUsesTheNativeEyeOrigin()
    {
        NativePlayerDimension native = NativePlayerDimensions.ForVersion("26.3").Single(row => row.Pose == EntityPose.Standing && row.Position == Vec3d.Zero);
        JavaVersion version = JavaVersions.All.Single(version => version.Version.Protocol == 777);
        var state = new ClientState(new ClientFeatures().Normalized());
        var sink = new RecordingSink();
        await using var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };
        var actions = new MovementActions(sink, services, () => null);
        var target = new Vec3d(0, 1.62, .1);
        float expectedPitch = (float)(-Math.Atan2(target.Y - native.EyeHeight, target.Z) * 180.0 / Math.PI);
        Assert.NotEqual(0.0f, expectedPitch);

        await actions.LookAtAsync(target);

        ServerboundMovePlayerRotPacket packet = Assert.Single(sink.Packets.OfType<ServerboundMovePlayerRotPacket>());
        Assert.Equal(expectedPitch, packet.Pitch);
    }
}
