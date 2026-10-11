using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;

namespace Umpk.Client.Bedrock;

internal sealed class BedrockClientBuilder
{
    private string _host = "127.0.0.1";
    private ushort _port = 19132;
    private IBedrockCredentialProvider? _credentialProvider;
    private IBedrockResourcePackPolicy? _resourcePackPolicy;
    private BedrockClientSkinProfile? _skinProfile;
    private TimeProvider _timeProvider = TimeProvider.System;
    private BedrockClientTimeouts _timeouts = BedrockClientTimeouts.Default;
    private int _requestedChunkRadius = 4;

    internal BedrockClientBuilder WithEndpoint(string host, ushort port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _host = host;
        _port = port;
        return this;
    }

    internal BedrockClientBuilder WithCredentialProvider(IBedrockCredentialProvider provider)
    {
        _credentialProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    internal BedrockClientBuilder WithResourcePackPolicy(IBedrockResourcePackPolicy policy)
    {
        _resourcePackPolicy = policy ?? throw new ArgumentNullException(nameof(policy));
        return this;
    }

    internal BedrockClientBuilder WithSkinProfile(BedrockClientSkinProfile profile)
    {
        _skinProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        return this;
    }

    internal BedrockClientBuilder WithTimeProvider(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        return this;
    }

    internal BedrockClientBuilder WithTimeouts(BedrockClientTimeouts timeouts)
    {
        _timeouts = timeouts ?? throw new ArgumentNullException(nameof(timeouts));
        return this;
    }

    internal BedrockClientBuilder WithRequestedChunkRadius(int radius)
    {
        _requestedChunkRadius = radius;
        return this;
    }

    internal BedrockClient Build() => new(new BedrockClientSettings
    {
        Host = _host,
        Port = _port,
        CredentialProvider = _credentialProvider ?? throw new InvalidOperationException("A Bedrock credential provider is required."),
        ResourcePackPolicy = _resourcePackPolicy ?? throw new InvalidOperationException("A Bedrock resource-pack policy is required."),
        SkinProfile = _skinProfile,
        TimeProvider = _timeProvider,
        Timeouts = _timeouts,
        RequestedChunkRadius = _requestedChunkRadius,
    });
}
