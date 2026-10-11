using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock;

internal static class BedrockProtocols
{
    internal static BedrockProtocolProfile V1_26_45 { get; } = new(
        new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
        BedrockPacketDialect.Base2168,
        new BedrockDataProfileId("bedrock-1.26.45-2169"),
        new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));
}
