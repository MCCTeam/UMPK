using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock;

internal sealed class BedrockClientSettings
{
    internal string Host { get; init; } = "127.0.0.1";

    internal ushort Port { get; init; } = 19132;

    internal BedrockProtocolProfile Protocol { get; init; } = BedrockProtocols.V1_26_45;

    internal required IBedrockCredentialProvider CredentialProvider { get; init; }

    internal required IBedrockResourcePackPolicy ResourcePackPolicy { get; init; }

    internal BedrockClientSkinProfile? SkinProfile { get; init; }

    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    internal BedrockClientTimeouts Timeouts { get; init; } = BedrockClientTimeouts.Default;

    internal int RequestedChunkRadius { get; init; } = 4;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        ArgumentNullException.ThrowIfNull(Protocol);
        ArgumentNullException.ThrowIfNull(CredentialProvider);
        ArgumentNullException.ThrowIfNull(ResourcePackPolicy);
        ArgumentNullException.ThrowIfNull(TimeProvider);
        ArgumentNullException.ThrowIfNull(Timeouts);
        if (Protocol.Version.Protocol != 2169)
            throw new ArgumentException("The Bedrock client supports protocol 2169 only.", nameof(Protocol));
        if (RequestedChunkRadius is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(RequestedChunkRadius), "The requested chunk radius must be within 0 and 32.");
        Timeouts.Validate();
    }
}
