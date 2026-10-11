using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock;

public sealed class BedrockClientBuilderTests
{
    [Fact]
    public void BuilderRequiresCredentialProviderAndResourcePolicy()
    {
        var builder = new BedrockClientBuilder()
            .WithResourcePackPolicy(new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads));

        Assert.Throws<InvalidOperationException>(() => builder.Build());

        var builder2 = new BedrockClientBuilder()
            .WithCredentialProvider(new OfflineBedrockCredentialProvider("Player", Guid.NewGuid()));

        Assert.Throws<InvalidOperationException>(() => builder2.Build());
    }

    [Fact]
    public void BuilderRejectsChunkRadiusOutsideSupportedRange()
    {
        var builder = ValidBuilder().WithRequestedChunkRadius(33);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build());
    }

    [Fact]
    public void BuilderRejectsNon2169Protocol()
    {
        var profile = new BedrockProtocolProfile(
            new Umpk.GameVersion(Umpk.GameEdition.Bedrock, "1.26.44", 2168),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.44-2168"),
            new BedrockBehaviorProfileId("bedrock-1.26.44-2168"));
        var settings = new BedrockClientSettings
        {
            Protocol = profile,
            CredentialProvider = new OfflineBedrockCredentialProvider("Player", Guid.NewGuid()),
            ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
        };

        Assert.Throws<ArgumentException>(() => new BedrockClient(settings));
    }

    [Fact]
    public async Task BuilderKeepsBedrockEndpointDefault()
    {
        System.Net.IPEndPoint? dialed = null;
        var client = new BedrockClient(
            new BedrockClientSettings
            {
                CredentialProvider = new OfflineBedrockCredentialProvider("Player", Guid.NewGuid()),
                ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
            },
            (endpoint, _) =>
            {
                dialed = endpoint;
                throw new InvalidOperationException("Dial must not run in this test.");
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(TestCancellation()).AsTask());

        Assert.NotNull(dialed);
        Assert.Equal(System.Net.IPAddress.Loopback, dialed.Address);
        Assert.Equal(19132, dialed.Port);
        await client.DisposeAsync();
    }

    private static System.Threading.CancellationToken TestCancellation() => new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private static BedrockClientBuilder ValidBuilder() =>
        new BedrockClientBuilder()
            .WithCredentialProvider(new OfflineBedrockCredentialProvider("Player", Guid.NewGuid()))
            .WithResourcePackPolicy(new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads));
}
