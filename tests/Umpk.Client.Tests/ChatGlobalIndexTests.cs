using System.Buffers;
using Umpk.Client.Events;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Protocols that carry <c>globalIndex</c> track it per connection. A missing chat frame advances the gap count and publishes <see cref="ChatStreamGap"/> instead of terminating the connection.</summary>
public sealed class ChatGlobalIndexTests
{
    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");

    /// <summary>The counter is armed by the JOIN, and only by the join, which is where vanilla resets it (<c>handleLogin</c>: <c>this.nextChatIndex = 0;</c>). This drives the real login packet through the real applier so a wrong arming point cannot pass.</summary>
    [Fact]
    public async Task Join_ArmsTheCounter_OnProtocolsThatCarryTheIndex()
    {
        var harness = new ApplierHarness(Version(770));
        Assert.False(harness.State.Chat.TracksGlobalIndex);

        await JoinAsync(harness);

        Assert.True(harness.State.Chat.TracksGlobalIndex);
        Assert.Equal(0, harness.State.Chat.NextGlobalIndex);
        Assert.Equal(0, harness.State.Chat.ObservedGaps);
    }

    /// <summary>Protocol 769 has no global index on the wire. Tracking its placeholder value would report a gap on the second message of every session.</summary>
    [Fact]
    public async Task Join_DoesNotArmTheCounter_BelowProtocol770()
    {
        var harness = new ApplierHarness(Version(769));
        await JoinAsync(harness);

        Assert.False(harness.State.Chat.TracksGlobalIndex);

        // Three frames, every one of them reporting index 0 because the field is not on the wire.
        for (int i = 0; i < 3; i++)
            await harness.ApplyAsync(DecodePlayerChat(Version(769), BuildFrame(769, globalIndex: 0, content: $"m{i}")));

        Assert.Equal(0, harness.State.Chat.ObservedGaps);
        Assert.Equal(0, harness.State.Chat.NextGlobalIndex);
    }

    /// <summary>An in-order stream advances the counter and reports nothing.</summary>
    [Fact]
    public async Task InOrderStream_AdvancesTheCounter_AndReportsNoGap()
    {
        var harness = new ApplierHarness(Version(770));
        var gaps = new List<ChatStreamGap>();
        harness.Events.Subscribe<ChatStreamGap>(gaps.Add);
        await JoinAsync(harness);

        for (int i = 0; i < 4; i++)
        {
            await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: i, content: $"m{i}")));
            Assert.Equal(i + 1, harness.State.Chat.NextGlobalIndex);
        }

        Assert.Empty(gaps);
        Assert.Equal(0, harness.State.Chat.ObservedGaps);
    }

    /// <summary>When the server skips index 1, the gap is counted and reported once. Messages that do arrive are still delivered.</summary>
    [Fact]
    public async Task MissingMessage_IsReportedOnce_AndTheStreamResynchronises()
    {
        var harness = new ApplierHarness(Version(770));
        var gaps = new List<ChatStreamGap>();
        var received = new List<ChatMessageReceived>();
        harness.Events.Subscribe<ChatStreamGap>(gaps.Add);
        harness.Events.Subscribe<ChatMessageReceived>(received.Add);
        await JoinAsync(harness);

        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 0, content: "first")));
        // index 1 never arrives
        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 2, content: "third")));
        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 3, content: "fourth")));

        ChatStreamGap gap = Assert.Single(gaps);
        Assert.Equal(1, gap.ExpectedIndex);
        Assert.Equal(2, gap.ActualIndex);

        // The running count rides the event, so a consumer can tell one dropped frame from a stream that is losing messages continuously without holding on to ClientState. This is also the only ObservedGaps is also exposed on the event so consumers do not need to retain ClientState.
        Assert.Equal(1, gap.TotalGaps);
        Assert.Equal(gap.TotalGaps, harness.State.Chat.ObservedGaps);

        // Reported ONCE, not once per message afterwards: the counter resynchronises on what the server actually said, which vanilla has no need to do because it has already dropped the connection.
        Assert.Equal(1, harness.State.Chat.ObservedGaps);
        Assert.Equal(4, harness.State.Chat.NextGlobalIndex);

        // And the messages that did arrive were still delivered. Dropping them would turn one missing server frame into three missing client events.
        Assert.Equal(["first", "third", "fourth"], received.Select(m => m.Body.ToPlainText()));
    }

    /// <summary>An out-of-order (replayed lower index) frame is reported too, not silently accepted.</summary>
    [Fact]
    public async Task OutOfOrderMessage_IsReported()
    {
        var harness = new ApplierHarness(Version(776));
        var gaps = new List<ChatStreamGap>();
        harness.Events.Subscribe<ChatStreamGap>(gaps.Add);
        await JoinAsync(harness);

        await harness.ApplyAsync(DecodePlayerChat(Version(776), BuildFrame(776, globalIndex: 0, content: "a")));
        await harness.ApplyAsync(DecodePlayerChat(Version(776), BuildFrame(776, globalIndex: 1, content: "b")));
        await harness.ApplyAsync(DecodePlayerChat(Version(776), BuildFrame(776, globalIndex: 0, content: "replay")));

        ChatStreamGap gap = Assert.Single(gaps);
        Assert.Equal(2, gap.ExpectedIndex);
        Assert.Equal(0, gap.ActualIndex);
        Assert.Equal(1, gap.TotalGaps);
    }

    /// <summary>A second gap reports a running total of 2, so the count on the event is cumulative for the join and not a per-event constant.</summary>
    [Fact]
    public async Task RepeatedGaps_ReportARunningTotal()
    {
        var harness = new ApplierHarness(Version(770));
        var gaps = new List<ChatStreamGap>();
        harness.Events.Subscribe<ChatStreamGap>(gaps.Add);
        await JoinAsync(harness);

        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 3, content: "a")));
        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 9, content: "b")));

        Assert.Equal(2, gaps.Count);
        Assert.Equal(1, gaps[0].TotalGaps);
        Assert.Equal(2, gaps[1].TotalGaps);
        Assert.Equal(2, harness.State.Chat.ObservedGaps);
    }

    /// <summary>A missed <c>player_chat</c> means the SERVER's per-connection signature cache advanced (it pushed the missed message's signatures when it sent the frame this client never received) while this side's mirror did not, so every push after this point can no longer be trusted to land in the same slots the server assigned. The gap detector must mark the shared cache desynced the moment it fires, not merely report the gap as an event, or a later cache-id reference could silently resolve to a slot the server has since reassigned.</summary>
    [Fact]
    public async Task MissingMessage_MarksTheSharedSignatureCacheDesynced()
    {
        var harness = new ApplierHarness(Version(770));
        await JoinAsync(harness);
        Assert.False(harness.SignatureCache.IsDesynced);

        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 0, content: "first")));
        Assert.False(harness.SignatureCache.IsDesynced);

        // index 1 never arrives
        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 2, content: "third")));

        Assert.True(harness.SignatureCache.IsDesynced);
    }

    /// <summary>The negative control: an in-order stream, with no gap ever detected, never touches the cache's trust state.</summary>
    [Fact]
    public async Task InOrderStream_NeverMarksTheSharedSignatureCacheDesynced()
    {
        var harness = new ApplierHarness(Version(770));
        await JoinAsync(harness);

        for (int i = 0; i < 4; i++)
            await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: i, content: $"m{i}")));

        Assert.False(harness.SignatureCache.IsDesynced);
    }

    /// <summary>A re-join restarts the counter, because that is the only place vanilla resets it. A client that carried the counter across a re-join would report a spurious gap on the first message of the new session, which is exactly the false positive that makes disconnecting on this signal unsafe.</summary>
    [Fact]
    public async Task Rejoin_RestartsTheCounter()
    {
        var harness = new ApplierHarness(Version(770));
        var gaps = new List<ChatStreamGap>();
        harness.Events.Subscribe<ChatStreamGap>(gaps.Add);
        await JoinAsync(harness);

        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 0, content: "a")));
        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 1, content: "b")));

        await JoinAsync(harness);
        Assert.Equal(0, harness.State.Chat.NextGlobalIndex);

        await harness.ApplyAsync(DecodePlayerChat(Version(770), BuildFrame(770, globalIndex: 0, content: "c")));
        Assert.Empty(gaps);
    }

    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    private static Task JoinAsync(ApplierHarness harness)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        return harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false,
            Legacy: null)).AsTask();
    }

    private static object DecodePlayerChat(JavaVersion version, byte[] frame)
    {
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        Assert.True(registry.TryGetOutbound(UiPackets.Clientbound.PlayerChat, out int wireId, out BoundPacketCodec _));
        Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec bound));
        Assert.True(bound.IsImplemented);
        return bound.Decode(frame, PacketCodecContext.Registryless);
    }

    /// <summary>Builds <c>player_chat</c> frames for both wire layouts: 770+ starts with <c>globalIndex</c>, while 769 and below use legacy component interactions.</summary>
    private static byte[] BuildFrame(int protocol, int globalIndex, string content)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        if (protocol >= 770)
            w.WriteVarInt(globalIndex);

        w.WriteUuid(Sender);
        w.WriteVarInt(0);
        w.WriteBool(false);         // unsigned: keeps the frame small and the assertions about ordering
        w.WriteString(content, 256);
        w.WriteLong(1_700_000_000_000L);
        w.WriteLong(1L);
        w.WriteVarInt(0);           // no last-seen entries
        w.WriteBool(false);         // no unsigned override
        w.WriteVarInt(0);           // FilterMask PASS_THROUGH
        w.WriteVarInt(1);           // bound chat type
        WriteComponent(ref w, protocol, Component.Text("Notch"));
        w.WriteBool(false);

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteComponent(ref PacketWriter w, int protocol, Component c) =>
        w.WriteComponent(
            c,
            protocol >= 770 ? ComponentWireEra.Modern : ComponentWireEra.Legacy,
            NbtWireFormat.JavaRootTagOrString);
}
