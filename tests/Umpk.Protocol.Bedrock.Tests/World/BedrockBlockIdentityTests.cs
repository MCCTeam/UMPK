using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.World;

public sealed class BedrockBlockIdentityTests
{
    [Fact]
    public void FromPalette_PreservesRuntimeFlagAndRawId()
    {
        BedrockBlockStateIdentity runtime = BedrockBlockStateIdentity.FromPalette(isRuntime: true, rawId: 42);
        BedrockBlockStateIdentity hash = BedrockBlockStateIdentity.FromPalette(isRuntime: false, rawId: 42);

        Assert.True(runtime.IsRuntime);
        Assert.False(hash.IsRuntime);
        Assert.Equal(42, runtime.RawId);
        Assert.Equal(42, hash.RawId);
        Assert.NotEqual(runtime, hash);
    }

    [Fact]
    public void PaletteIdentities_StayUnknownUntilLiveResolution()
    {
        BedrockBlockStateIdentity identity = BedrockBlockStateIdentity.FromPalette(isRuntime: true, rawId: 7);

        Assert.True(identity.IsUnknown);
        Assert.False(identity.IsKnownAir);
    }

    [Fact]
    public void LargeCustomIds_PreservedExactly()
    {
        BedrockBlockStateIdentity max = BedrockBlockStateIdentity.FromPalette(isRuntime: true, rawId: int.MaxValue);
        BedrockBlockStateIdentity min = BedrockBlockStateIdentity.FromPalette(isRuntime: false, rawId: int.MinValue);

        Assert.Equal(int.MaxValue, max.RawId);
        Assert.Equal(int.MinValue, min.RawId);
        Assert.True(max.IsUnknown);
        Assert.True(min.IsUnknown);
    }
}
