using Xunit;

namespace Umpk.Auth.Tests;

/// <summary><see cref="YggdrasilEndpoints"/>: the authlib-injector URL rules an account's configured provider root has to go through before anything resolves a relative path against it.</summary>
public sealed class YggdrasilEndpointTests
{
    /// <summary>Covers a root without a trailing slash, one that already has it, and a non-default-port host.</summary>
    [Theory]
    [InlineData("https://auth.example/api/yggdrasil", "https://auth.example/api/yggdrasil/")]
    [InlineData("https://auth.example/api/yggdrasil/", "https://auth.example/api/yggdrasil/")]
    [InlineData("http://localhost:25585/authlib-injector", "http://localhost:25585/authlib-injector/")]
    public void EnsureTrailingSlash_AddsOneWhenMissing(string configured, string expected)
        => Assert.Equal(new Uri(expected), YggdrasilEndpoints.EnsureTrailingSlash(new Uri(configured)));

    [Fact]
    public void SessionServer_IsTheProvidersSessionServerSubApi()
    {
        Uri result = YggdrasilEndpoints.SessionServer(new Uri("https://auth.example/api/yggdrasil"));

        Assert.Equal(new Uri("https://auth.example/api/yggdrasil/sessionserver/"), result);
    }

    /// <summary>The whole reason the rule exists: <see cref="Uri"/> relative resolution drops the last segment of a base that does not end in a slash, so resolving the join path under a provider root that was never trailing-slash-normalized would silently land one directory up from where the provider actually serves it.</summary>
    [Fact]
    public void SessionServer_ResolvesTheJoinPathUnderTheProviderRoot()
    {
        Uri sessionServer = YggdrasilEndpoints.SessionServer(new Uri("http://host/authlib-injector"));

        Assert.Equal(
            new Uri("http://host/authlib-injector/sessionserver/session/minecraft/join"),
            new Uri(sessionServer, "session/minecraft/join"));
    }
}
