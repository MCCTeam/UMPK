using System.Net.Http;
using Umpk;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Auth.Tests;

/// <summary><see cref="AuthFlowCertificateProvider"/> against a fake <see cref="IMinecraftAuthFlow"/>. The interface exists precisely so this adapter's behaviour is pinnable without a live flow: pass the flow's certificates through, degrade to unsigned on an auth-level failure, and propagate everything else (cancellation, an unrelated fault) rather than swallowing it alongside the auth failure it does handle.</summary>
public sealed class AuthFlowCertificateProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static JavaSession Session() =>
        new(new GameProfile(Guid.NewGuid(), "Dinnerbone"), "ACCESS", Start + TimeSpan.FromHours(1), null, AuthKind.Microsoft);

    private static PlayerCertificates Certificates() =>
        new("PUBLIC_KEY", "PRIVATE_KEY", "SIG1", "SIG2", Start + TimeSpan.FromHours(1), Start);

    [Fact]
    public async Task GetCertificatesAsync_ReturnsTheFlowsCertificates()
    {
        PlayerCertificates certs = Certificates();
        var flow = new FakeMinecraftAuthFlow(_ => Task.FromResult(certs));
        var provider = new AuthFlowCertificateProvider(flow, Session());

        PlayerCertificates? result = await provider.GetCertificatesAsync(CancellationToken.None);

        Assert.Same(certs, result);
    }

    [Fact]
    public async Task GetCertificatesAsync_DegradesToNull_OnAnAuthException()
    {
        var flow = new FakeMinecraftAuthFlow(_ => throw new AuthException("The provider refused."));
        var provider = new AuthFlowCertificateProvider(flow, Session());

        PlayerCertificates? result = await provider.GetCertificatesAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCertificatesAsync_DoesNotSwallowCancellation()
    {
        var flow = new FakeMinecraftAuthFlow(_ => throw new OperationCanceledException());
        var provider = new AuthFlowCertificateProvider(flow, Session());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetCertificatesAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task GetCertificatesAsync_DoesNotSwallowAnUnexpectedFault()
    {
        // The adapter's catch is typed to AuthException only; anything else - here, the flow's own HTTP client failing outright - is not an auth-level "no certificates available" and must not be mistaken for one.
        var flow = new FakeMinecraftAuthFlow(_ => throw new HttpRequestException("Connection refused."));
        var provider = new AuthFlowCertificateProvider(flow, Session());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetCertificatesAsync(CancellationToken.None).AsTask());
    }

    /// <summary>A minimal fake: only <see cref="GetCertificatesAsync"/> is exercised by this adapter.</summary>
    private sealed class FakeMinecraftAuthFlow(Func<CancellationToken, Task<PlayerCertificates>> getCertificates)
        : IMinecraftAuthFlow
    {
        public Task<JavaSession> LoginAsync(IAuthInteraction interaction, CancellationToken ct, string? loginHint = null)
            => throw new NotSupportedException("Not exercised by AuthFlowCertificateProvider.");

        public Task<JavaSession?> TryResumeAsync(string loginHint, CancellationToken ct)
            => throw new NotSupportedException("Not exercised by AuthFlowCertificateProvider.");

        public Task<PlayerCertificates> GetCertificatesAsync(JavaSession session, CancellationToken ct)
            => getCertificates(ct);

        public Task InvalidateAsync(string loginHint, CancellationToken ct)
            => throw new NotSupportedException("Not exercised by AuthFlowCertificateProvider.");

        public void Dispose()
        {
        }
    }
}
