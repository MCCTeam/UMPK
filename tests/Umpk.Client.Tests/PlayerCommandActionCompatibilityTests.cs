using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The <c>player_command</c> action enum is NOT stable across the supported range, and the sneak / dismount signal moves off it entirely at 1.21.6.</summary>
/// <remarks>
/// <para>The action values in ordinal order are: 1.21.5 declares PRESS_SHIFT_KEY, RELEASE_SHIFT_KEY, STOP_SLEEPING, START_SPRINTING, STOP_SPRINTING, START_RIDING_JUMP, STOP_RIDING_JUMP, OPEN_INVENTORY, START_FALL_FLYING. 1.21.6 removes the first two, and later versions agree. So at protocol 771 every surviving ordinal moved down by two and the shift pair has no ordinal at all. 1.8.4 through 1.13 spell the first two START_SNEAKING / STOP_SNEAKING, which use the same handler under an older name and the same ordinals.</para>
/// <para>Dismount uses the server's shift state. Protocols below 771 update it through <c>player_command</c>, while protocol 771 and later use <c>player_input</c>. Sending the older ordinals on 771 and later can decode stop-sneaking as start-sprinting and cannot request a dismount.</para>
/// </remarks>
public sealed class PlayerCommandActionCompatibilityTests
{
    // Action truth tables for the version bands covered here.

    private static readonly string[] ActionsBelowV1_21_6 =
    [
        "PRESS_SHIFT_KEY", "RELEASE_SHIFT_KEY", "STOP_SLEEPING", "START_SPRINTING", "STOP_SPRINTING",
        "START_RIDING_JUMP", "STOP_RIDING_JUMP", "OPEN_INVENTORY", "START_FALL_FLYING",
    ];

    private static readonly string[] ActionsFromV1_21_6 =
    [
        "STOP_SLEEPING", "START_SPRINTING", "STOP_SPRINTING", "START_RIDING_JUMP", "STOP_RIDING_JUMP",
        "OPEN_INVENTORY", "START_FALL_FLYING",
    ];

    /// <summary>The first protocol whose action enum has no shift entries (1.21.6).</summary>
    private const int NoShiftPlayerCommandMin = 771;

    /// <summary>Every protocol this surface has to be right on, one per wire form in the range.</summary>
    public static TheoryData<int> EveryEra =>
        [47, 107, 210, 340, 393, 477, 754, 758, 762, 765, 767, 768, 770, 771, 772, 773, 774, 775, 776];

    private static string[] VanillaActions(int protocol) =>
        protocol >= NoShiftPlayerCommandMin ? ActionsFromV1_21_6 : ActionsBelowV1_21_6;

    /// <summary>The name a vanilla server of this protocol resolves an action ordinal to. An out-of-range ordinal is not a silent no-op there: <c>handlePlayerCommand</c> ends in <c>default: throw new IllegalArgumentException("Invalid client command!")</c>, so the test treats it as a failure rather than as "no effect".</summary>
    private static string VanillaActionName(int protocol, int ordinal)
    {
        string[] table = VanillaActions(protocol);
        Assert.InRange(ordinal, 0, table.Length - 1);
        return table[ordinal];
    }

    // A model of the two vanilla server handlers that write the player's shift / sprint flags. The assertions below are on the RESULTING SERVER STATE, not on "a packet was written": an action that encodes cleanly and lands on the wrong enum member writes nothing, and only this can tell.

    private static bool VanillaShiftKeyDown(int protocol, IEnumerable<object> sent)
    {
        bool shift = false;
        foreach (object packet in sent)
        {
            switch (packet)
            {
                // player command behavior
                case ServerboundPlayerCommandPacket command:
                    switch (VanillaActionName(protocol, command.Action))
                    {
                        case "PRESS_SHIFT_KEY": shift = true; break;
                        case "RELEASE_SHIFT_KEY": shift = false; break;
                        default: break;
                    }

                    break;

                // player input behavior, which only writes the flag from 1.21.6.
                case ServerboundPlayerInputPacket input when protocol >= NoShiftPlayerCommandMin:
                    shift = input.Shift;
                    break;

                default:
                    break;
            }
        }

        return shift;
    }

    private static bool VanillaSprinting(int protocol, IEnumerable<object> sent)
    {
        bool sprinting = false;
        foreach (object packet in sent)
            if (packet is ServerboundPlayerCommandPacket command)
                switch (VanillaActionName(protocol, command.Action))
                {
                    case "START_SPRINTING": sprinting = true; break;
                    case "STOP_SPRINTING": sprinting = false; break;
                    default: break;
                }

        return sprinting;
    }

    private static IReadOnlyList<string> VanillaCommandNames(int protocol, IEnumerable<object> sent) =>
        [.. sent.OfType<ServerboundPlayerCommandPacket>().Select(c => VanillaActionName(protocol, c.Action))];

    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    private static MovementActions Actions(RecordingSink recorder, int protocol, int entityId = 77)
    {
        JavaVersion version = Version(protocol);
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.EntityId = entityId;
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        return new MovementActions(recorder, services, () => null);
    }

    /// <summary>After the client asks to sneak, the server's shift flag is true; after it asks to stop, the flag is false. This is also the value used by the dismount predicate.</summary>
    [Theory]
    [MemberData(nameof(EveryEra))]
    public async Task Sneaking_Writes_TheServersShiftFlag(int protocol)
    {
        var recorder = new RecordingSink();
        MovementActions actions = Actions(recorder, protocol);

        await actions.SetSneakingAsync(true);
        Assert.True(
            VanillaShiftKeyDown(protocol, recorder.Packets),
            $"protocol {protocol}: nothing the client sent set the server's shift key");

        await actions.SetSneakingAsync(false);
        Assert.False(VanillaShiftKeyDown(protocol, recorder.Packets));
    }

    /// <summary>A rider leaves its vehicle on the first server tick after the shift flag becomes true, on every era.</summary>
    [Theory]
    [MemberData(nameof(EveryEra))]
    public async Task ASingleSneak_Satisfies_TheVanillaDismountPredicate(int protocol)
    {
        var recorder = new RecordingSink();

        await Actions(recorder, protocol).SetSneakingAsync(true);

        bool wantsToStopRiding = VanillaShiftKeyDown(protocol, recorder.Packets);
        Assert.True(wantsToStopRiding, $"protocol {protocol}: a rider would never leave the vehicle");
    }

    /// <summary>Releasing sneak must not start the player SPRINTING. On 1.21.6+ the old release ordinal is START_SPRINTING, so using it for "stop sneaking" produces the opposite action.</summary>
    [Theory]
    [MemberData(nameof(EveryEra))]
    public async Task Sneaking_Never_TouchesTheSprintFlag(int protocol)
    {
        var recorder = new RecordingSink();
        MovementActions actions = Actions(recorder, protocol);

        await actions.SetSneakingAsync(true);
        await actions.SetSneakingAsync(false);

        Assert.False(VanillaSprinting(protocol, recorder.Packets));
        Assert.DoesNotContain("START_SPRINTING", VanillaCommandNames(protocol, recorder.Packets));
        Assert.DoesNotContain("STOP_SLEEPING", VanillaCommandNames(protocol, recorder.Packets));
    }

    /// <summary>The channel split, stated directly: below 1.21.6 the shift key rides <c>player_command</c>, from 1.21.6 it rides <c>player_input</c> and no <c>player_command</c> is sent for it at all.</summary>
    [Theory]
    [InlineData(47, false)]
    [InlineData(340, false)]
    [InlineData(762, false)]
    [InlineData(768, false)]
    [InlineData(770, false)]
    [InlineData(771, true)]
    [InlineData(772, true)]
    [InlineData(774, true)]
    [InlineData(776, true)]
    public async Task Sneak_Uses_TheChannelTheWireLayoutReads(int protocol, bool viaPlayerInput)
    {
        var recorder = new RecordingSink();

        await Actions(recorder, protocol).SetSneakingAsync(true);

        object sent = Assert.Single(recorder.Packets);
        if (viaPlayerInput)
        {
            var input = Assert.IsType<ServerboundPlayerInputPacket>(sent);
            Assert.True(input.Shift);
            Assert.False(input.Sprint);
        }
        else
        {
            var command = Assert.IsType<ServerboundPlayerCommandPacket>(sent);
            Assert.Equal("PRESS_SHIFT_KEY", VanillaActionName(protocol, command.Action));
        }
    }

    /// <summary>Sprint stays on <c>player_command</c> everywhere, but its ordinal moves at 1.21.6.</summary>
    [Theory]
    [MemberData(nameof(EveryEra))]
    public async Task Sprint_Writes_TheServersSprintFlag(int protocol)
    {
        var recorder = new RecordingSink();
        MovementActions actions = Actions(recorder, protocol);

        await actions.SetSprintingAsync(true);
        Assert.True(VanillaSprinting(protocol, recorder.Packets));
        Assert.Contains("START_SPRINTING", VanillaCommandNames(protocol, recorder.Packets));

        await actions.SetSprintingAsync(false);
        Assert.False(VanillaSprinting(protocol, recorder.Packets));
        Assert.Contains("STOP_SPRINTING", VanillaCommandNames(protocol, recorder.Packets));

        // Sprinting must never be mistaken for a vehicle jump, which is what ordinals 3 and 4 mean from 1.21.6 on.
        Assert.DoesNotContain("START_RIDING_JUMP", VanillaCommandNames(protocol, recorder.Packets));
        Assert.DoesNotContain("STOP_RIDING_JUMP", VanillaCommandNames(protocol, recorder.Packets));
    }

    /// <summary>Leaving a bed must resolve to STOP_SLEEPING on both sides of the 1.21.6 reshuffle.</summary>
    [Theory]
    [MemberData(nameof(EveryEra))]
    public async Task LeaveBed_Resolves_ToStopSleeping(int protocol)
    {
        var recorder = new RecordingSink();

        await Actions(recorder, protocol).LeaveBedAsync();

        var sent = Assert.IsType<ServerboundPlayerCommandPacket>(Assert.Single(recorder.Packets));
        Assert.Equal(77, sent.EntityId);
        Assert.Equal(0, sent.Data);
        Assert.Equal("STOP_SLEEPING", VanillaActionName(protocol, sent.Action));
    }

    /// <summary>The bytes placed on the wire from 1.21.6, encoded through the bound descriptor rather than through a codec the test picked. <c>player_input</c> being registered is not enough: a marker has a real wire id and throws here, and <c>ServerboundPlay(id) &gt;= 0</c> would pass for one. The body is one flags byte with FLAG_SHIFT = 32.</summary>
    [Theory]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(774)]
    [InlineData(776)]
    public void TheShiftFrame_Encodes_ThroughTheShippedTable(int protocol)
    {
        JavaVersion version = Version(protocol);
        Assert.True(new WireIndex(version).CanSendPlay(EntityPackets.Serverbound.PlayerInput));

        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        Assert.True(
            registry.TryGetOutbound(EntityPackets.Serverbound.PlayerInput, out _, out BoundPacketCodec bound),
            $"player_input has no outbound binding at protocol {protocol}");

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(
            ref writer,
            new ServerboundPlayerInputPacket(false, false, false, false, false, Shift: true, Sprint: false),
            new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty));

        Assert.Equal<byte[]>([0x20], buffer.WrittenSpan.ToArray());
    }

    /// <summary>Cross-era rejection: below 1.21.2 there is no flags-only <c>player_input</c> record at all. 107-767 do register the identifier, but bound to the 1.8 steer-vehicle record (strafe float, forward float, flags byte), so the flags-only record has no outbound entry and a sneak routed to it there would be unsendable. This is the OUTBOUND-TABLE question, not an identifier lookup, which is what makes it able to tell a marker and a wrong-record binding apart.</summary>
    [Theory]
    [InlineData(47, false)]
    [InlineData(340, false)]
    [InlineData(762, false)]
    [InlineData(767, false)]
    [InlineData(768, true)]
    [InlineData(770, true)]
    [InlineData(771, true)]
    [InlineData(776, true)]
    public void TheFlagsOnlyInputRecord_Exists_OnlyFromTheInputRework(int protocol, bool sendable)
    {
        JavaVersion version = Version(protocol);
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);

        Assert.Equal(sendable, new WireIndex(version).CanSendPlay(EntityPackets.Serverbound.PlayerInput));
        Assert.Equal(sendable, registry.TryGetOutbound(EntityPackets.Serverbound.PlayerInput, out _, out _));
    }
}
