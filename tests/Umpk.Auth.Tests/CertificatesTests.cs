using System.Net;
using System.Net.Http;
using Umpk.Auth;
using Umpk.Auth.Tests.Fakes;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class CertificatesTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string CannedCertificates = """
        {
          "keyPair": {
            "privateKey": "-----BEGIN RSA PRIVATE KEY-----\nPRIVATE\n-----END RSA PRIVATE KEY-----\n",
            "publicKey": "-----BEGIN RSA PUBLIC KEY-----\nPUBLIC\n-----END RSA PUBLIC KEY-----\n"
          },
          "publicKeySignature": "U0lHTkFUVVJFX1Yx",
          "publicKeySignatureV2": "U0lHTkFUVVJFX1Yy",
          "expiresAt": "2026-02-01T00:00:00.0000000Z",
          "refreshedAfter": "2026-01-15T00:00:00.0000000Z"
        }
        """;

    [Fact]
    public async Task GetCertificates_ParsesCannedResponse()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "player/certificates", HttpStatusCode.OK, CannedCertificates);

        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            TokenStore = new InMemoryTokenStore(),
        });

        var session = new JavaSession(new GameProfile(Guid.NewGuid(), "dinnerbone"), "MC_TOKEN", Start + TimeSpan.FromHours(1), null, AuthKind.Microsoft);
        PlayerCertificates certs = await flow.GetCertificatesAsync(session, CancellationToken.None);

        Assert.Contains("PUBLIC", certs.PublicKeyPem, StringComparison.Ordinal);
        Assert.Contains("PRIVATE", certs.PrivateKeyPem, StringComparison.Ordinal);
        Assert.Equal("U0lHTkFUVVJFX1Yx", certs.PublicKeySignature);
        Assert.Equal("U0lHTkFUVVJFX1Yy", certs.PublicKeySignatureV2);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), certs.ExpiresAt);

        // The access token was sent as a bearer.
        RecordedRequest req = handler.Requests.Single(r => r.Uri.AbsoluteUri.Contains("player/certificates", StringComparison.Ordinal));
        Assert.Equal("MC_TOKEN", req.BearerToken);
    }

    [Fact]
    public async Task GetCertificates_UsesCacheOnSecondCall()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "player/certificates", HttpStatusCode.OK, CannedCertificates);

        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            TokenStore = new InMemoryTokenStore(),
        });

        var session = new JavaSession(new GameProfile(Guid.NewGuid(), "dinnerbone"), "T", Start + TimeSpan.FromHours(1), null, AuthKind.Microsoft);
        await flow.GetCertificatesAsync(session, CancellationToken.None);
        await flow.GetCertificatesAsync(session, CancellationToken.None);

        int fetches = handler.Requests.Count(r => r.Uri.AbsoluteUri.Contains("player/certificates", StringComparison.Ordinal));
        Assert.Equal(1, fetches);
    }
}
