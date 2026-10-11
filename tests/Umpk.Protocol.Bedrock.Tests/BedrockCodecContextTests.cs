using Xunit;

namespace Umpk.Protocol.Bedrock.Tests;

public sealed class BedrockCodecContextTests
{
    [Fact]
    public void ContextPreservesDirectionAndSubclientRouting()
    {
        BedrockProtocolProfile profile = CreateTargetProfile();
        var context = new BedrockCodecContext(
            profile,
            BedrockPacketDirection.Clientbound,
            new BedrockSubclientId(1),
            new BedrockSubclientId(2));

        Assert.Same(profile, context.Profile);
        Assert.Equal(BedrockPacketDirection.Clientbound, context.Direction);
        Assert.Equal(1, context.SenderSubclient.Value);
        Assert.Equal(2, context.TargetSubclient.Value);
    }

    [Fact]
    public void ContextRejectsUnknownDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockCodecContext(
            CreateTargetProfile(),
            BedrockPacketDirection.Unknown,
            default,
            default));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(255)]
    public void SubclientRejectsValuesOutsideTwoBits(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockSubclientId(value));
    }

    [Fact]
    public void DefaultSubclientRepresentsThePrimaryClient()
    {
        BedrockSubclientId subclient = default;

        Assert.Equal(0, subclient.Value);
    }

    private static BedrockProtocolProfile CreateTargetProfile() =>
        new(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));
}
