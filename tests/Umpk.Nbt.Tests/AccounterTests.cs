using Umpk.Nbt;
using Xunit;

namespace Umpk.Nbt.Tests;

public sealed class AccounterTests
{
    [Fact]
    public void AccountBytes_TripsAtQuota()
    {
        var a = NbtAccounter.Create(10);
        a.AccountBytes(10);
        Assert.Equal(10, a.Usage);
        Assert.Throws<NbtSizeLimitException>(() => a.AccountBytes(1));
    }

    [Fact]
    public void AccountBytes_MultiplyOverload()
    {
        var a = NbtAccounter.Create(100);
        a.AccountBytes(4, 10);
        Assert.Equal(40, a.Usage);
    }

    [Fact]
    public void PushDepth_TripsAtLimit()
    {
        var a = new NbtAccounter(long.MaxValue, 2);
        a.PushDepth();
        a.PushDepth();
        Assert.Equal(2, a.Depth);
        Assert.Throws<NbtDepthLimitException>(() => a.PushDepth());
    }

    [Fact]
    public void PopDepth_BelowZero_Throws()
    {
        var a = NbtAccounter.Unlimited();
        Assert.Throws<NbtFormatException>(() => a.PopDepth());
    }

    [Fact]
    public void PushPop_Balances()
    {
        var a = NbtAccounter.Unlimited();
        a.PushDepth();
        a.PopDepth();
        Assert.Equal(0, a.Depth);
    }

    [Fact]
    public void NegativeLimits_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NbtAccounter(-1, 512));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NbtAccounter(1, -1));
    }

    [Fact]
    public void Defaults_MatchVanillaConstants()
    {
        Assert.Equal(512, NbtAccounter.DefaultMaxDepth);
        Assert.Equal(2 * 1024 * 1024, NbtAccounter.DefaultNetworkQuota);
    }
}
