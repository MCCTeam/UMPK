using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Movement;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class MovementTickPipelineTests
{
    [Fact]
    public async Task InputSprintAndPositionDescribeOneTickInWireOrder()
    {
        await using ChannelSessionScheduler scheduler = new();
        ClientSessionServices services = Services(scheduler);
        var reporter = new MovementReporter(services);
        reporter.Reset();
        var sent = new List<object>();
        var input = new MovementInput { Forward = true, Sprint = true };
        var state = new PhysicsState
        {
            Position = new Vec3d(1, 64, 2),
            OnGround = true,
            IsSprinting = true,
        };

        await reporter.ReportAsync(input, state, sendPosition: true, Send);

        Assert.Collection(
            sent,
            packet =>
            {
                var published = Assert.IsType<ServerboundPlayerInputPacket>(packet);
                Assert.True(published.Forward);
                Assert.True(published.Sprint);
            },
            packet => Assert.IsType<ServerboundPlayerCommandPacket>(packet),
            packet => Assert.IsType<ServerboundMovePlayerPosPacket>(packet));
        Assert.True(services.State.Self.Sprinting);

        ValueTask<bool> Send(object packet, string _)
        {
            sent.Add(packet);
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task UnchangedModernTickIsSuppressedAndCollisionChangeUsesCurrentState()
    {
        await using ChannelSessionScheduler scheduler = new();
        ClientSessionServices services = Services(scheduler);
        var reporter = new MovementReporter(services);
        reporter.Reset();
        var sent = new List<object>();
        var baseline = new PhysicsState { OnGround = true };

        await reporter.ReportAsync(MovementInput.None, baseline, sendPosition: true, Send);
        await reporter.ReportAsync(
            MovementInput.None,
            baseline with { HorizontalCollision = true },
            sendPosition: true,
            Send);

        var status = Assert.IsType<ServerboundMovePlayerStatusOnlyPacket>(Assert.Single(sent));
        Assert.True(status.OnGround);
        Assert.True(status.HorizontalCollision);

        ValueTask<bool> Send(object packet, string _)
        {
            sent.Add(packet);
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task FailedSendDoesNotAdvanceSuccessDependentBaselines()
    {
        await using ChannelSessionScheduler scheduler = new();
        ClientSessionServices services = Services(scheduler);
        var reporter = new MovementReporter(services);
        reporter.Reset();
        var attempts = new List<object>();
        var state = new PhysicsState { Position = new Vec3d(4, 70, 8), OnGround = true };

        await reporter.ReportAsync(MovementInput.None, state, sendPosition: true, Failed);
        await reporter.ReportAsync(MovementInput.None, state, sendPosition: true, Succeeded);

        Assert.Equal(2, attempts.OfType<ServerboundMovePlayerPosPacket>().Count());

        ValueTask<bool> Failed(object packet, string _)
        {
            attempts.Add(packet);
            return ValueTask.FromResult(false);
        }

        ValueTask<bool> Succeeded(object packet, string _)
        {
            attempts.Add(packet);
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task StationaryPlayerGetsOnePositionReminderOnTickTwenty()
    {
        await using ChannelSessionScheduler scheduler = new();
        ClientSessionServices services = Services(scheduler);
        var reporter = new MovementReporter(services);
        reporter.Reset();
        var sent = new List<object>();
        var state = new PhysicsState { OnGround = true };

        for (int tick = 0; tick < 20; tick++)
            await reporter.ReportAsync(MovementInput.None, state, sendPosition: true, Send);

        Assert.IsType<ServerboundMovePlayerPosPacket>(Assert.Single(sent));

        ValueTask<bool> Send(object packet, string _)
        {
            sent.Add(packet);
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task LegacyStationaryPlayerPublishesOneMovementPacketPerTickSoTimedItemUseAdvances()
    {
        await using ChannelSessionScheduler scheduler = new();
        ClientSessionServices services = Services(scheduler, JavaVersions.V1_8);
        var reporter = new MovementReporter(services);
        reporter.Reset();
        var sent = new List<object>();
        var state = new PhysicsState { OnGround = true };

        // Vanilla 1.8 calls EntityPlayerMP.l() from its movement-packet handler. A drink/eat use lasts 32 player ticks, so suppressing unchanged movement packets leaves that countdown stuck forever.
        for (int tick = 0; tick < 32; tick++)
            await reporter.ReportAsync(MovementInput.None, state, sendPosition: true, Send);

        Assert.Equal(32, sent.Count);
        Assert.Equal(31, sent.OfType<ServerboundMovePlayerStatusPacket>().Count());
        Assert.Single(sent.OfType<ServerboundMovePlayerPosPacket>());

        ValueTask<bool> Send(object packet, string _)
        {
            sent.Add(packet);
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task OneNineAndLaterSuppressUnchangedTicksUntilMovementStateChanges()
    {
        await using ChannelSessionScheduler scheduler = new();
        ClientSessionServices services = Services(scheduler, JavaVersions.V1_9);
        var reporter = new MovementReporter(services);
        reporter.Reset();
        var sent = new List<object>();
        var state = new PhysicsState { OnGround = true };

        await reporter.ReportAsync(MovementInput.None, state, sendPosition: true, Send);
        await reporter.ReportAsync(MovementInput.None, state with { OnGround = false }, sendPosition: true, Send);

        var status = Assert.IsType<ServerboundMovePlayerStatusPacket>(Assert.Single(sent));
        Assert.False(status.OnGround);

        ValueTask<bool> Send(object packet, string _)
        {
            sent.Add(packet);
            return ValueTask.FromResult(true);
        }
    }

    private static ClientSessionServices Services(ISessionScheduler scheduler, JavaVersion? version = null)
    {
        version ??= ScriptedServer.Version;
        return new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = new ClientState(new ClientFeatures()),
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };
    }
}
