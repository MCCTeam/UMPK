using System.Buffers;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.TestKit.Corpus;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>
/// Per-era pins for <c>update_advancements</c>, driven by recorded frames rather than round trips. Advancements decode on protocols 335-767, 770-773, and 776.
/// <para>Two properties are asserted per era, and the second is the one that matters: the frame decodes to the expected NON-EMPTY content and re-encodes byte-identically, AND the neighbouring era's codec does NOT accept the same bytes. An empty or absent field decodes the same way under right and wrong framing, so an era boundary is only really pinned when the wrong side of it is shown to fail.</para>
/// </summary>
public sealed class AdvancementFramePinTests
{
    /// <summary>The 1.17 (protocol 755) steady-play capture carries a fully populated advancement tree: 18 advancements, all with DisplayInfo (real icon item ids, a background texture on the root, frame flags and tree coordinates), plus 18 progress entries. It is the only recorded frame in the whole corpus that exercises the DisplayInfo branch, so it is the load-bearing pin for the pre-component icon slot and the JSON text encoding.</summary>
    [Fact]
    public void Protocol755_PopulatedTree_DecodesWithDisplayInfo()
    {
        ClientboundUpdateAdvancementsPacket p = DecodeCorpusFrame(755, "play-idle");

        Assert.False(p.Reset);
        Assert.Equal(18, p.Added.Count);
        Assert.Equal(18, p.Progress.Count);
        Assert.Empty(p.Removed);

        AdvancementEntry root = p.Added.Single(a => a.Id == Identifier.Minecraft("adventure/root"));
        AdvancementDisplayInfo display = Assert.IsType<AdvancementDisplayInfo>(root.Value.Display);
        Assert.Null(root.Value.Parent);
        Assert.Equal(
            "minecraft:textures/gui/advancements/backgrounds/adventure.png",
            display.Background?.ToString());
        Assert.Equal(AdvancementFrameType.Task, display.Frame);
        Assert.False(display.ShowToast);
        Assert.False(display.Hidden);
        Assert.Equal(0f, display.X);
        Assert.Equal(5.75f, display.Y);
        Assert.False(display.Icon.IsEmpty);
        Assert.Equal(1, display.Icon.Count);
        Assert.Equal(["killed_by_something", "killed_something"], [.. root.Value.Criteria]);
        Assert.False(root.Value.SendsTelemetryEvent);

        // A child node: parented, toast-flagged (flags bit 2), no background, and offset in the tree.
        AdvancementEntry child = p.Added.Single(a => a.Id == Identifier.Minecraft("adventure/spyglass_at_parrot"));
        AdvancementDisplayInfo childDisplay = Assert.IsType<AdvancementDisplayInfo>(child.Value.Display);
        Assert.Equal("minecraft:adventure/root", child.Value.Parent?.ToString());
        Assert.Null(childDisplay.Background);
        Assert.True(childDisplay.ShowToast);
        Assert.Equal(1f, childDisplay.X);
        Assert.Equal(1f, childDisplay.Y);
        Assert.Equal(["spyglass_at_parrot"], [.. child.Value.Criteria]);

        Assert.Contains(p.Progress, e => e.Id == Identifier.Minecraft("adventure/sleep_in_bed"));
    }

    /// <summary>The 1.20 (763) telemetry split, proven on real bytes. 1.19.4 (762) and 1.20 (763) send the same two recipe advancements, and the recorded frames differ by exactly two bytes: 318 vs 320. Those two bytes are the per-node telemetry bool that 1.20 appended after the requirements. Both frames must decode under their own era shape and be rejected by the neighbour's.</summary>
    [Fact]
    public void TelemetryBool_ArrivesAt763_NotAt762()
    {
        byte[] body762 = CorpusFrame(762, "chunk-join");
        byte[] body763 = CorpusFrame(763, "chunk-join");
        Assert.Equal(318, body762.Length);
        Assert.Equal(320, body763.Length);

        ClientboundUpdateAdvancementsPacket p762 = Decode(AdvancementCodecs.UpdateAdvancementsV1_13_2, body762, 762);
        ClientboundUpdateAdvancementsPacket p763 = Decode(AdvancementCodecs.UpdateAdvancementsV1_20, body763, 763);
        Assert.Equal(2, p762.Added.Count);
        Assert.Equal(2, p763.Added.Count);
        Assert.All(p762.Added, a => Assert.NotEmpty(a.Value.Criteria));
        Assert.All(p763.Added, a => Assert.NotEmpty(a.Value.Criteria));

        // Both eras carry the criterion list, so the ONLY difference is the telemetry bool. Cross-decode must fail in both directions: 763's frame overruns under the 762 shape (two unread bytes), and 762's frame runs past its end under the 763 shape.
        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_13_2, body763, 763);
        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_20, body762, 762);
    }

    /// <summary>The 1.20.2 (764) criterion-list removal, proven on real bytes: the same two recipe advancements drop from 320 to 274 bytes because the criterion-name list left the node. The 763 shape must reject the 764 frame and vice versa.</summary>
    [Fact]
    public void CriterionList_Removed_At764()
    {
        byte[] body763 = CorpusFrame(763, "chunk-join");
        byte[] body764 = CorpusFrame(764, "chunk-join");
        Assert.Equal(274, body764.Length);

        ClientboundUpdateAdvancementsPacket p764 = Decode(AdvancementCodecs.UpdateAdvancementsV1_20_2, body764, 764);
        Assert.Equal(2, p764.Added.Count);
        Assert.All(p764.Added, a => Assert.Empty(a.Value.Criteria));
        Assert.All(p764.Added, a => Assert.NotEmpty(a.Value.Requirements));
        Assert.False(p764.ShowAdvancements is false, "pre-1.21.5 decode surfaces ShowAdvancements as true");

        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_20, body764, 764);
        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_20_2, body763, 763);
    }

    /// <summary>The 1.21.5 (770) show-advancements split on real bytes: the 769 and 770 frames carry the same two recipe advancements and differ by exactly the one trailing bool (274 vs 275 bytes).</summary>
    [Fact]
    public void ShowAdvancementsBool_ArrivesAt770()
    {
        byte[] body769 = CorpusFrame(769, "chunk-join");
        byte[] body770 = CorpusFrame(770, "chunk-join");
        Assert.Equal(274, body769.Length);
        Assert.Equal(275, body770.Length);

        Decode(AdvancementCodecs.UpdateAdvancementsV1_21_5, body770, 770);
        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_21_5, body769, 769);
    }

    /// <summary>Every recorded advancements frame in the corpus decodes and re-encodes byte-identically under the era its protocol resolves through the real registration table. This is the whole-band sweep: it covers 393-776, including the protocols whose recorded frame is an empty tree (those cannot pin a shape on their own, which is exactly why the boundary tests above exist).</summary>
    [Theory]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(498)]
    [InlineData(573)]
    [InlineData(735)]
    [InlineData(754)]
    [InlineData(755)]
    [InlineData(758)]
    [InlineData(759)]
    [InlineData(762)]
    [InlineData(763)]
    [InlineData(764)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(770)]
    [InlineData(776)]
    public void EveryRecordedAdvancementFrame_RoundTripsByteExact(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        Assert.True(version!.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));

        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        int seen = 0;
        foreach ((int wireId, byte[] body) in AdvancementFrames(protocol))
        {
            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented, $"update_advancements is a marker on protocol {protocol}");

            object packet = codec.Decode(body, context);
            var buffer = new ArrayBufferWriter<byte>(body.Length + 8);
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, context);
            Assert.Equal(body, buffer.WrittenSpan.ToArray());
            seen++;
        }

        Assert.True(seen > 0, $"no update_advancements frame recorded for protocol {protocol}");
    }

    // helpers

    private static ClientboundUpdateAdvancementsPacket Decode(
        PacketCodec<ClientboundUpdateAdvancementsPacket> codec, byte[] body, int protocol)
    {
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var reader = new PacketReader(body);
        ClientboundUpdateAdvancementsPacket packet = codec.Decode(ref reader, context);
        Assert.Equal(0, reader.Remaining);
        return packet;
    }

    private static void AssertRejects(
        PacketCodec<ClientboundUpdateAdvancementsPacket> codec, byte[] body, int protocol)
    {
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        bool rejected;
        try
        {
            var reader = new PacketReader(body);
            codec.Decode(ref reader, context);
            rejected = reader.Remaining != 0;
        }
        catch (Exception)
        {
            rejected = true;
        }

        Assert.True(rejected, "the wrong era's codec consumed the frame exactly, so the era boundary is not pinned");
    }

    private static ClientboundUpdateAdvancementsPacket DecodeCorpusFrame(int protocol, string scenario)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        Assert.True(version!.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        (int wireId, byte[] body) = LargestAdvancementFrame(protocol, scenario);
        Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        return Assert.IsType<ClientboundUpdateAdvancementsPacket>(codec.Decode(body, context));
    }

    private static byte[] CorpusFrame(int protocol, string scenario) => LargestAdvancementFrame(protocol, scenario).Body;

    private static (int WireId, byte[] Body) LargestAdvancementFrame(int protocol, string scenario)
    {
        (int WireId, byte[] Body)? best = null;
        foreach ((int wireId, byte[] body) in AdvancementFrames(protocol, scenario))
            if (best is null || body.Length > best.Value.Body.Length)
                best = (wireId, body);

        Assert.True(best is not null, $"no update_advancements frame in the protocol {protocol} {scenario} capture");
        return best!.Value;
    }

    private static IEnumerable<(int WireId, byte[] Body)> AdvancementFrames(int protocol, string? scenario = null)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        version!.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry);

        string dir = Path.Combine(FixturePaths.CorpusRoot, protocol.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (string path in Directory.EnumerateFiles(dir, "*.umpkcap").OrderBy(static p => p, StringComparer.Ordinal))
        {
            if (scenario is not null && !Path.GetFileNameWithoutExtension(path).Equals(scenario, StringComparison.Ordinal))
                continue;

            LoadedCorpus corpus = CorpusLoader.LoadFileAsync(path).GetAwaiter().GetResult();
            foreach (RecordedFrame frame in corpus.Frames)
            {
                if (frame.Direction != CorpusDirection.Clientbound ||
                    !registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec) ||
                    codec.Type.Id != Identifier.Minecraft("update_advancements"))
                    continue;

                yield return (frame.WireId, frame.Body);
            }
        }
    }
}
