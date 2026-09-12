using System.Text;
using Umpk.Client.Tests.Support;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.Commands;

/// <summary>The whole inbound half of the server tab-complete round trip, per era, ending at the CONSUMER: a hand-built <c>command_suggestions</c> frame is decoded through the version's OWN bound codec, handed to the real applier chain, and asserted to resolve the request that <c>CommandCompletionService.RequestAsync</c> is awaiting.</summary>
/// <remarks>
/// <para>A test that only asserts the packet decoded proves nothing here: the value has to arrive at the awaiting caller. Both halves have failed here before. The applier seam is real (26.1 live: an opped <c>/tell </c> returned <c>@a @e @n @p @r @s c2tap</c> through it), and the decode half was broken from 1.14 to 1.20.2, where the network-NBT tooltip codec was bound over a JSON-string wire and the resulting <c>ProtocolViolationException</c> killed the session under the default <c>DecodeFailurePolicy</c>.</para>
/// <para>The frame is hand-built rather than produced by the encoder on purpose: encoding and decoding through one wrong codec agree with each other, so a round trip cannot see an era misbinding.</para>
/// </remarks>
public sealed class CommandSuggestionConsumerTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

    public static TheoryData<string> JsonEraVersions => new() { "1.13.2", "1.16.5", "1.18.2", "1.20.2" };

    public static TheoryData<string> NbtEraVersions => new() { "1.20.4", "1.21.5", "26.1", "26.2" };

    [Theory]
    [MemberData(nameof(JsonEraVersions))]
    public Task JsonWireLayout_DecodedSuggestionReachesTheAwaitingRequest(string versionName) =>
        AssertReachesConsumerAsync(versionName, JsonTooltipFrame());

    [Theory]
    [MemberData(nameof(NbtEraVersions))]
    public Task NbtWireLayout_DecodedSuggestionReachesTheAwaitingRequest(string versionName) =>
        AssertReachesConsumerAsync(versionName, NbtTooltipFrame());

    private static async Task AssertReachesConsumerAsync(string versionName, byte[] frame)
    {
        using var cts = new CancellationTokenSource(Budget);
        JavaVersion version = JavaVersions.All.Single(v => v.Version.Name == versionName);
        var harness = new ApplierHarness(version);

        // The first request allocates transaction id 1, which is what the frame below answers.
        Task<CompletionResult> pending = harness.Completions.RequestAsync("/tell ", cts.Token);
        Assert.Contains(harness.Recorder.Packets, p => p is ServerboundCommandSuggestionPacket);

        object decoded = DecodeInbound(version, frame);
        Assert.True(await harness.TryApplyAsync(decoded), "no applier owned the command_suggestions packet");

        CompletionResult result = await pending.WaitAsync(Budget, cts.Token);

        Assert.Equal(6, result.RangeStart);
        Assert.Equal(6, result.RangeEnd);
        CompletionSuggestion first = Assert.Single(result.Suggestions, s => s.Text == "@a");
        Assert.Equal("Nearest player", first.Tooltip);
        Assert.Contains(result.Suggestions, s => s.Text == "Steve" && s.Tooltip is null);
    }

    /// <summary>Decodes a raw play-phase frame through the codec the version's descriptor actually binds.</summary>
    private static object DecodeInbound(JavaVersion version, byte[] frame)
    {
        ProtocolDescriptor descriptor = version.Protocol;
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType candidate) in registry.Packets)
            if (candidate.Id == UiPackets.Clientbound.CommandSuggestions.Id &&
                registry.TryGetInbound(wireId, out BoundPacketCodec codec))
            {
                Assert.True(codec.IsImplemented, $"command_suggestions is a marker on {version.Version.Name}");
                return codec.Decode(frame, PacketCodecContext.Registryless);
            }

        throw new Xunit.Sdk.XunitException($"command_suggestions is not registered inbound on {version.Version.Name}.");
    }

    // transaction 1, range start 6, length 0, "@a" with a tooltip and "Steve" without.
    private static byte[] JsonTooltipFrame() => Cat(
        [0x01, 0x06, 0x00, 0x02],
        Str("@a"),
        [0x01],
        Str("{\"text\":\"Nearest player\"}"),
        Str("Steve"),
        [0x00]);

    private static byte[] NbtTooltipFrame() => Cat(
        [0x01, 0x06, 0x00, 0x02],
        Str("@a"),
        [0x01],
        NbtTextComponent("Nearest player"),
        Str("Steve"),
        [0x00]);

    /// <summary>A network-NBT root compound with one string field <c>text</c>, and no root name.</summary>
    private static byte[] NbtTextComponent(string text)
    {
        byte[] key = Encoding.UTF8.GetBytes("text");
        byte[] value = Encoding.UTF8.GetBytes(text);
        var bytes = new List<byte> { 0x0A, 0x08, (byte)(key.Length >> 8), (byte)key.Length };
        bytes.AddRange(key);
        bytes.Add((byte)(value.Length >> 8));
        bytes.Add((byte)value.Length);
        bytes.AddRange(value);
        bytes.Add(0x00);
        return [.. bytes];
    }

    private static byte[] Str(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        var bytes = new List<byte>();
        uint v = (uint)utf8.Length;
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            bytes.Add(v != 0 ? (byte)(b | 0x80) : b);
        }
        while (v != 0);
        bytes.AddRange(utf8);
        return [.. bytes];
    }

    private static byte[] Cat(params byte[][] parts)
    {
        var bytes = new List<byte>();
        foreach (byte[] part in parts)
            bytes.AddRange(part);

        return [.. bytes];
    }
}
