using Xunit;

namespace Umpk.Bedrock.IntegrationTests;

public sealed class BedrockServerHarnessPropertyTests
{
    [Fact]
    public void DefaultHarnessLeavesStrictMovementUnset()
    {
        var harness = new BedrockServerHarness();
        Assert.False(harness.StrictMovementRequested);
        Assert.Equal("false", harness.EffectiveMovementStrict);
        List<string> lines = BedrockServerHarness.ApplyProperties(["server-authoritative-movement-strict=false"], 19132, 19133, "run1", harness.StrictMovementRequested);
        Assert.Contains("server-authoritative-movement-strict=false", lines);
        Assert.DoesNotContain("server-authoritative-movement-strict=true", lines);
    }

    [Fact]
    public void StrictHarnessWritesStrictMovementTrue()
    {
        var harness = new BedrockServerHarness(strictMovement: true);
        Assert.True(harness.StrictMovementRequested);
        Assert.Equal("true", harness.EffectiveMovementStrict);
        List<string> lines = BedrockServerHarness.ApplyProperties(["server-authoritative-movement-strict=false"], 19132, 19133, "run1", harness.StrictMovementRequested);
        Assert.Contains("server-authoritative-movement-strict=true", lines);
        Assert.DoesNotContain("server-authoritative-movement-strict=false", lines);
    }

    [Fact]
    public void StrictHarnessAppendsKeyWhenAbsent()
    {
        List<string> lines = BedrockServerHarness.ApplyProperties(["server-port=19132"], 19132, 19133, "run1", true);
        Assert.Contains("server-authoritative-movement-strict=true", lines);
        Assert.Contains("server-port=19132", lines);
    }

    [Fact]
    public void CreativeHarnessRequestsCreativeGamemode()
    {
        var harness = new BedrockServerHarness(creativeMode: true);
        Assert.True(harness.CreativeModeRequested);
        List<string> lines = BedrockServerHarness.ApplyProperties(["gamemode=survival"], 19132, 19133, "run1", false, harness.CreativeModeRequested);
        Assert.Contains("gamemode=creative", lines);
        Assert.DoesNotContain("gamemode=survival", lines);
    }

    [Fact]
    public void DefaultHarnessLeavesGamemodeUnset()
    {
        var harness = new BedrockServerHarness();
        Assert.False(harness.CreativeModeRequested);
        List<string> lines = BedrockServerHarness.ApplyProperties(["gamemode=survival"], 19132, 19133, "run1", false, harness.CreativeModeRequested);
        Assert.Contains("gamemode=survival", lines);
        Assert.DoesNotContain("gamemode=creative", lines);
    }

    [Fact]
    public void DefaultHarnessLeavesLanVisibilityFalse()
    {
        var harness = new BedrockServerHarness();
        Assert.False(harness.LanVisibilityRequested);
        Assert.Equal("false", harness.EffectiveLanVisibility);
        List<string> lines = BedrockServerHarness.ApplyProperties(["enable-lan-visibility=false"], 19132, 19133, "run1", false, false, harness.LanVisibilityRequested);
        Assert.Contains("enable-lan-visibility=false", lines);
        Assert.DoesNotContain("enable-lan-visibility=true", lines);
    }

    [Fact]
    public void LanVisibleHarnessWritesLanVisibilityTrue()
    {
        var harness = new BedrockServerHarness(lanVisibility: true);
        Assert.True(harness.LanVisibilityRequested);
        Assert.Equal("true", harness.EffectiveLanVisibility);
        List<string> lines = BedrockServerHarness.ApplyProperties(["enable-lan-visibility=false"], 19132, 19133, "run1", false, false, harness.LanVisibilityRequested);
        Assert.Contains("enable-lan-visibility=true", lines);
        Assert.DoesNotContain("enable-lan-visibility=false", lines);
    }

    [Fact]
    public void LanVisibilityAppendsKeyWhenAbsent()
    {
        List<string> lines = BedrockServerHarness.ApplyProperties(["server-port=19132"], 19132, 19133, "run1", false, false, true);
        Assert.Contains("enable-lan-visibility=true", lines);
        Assert.Contains("server-port=19132", lines);
    }
}
