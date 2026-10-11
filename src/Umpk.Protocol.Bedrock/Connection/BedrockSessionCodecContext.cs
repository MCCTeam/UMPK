using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal sealed record BedrockRegistrySnapshot(
    int Generation,
    ImmutableDictionary<short, string> Items,
    ImmutableDictionary<string, int> Entities)
{
    internal static BedrockRegistrySnapshot Empty { get; } = new(
        0,
        ImmutableDictionary<short, string>.Empty,
        ImmutableDictionary<string, int>.Empty);
}

internal sealed record BedrockSessionCodecContext
{
    internal BedrockSessionCodecContext(BedrockCodecContext routing, BedrockRegistrySnapshot registries, BedrockCodecLimits limits)
    {
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(registries);
        ArgumentNullException.ThrowIfNull(limits);
        Routing = routing;
        Registries = registries;
        Limits = limits;
    }

    internal BedrockCodecContext Routing { get; }

    internal BedrockRegistrySnapshot Registries { get; init; }

    internal BedrockCodecLimits Limits { get; }
}

internal interface IBedrockBatchConsumer
{
    BedrockRegistrySnapshot? Consume(BedrockBatchMember member, BedrockSessionCodecContext context);
}
