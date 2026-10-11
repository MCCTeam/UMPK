using Xunit;

namespace Umpk.Client.Bedrock.Policies;

public sealed class BedrockResourcePackPolicyTests
{
    [Fact]
    public async Task NoPackOfferCompletesWithoutDownloadPermission()
    {
        var policy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads);
        BedrockResourcePackDecision decision = await policy.DecideAsync(new BedrockResourcePackOffer(false, []), CancellationToken.None);

        Assert.Equal(BedrockResourcePackDecisionKind.Complete, decision.Kind);
    }

    [Fact]
    public async Task RequiredPackFailsExplicitlyWhenDownloadsAreDisabled()
    {
        var policy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads);
        BedrockResourcePackOffer offer = new(true, [new BedrockResourcePackDescriptor("pack", 10, new byte[32])]);

        BedrockResourcePackDecision decision = await policy.DecideAsync(offer, CancellationToken.None);

        Assert.Equal(BedrockResourcePackDecisionKind.UnsupportedRequiredContent, decision.Kind);
    }

    [Fact]
    public async Task OfferLimitsAreCheckedBeforeAcceptance()
    {
        var options = new BedrockResourcePackPolicyOptions(true, true, false, true, 1, 8, 8, 2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        var policy = new BedrockResourcePackPolicy(options);
        BedrockResourcePackOffer offer = new(false, [new BedrockResourcePackDescriptor("pack", 9, new byte[32])]);

        BedrockResourcePackDecision decision = await policy.DecideAsync(offer, CancellationToken.None);

        Assert.Equal(BedrockResourcePackDecisionKind.LimitsExceeded, decision.Kind);
    }

    [Fact]
    public async Task DownloadDecisionPreservesIndependentCacheAndHashChoices()
    {
        var options = new BedrockResourcePackPolicyOptions(true, true, false, true, 1, 8, 8, 2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        var policy = new BedrockResourcePackPolicy(options);
        BedrockResourcePackOffer offer = new(false, [new BedrockResourcePackDescriptor("pack", 4, new byte[32])]);

        BedrockResourcePackDecision decision = await policy.DecideAsync(offer, CancellationToken.None);

        Assert.Equal(BedrockResourcePackDecisionKind.Download, decision.Kind);
        Assert.False(decision.CacheAfterVerification);
        Assert.True(decision.VerifyHash);
    }

    [Fact]
    public void DownloadSessionRejectsDuplicatesAndHashMismatch()
    {
        byte[] content = [1, 2, 3, 4];
        byte[] expected = System.Security.Cryptography.SHA256.HashData(content);
        var limits = new BedrockResourcePackPolicyOptions(true, true, false, true, 1, 16, 16, 2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        using var session = new BedrockResourcePackDownloadSession(new BedrockResourcePackDescriptor("pack", 4, expected), limits);

        session.AddChunk(0, [1, 2]);
        Assert.Throws<BedrockResourcePackException>(() => session.AddChunk(0, [1, 2]));
        session.AddChunk(1, [3, 9]);
        Assert.Throws<BedrockResourcePackException>(() => session.Complete());
    }

    [Fact]
    public void DownloadSessionEnforcesInactivityTimeout()
    {
        var time = new ManualTimeProvider();
        var limits = new BedrockResourcePackPolicyOptions(true, true, false, true, 1, 16, 16, 2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        using var session = new BedrockResourcePackDownloadSession(new BedrockResourcePackDescriptor("pack", 4, new byte[32]), limits, time);
        time.Advance(TimeSpan.FromSeconds(6));

        BedrockResourcePackException failure = Assert.Throws<BedrockResourcePackException>(() => session.AddChunk(0, [1]));

        Assert.Equal(BedrockResourcePackFailureCode.Timeout, failure.Code);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        internal void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
