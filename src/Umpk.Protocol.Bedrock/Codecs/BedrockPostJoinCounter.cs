namespace Umpk.Protocol.Bedrock;

internal sealed class BedrockPostJoinCounter(Action<BedrockPacketHeader> observe) : IBedrockBatchConsumer
{
    private readonly Action<BedrockPacketHeader> _observe = observe ?? throw new ArgumentNullException(nameof(observe));

    public BedrockRegistrySnapshot? Consume(BedrockBatchMember member, BedrockSessionCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(context);
        _observe(member.Header);
        return null;
    }
}
