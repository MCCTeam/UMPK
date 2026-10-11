using Umpk.BedrockDataGen.Schema;
using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class LargeBitSetEncodingPolicyTests
{
    [Fact]
    public void AllZeroValuePreservesNineteenByteProfileWidth()
    {
        var policy = new LargeBitSetEncodingPolicy(131, 32);

        byte[] encoded = policy.CreateVector([]);

        Assert.Equal(19, encoded.Length);
        Assert.All(encoded[..^1], static value => Assert.Equal(0x80, value));
        Assert.Equal(0, encoded[^1]);
    }

    [Fact]
    public void Bits127128And130FitTheBoundedLargeEncoding()
    {
        var policy = new LargeBitSetEncodingPolicy(131, 32);

        byte[] encoded = policy.CreateVector([127, 128, 130]);

        Assert.Equal(19, encoded.Length);
        Assert.Equal(0x16, encoded[^1]);
        Assert.Equal(19, policy.EncodedByteCount);
    }

    [Fact]
    public void BitOutsideLogicalProfileFails()
    {
        var policy = new LargeBitSetEncodingPolicy(131, 32);

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.CreateVector([131]));
    }
}
