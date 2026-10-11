using Xunit;

namespace Umpk.Bedrock.IntegrationTests;

[Trait("Category", "BedrockLive")]
public sealed class BedrockLiveFactAttribute : FactAttribute
{
    public BedrockLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("UMPK_BEDROCK_LIVE") != "1")
            Skip = "Bedrock live-server test. Set UMPK_BEDROCK_LIVE=1 to run the isolated BDS join leg.";
    }
}
