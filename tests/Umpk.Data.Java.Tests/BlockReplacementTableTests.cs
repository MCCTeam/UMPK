using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class BlockReplacementTableTests
{
    public static IEnumerable<object[]> Protocols() => JavaVersions.All.Select(row => row.Version.Protocol).Distinct().Select(protocol => new object[] { protocol });

    [Fact]
    public void PackedRulesPreserveTheLowerBlockDependencyAndUnknownStates()
    {
        IBlockInteractionSource data = Packed();
        Assert.True(data.HasData);
        Assert.True(data.TryGetCubeReplacement(2808, 2802, out BlockReplacementInfo grass));
        Assert.Equal(new BlockReplacementInfo(true, true), grass);
        Assert.True(data.TryGetCubeReplacement(2808, 0, out BlockReplacementInfo flower));
        Assert.Equal(new BlockReplacementInfo(false, true), flower);
        Assert.False(data.TryGetCubeReplacement(2808, null, out _));
        Assert.False(data.TryGetCubeReplacement(2808, 9000, out _));
        Assert.False(data.TryGetCubeReplacement(1, 0, out _));
        Assert.True(data.TryGetCubeReplacement(16, null, out BlockReplacementInfo stone));
        Assert.Equal(new BlockReplacementInfo(false, false), stone);
        Assert.False(((IBlockInteractionSource)JavaBlockInteractions.Create([0])).TryGetCubeReplacement(0, 0, out _));
    }

    [Theory]
    [InlineData("negative-count")]
    [InlineData("excess-count")]
    [InlineData("negative-state")]
    [InlineData("reversed")]
    [InlineData("overlap")]
    [InlineData("flags")]
    [InlineData("unknown-target")]
    [InlineData("unknown-below")]
    [InlineData("redundant")]
    [InlineData("duplicate-override")]
    public void MalformedPackedReplacementRulesAreRejected(string defect) => Assert.Throws<ProtocolViolationException>(() => Packed(defect));

    [Theory]
    [MemberData(nameof(Protocols))]
    public void EveryGeneratedRuleMatchesTheNativeDataset(int protocol)
    {
        string file = Path.Combine(FixturePaths.RepoRoot(), "data", "java", protocol.ToString(CultureInfo.InvariantCulture), "block-replacement.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
        JsonElement root = document.RootElement, overrides = root.GetProperty("below_overrides");
        IBlockInteractionSource source = JavaGameData.BlockInteractionData(protocol);
        foreach (JsonElement range in root.GetProperty("ranges").EnumerateArray())
            for (int state = range[0].GetInt32(); state <= range[1].GetInt32(); state++)
            {
                Assert.True(source.TryGetCubeReplacement(state, 0, out BlockReplacementInfo actual));
                Assert.Equal(Info(range[2].GetInt32()), actual);
                if (overrides.TryGetProperty(state.ToString(CultureInfo.InvariantCulture), out _))
                    Assert.False(source.TryGetCubeReplacement(state, null, out _));
            }
        foreach (JsonProperty target in overrides.EnumerateObject())
            foreach (JsonElement range in target.Value.EnumerateArray())
                for (int lower = range[0].GetInt32(); lower <= range[1].GetInt32(); lower++)
                {
                    Assert.True(source.TryGetCubeReplacement(int.Parse(target.Name, CultureInfo.InvariantCulture), lower, out BlockReplacementInfo actual));
                    Assert.Equal(Info(range[2].GetInt32()), actual);
                }
        Assert.False(source.TryGetCubeReplacement(-1, 0, out _));
        Assert.False(source.TryGetCubeReplacement(int.MaxValue, 0, out _));
    }

    private static BlockReplacementInfo Info(int flags) => new((flags & 1) != 0, (flags & 2) != 0);

    private static JavaBlockInteractions Packed(string? defect = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(4);
        for (int i = 0; i < 4; i++)
            writer.WriteVarInt(0);
        writer.WriteVarInt(defect == "negative-count" ? -1 : defect == "excess-count" ? 1000 : 4);
        Range(ref writer, defect == "negative-state" ? -1 : 0, 0, 3);
        Range(ref writer, defect == "overlap" ? 0 : 16, defect == "reversed" ? 15 : 16, 0);
        Range(ref writer, 2802, 2803, defect == "flags" ? 4 : 3);
        Range(ref writer, 2808, 2811, 2);
        writer.WriteVarInt(defect == "duplicate-override" ? 2 : 1);
        for (int i = 0; i < (defect == "duplicate-override" ? 2 : 1); i++)
        {
            writer.WriteVarInt(defect == "unknown-target" ? 42 : 2808);
            writer.WriteVarInt(1);
            Range(ref writer, defect == "unknown-below" ? 11 : 2802, defect == "unknown-below" ? 12 : 2803, defect == "redundant" ? 2 : 3);
        }
        return JavaBlockInteractions.Create(buffer.WrittenSpan);
    }

    private static void Range(ref PacketWriter writer, int first, int last, int flags)
    {
        writer.WriteVarInt(first);
        writer.WriteVarInt(last);
        writer.WriteByte((byte)flags);
    }
}
