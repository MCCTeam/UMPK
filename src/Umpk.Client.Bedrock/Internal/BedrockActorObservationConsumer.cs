using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.Internal;

internal sealed class BedrockActorObservationConsumer(
    Action<BedrockPacketHeader> observe,
    Action<BedrockBatchMember, BedrockSessionCodecContext> apply) : IBedrockBatchConsumer
{
    private readonly Action<BedrockPacketHeader> _observe = observe ?? throw new ArgumentNullException(nameof(observe));
    private readonly Action<BedrockBatchMember, BedrockSessionCodecContext> _apply = apply ?? throw new ArgumentNullException(nameof(apply));

    public BedrockRegistrySnapshot? Consume(BedrockBatchMember member, BedrockSessionCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(context);
        _observe(member.Header);
        _apply(member, context);
        return null;
    }
}
