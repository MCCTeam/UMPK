using System.Net;
using Umpk.Realms.Tests.Fakes;
using Xunit;

namespace Umpk.Realms.Tests;

/// <summary>Pins <c>RealmsErrorParser</c> against a hand-authored status/body-to-kind table. It is independent of the implementation so an incorrect classification can fail the test.</summary>
public sealed class RealmsErrorClassificationTests
{
    private static readonly Guid ProfileId = Guid.NewGuid();

    private static RealmsClient NewClient(ScriptedRealmsHandler handler) =>
        new(new RealmsClientOptions
        {
            Credential = new RealmsSessionCredential("MC_TOKEN_SECRET", ProfileId, "Dinnerbone", "1.21.11"),
            HttpHandlerFactory = handler,
        });

    [Theory]
    // 401 wins over any body errorCode; classified before the body is parsed.
    [InlineData(HttpStatusCode.Unauthorized, "", RealmsErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorCode":6002}""", RealmsErrorKind.Unauthorized)]
    // 403 with a recognized body errorCode classifies on the code, not the status.
    [InlineData(HttpStatusCode.Forbidden, """{"errorCode":6002,"reason":"tos"}""", RealmsErrorKind.TermsNotAgreed)]
    // 403 with no body or code falls back to the actionable terms-of-service classification.
    [InlineData(HttpStatusCode.Forbidden, "", RealmsErrorKind.TermsNotAgreed)]
    [InlineData(HttpStatusCode.Forbidden, """{"errorCode":6005}""", RealmsErrorKind.WorldLocked)]
    // Body errorCode drives classification regardless of the carrying HTTP status.
    [InlineData((HttpStatusCode)400, """{"errorCode":6001}""", RealmsErrorKind.ClientOutdated)]
    [InlineData((HttpStatusCode)400, """{"errorCode":6006}""", RealmsErrorKind.WorldOutOfDate)]
    [InlineData((HttpStatusCode)400, """{"errorCode":6003}""", RealmsErrorKind.ServiceError)]
    // 429/503/277 are always ServiceBusy, checked before the body.
    [InlineData((HttpStatusCode)429, "", RealmsErrorKind.ServiceBusy)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", RealmsErrorKind.ServiceBusy)]
    [InlineData((HttpStatusCode)277, "", RealmsErrorKind.ServiceBusy)]
    // Non-JSON and empty bodies at an otherwise unclassified status must not throw; they degrade to ServiceError.
    [InlineData((HttpStatusCode)500, "<html><body>Server error</body></html>", RealmsErrorKind.ServiceError)]
    [InlineData((HttpStatusCode)500, "", RealmsErrorKind.ServiceError)]
    public async Task ListWorlds_ErrorResponse_ClassifiesAsExpected(HttpStatusCode status, string body, RealmsErrorKind expectedKind)
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/worlds", status, body);
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.ListWorldsAsync(CancellationToken.None));

        Assert.Equal(expectedKind, ex.Kind);
    }

    [Fact]
    public async Task ErrorCode_AndReason_AreSurfaced()
    {
        var handler = new ScriptedRealmsHandler().On(
            HttpMethod.Get, "/worlds", HttpStatusCode.Forbidden, """{"errorCode":6005,"reason":"worldLocked"}""");
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.ListWorldsAsync(CancellationToken.None));

        Assert.Equal(RealmsErrorKind.WorldLocked, ex.Kind);
        Assert.Equal(6005, ex.ErrorCode);
        Assert.Equal("worldLocked", ex.Reason);
    }

    [Fact]
    public async Task ErrorCode_AbsentInBody_IsNull()
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/worlds", (HttpStatusCode)500, "");
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.ListWorldsAsync(CancellationToken.None));

        Assert.Null(ex.ErrorCode);
        Assert.Null(ex.Reason);
    }

    [Fact]
    public async Task ErrorCode_AsJsonString_IsParsed()
    {
        var handler = new ScriptedRealmsHandler().On(
            HttpMethod.Get, "/worlds", (HttpStatusCode)400, """{"errorCode":"6001"}""");
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.ListWorldsAsync(CancellationToken.None));

        Assert.Equal(RealmsErrorKind.ClientOutdated, ex.Kind);
        Assert.Equal(6001, ex.ErrorCode);
    }

    [Fact]
    public async Task Message_DoesNotLeakAccessToken()
    {
        var handler = new ScriptedRealmsHandler().On(
            HttpMethod.Get, "/worlds", HttpStatusCode.Forbidden, """{"errorCode":6002,"reason":"tos not agreed"}""");
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.ListWorldsAsync(CancellationToken.None));

        Assert.DoesNotContain("MC_TOKEN_SECRET", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("MC_TOKEN_SECRET", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidJsonBodyOn200_ClassifiesInvalidResponse()
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/worlds", HttpStatusCode.OK, "not json at all");
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.ListWorldsAsync(CancellationToken.None));

        Assert.Equal(RealmsErrorKind.InvalidResponse, ex.Kind);
    }
}
