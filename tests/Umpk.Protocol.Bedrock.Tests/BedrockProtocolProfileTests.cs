using Xunit;

namespace Umpk.Protocol.Bedrock.Tests;

public sealed class BedrockProtocolProfileTests
{
    [Fact]
    public void ProfilePreservesExplicitTargetIdentities()
    {
        var version = new GameVersion(GameEdition.Bedrock, "1.26.45", 2169);
        var profile = new BedrockProtocolProfile(
            version,
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));

        Assert.Equal(version, profile.Version);
        Assert.Equal(2169, profile.Version.Protocol);
        Assert.Equal(BedrockPacketDialect.Base2168, profile.PacketDialect);
        Assert.Equal("bedrock-1.26.45-2169", profile.DataProfile.Value);
        Assert.Equal("bedrock-1.26.45-2169", profile.BehaviorProfile.Value);
    }

    [Fact]
    public void ProfilesUseValueEqualityAcrossEveryIdentityAxis()
    {
        BedrockProtocolProfile first = CreateTargetProfile();
        BedrockProtocolProfile equal = CreateTargetProfile();
        var differentBehavior = new BedrockProtocolProfile(
            first.Version,
            first.PacketDialect,
            first.DataProfile,
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169-experimental"));

        Assert.Equal(first, equal);
        Assert.NotEqual(first, differentBehavior);
    }

    [Fact]
    public void ProfileRejectsNonBedrockVersion()
    {
        var version = new GameVersion(GameEdition.Java, "1.26.45", 2169);

        Assert.Throws<ArgumentException>(() => new BedrockProtocolProfile(
            version,
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169")));
    }

    [Fact]
    public void ProfileRejectsUnknownDialect()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockProtocolProfile(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Unknown,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169")));
    }

    [Fact]
    public void ProfileRejectsDefaultProfileIdentifiers()
    {
        var version = new GameVersion(GameEdition.Bedrock, "1.26.45", 2169);

        Assert.Throws<ArgumentException>(() => new BedrockProtocolProfile(
            version,
            BedrockPacketDialect.Base2168,
            default,
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169")));
        Assert.Throws<ArgumentException>(() => new BedrockProtocolProfile(
            version,
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ProfileRejectsInvalidGameProtocol(int protocol)
    {
        var version = new GameVersion(GameEdition.Bedrock, "1.26.45", protocol);

        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockProtocolProfile(
            version,
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ProfileIdentifiersRejectMissingValues(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new BedrockDataProfileId(value!));
        Assert.ThrowsAny<ArgumentException>(() => new BedrockBehaviorProfileId(value!));
    }

    private static BedrockProtocolProfile CreateTargetProfile() =>
        new(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));
}
