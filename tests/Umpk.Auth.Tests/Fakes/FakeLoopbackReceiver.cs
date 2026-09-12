using Umpk.Auth;

namespace Umpk.Auth.Tests.Fakes;

/// <summary>A fake <see cref="ILoopbackCodeReceiver"/> that returns a fixed redirect URI and a pre-seeded authorization code without opening a socket. Validates the expected state matches the request.</summary>
public sealed class FakeLoopbackReceiver(string code, Uri redirectUri) : ILoopbackCodeReceiver
{
    public string? SeenState { get; private set; }

    public Uri Start(Uri requestedRedirectUri, string expectedState)
    {
        SeenState = expectedState;
        return redirectUri;
    }

    public Task<string> WaitForCodeAsync(CancellationToken ct) => Task.FromResult(code);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Creates a single pre-configured <see cref="FakeLoopbackReceiver"/>.</summary>
public sealed class FakeLoopbackReceiverFactory(string code, Uri redirectUri) : ILoopbackCodeReceiverFactory
{
    public ILoopbackCodeReceiver Create() => new FakeLoopbackReceiver(code, redirectUri);
}
