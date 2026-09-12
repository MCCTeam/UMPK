using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Umpk.Auth;
using Umpk.Auth.Tests.Fakes;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class MicrosoftBrowserFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The redirect URI registered for the default client id.</summary>
    private const string RegisteredRedirectUri = "https://mccteam.github.io/redirect.html";

    /// <summary>The default browser flow must send the REGISTERED redirect URI and ask for the fragment response mode, and must not stand up a loopback listener at all.</summary>
    /// <remarks>Asserting the decoded <c>redirect_uri</c> value is the point: the previous test asserted only that the string "redirect_uri=" appeared, which was true of the broken URL that Microsoft rejected outright.</remarks>
    [Fact]
    public async Task Browser_Default_SendsRegisteredRedirectUri_AndFragmentMode_WithNoListener()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            """{"access_token":"MSA_ACCESS","refresh_token":"MSA_REFRESH","expires_in":3600}""");
        handler.AddChain();

        var receiverFactory = new ThrowingReceiverFactory();
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftBrowser,
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            LoopbackReceiverFactory = receiverFactory,
        });

        var interaction = new FakeAuthInteraction { BrowserAuthCode = "PASTED-CODE" };
        JavaSession session = await flow.LoginAsync(interaction, CancellationToken.None);

        Assert.Equal(AuthKind.Microsoft, session.Kind);
        Assert.False(receiverFactory.WasCreated, "the hosted paste-page flow must not open a local listener");

        Uri signIn = Assert.IsType<Uri>(interaction.BrowserSignInUrl);
        Assert.Equal(RegisteredRedirectUri, QueryValue(signIn, "redirect_uri"));
        Assert.Equal("fragment", QueryValue(signIn, "response_mode"));
        Assert.Equal("code", QueryValue(signIn, "response_type"));

        // The auth-code exchange must repeat the SAME redirect URI or Microsoft rejects the grant.
        RecordedRequest tokenReq = handler.Requests.First(r => r.Uri.AbsoluteUri.Contains("oauth2/v2.0/token", StringComparison.Ordinal));
        Assert.Contains("grant_type=authorization_code", tokenReq.Body, StringComparison.Ordinal);
        Assert.Contains("code=PASTED-CODE", tokenReq.Body, StringComparison.Ordinal);
        Assert.Contains(
            "redirect_uri=" + Uri.EscapeDataString(RegisteredRedirectUri),
            tokenReq.Body,
            StringComparison.Ordinal);
    }

    /// <summary>A loopback redirect URI that cannot be both registered and served must be refused BEFORE a browser is opened and before any network call, so no user can reach a Microsoft error page - or, worse, a browser window over a listener that was never able to start.</summary>
    /// <remarks>The first case is the shape the maintainer hit. The second is the same fault for a URI written with no port at all: it carries port 80, which is unbindable unprivileged and unsubstitutable on a host whose port has to match the registration.</remarks>
    [Theory]
    [InlineData("http://127.0.0.1:0/signin/")]
    [InlineData("http://127.0.0.1/signin/")]
    [InlineData("http://[::1]/signin/")]
    public async Task Browser_UnusableLoopbackRedirect_RefusesBeforeOpeningBrowser(string redirectUri)
    {
        var handler = new ScriptedHttpHandler();
        var receiverFactory = new ThrowingReceiverFactory();
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftBrowser,
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            BrowserRedirectUri = new Uri(redirectUri),
            LoopbackReceiverFactory = receiverFactory,
        });

        var interaction = new FakeAuthInteraction { BrowserAuthCode = "UNUSED" };

        AuthException ex = await Assert.ThrowsAsync<AuthException>(
            () => flow.LoginAsync(interaction, CancellationToken.None));

        Assert.Contains("localhost", ex.Message, StringComparison.Ordinal);
        Assert.Null(interaction.BrowserSignInUrl);
        Assert.False(receiverFactory.WasCreated);
        Assert.Empty(handler.Requests);
    }

    /// <summary>A caller who registered their own loopback redirect URI still gets the listener flow, and the listener's URI (not the requested one) is what is sent and exchanged.</summary>
    [Fact]
    public async Task Browser_ExchangesLoopbackCode_ForSession()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            """{"access_token":"MSA_ACCESS","refresh_token":"MSA_REFRESH","expires_in":3600}""");
        handler.AddChain();

        // The REQUESTED URI and the one the listener actually bound are deliberately different: the receiver picks the concrete port. If the product sent the requested URI instead of the bound one, the code would be redirected to a port nothing is listening on. A test that passes the same URI to both cannot tell those apart and stays green either way.
        var requested = new Uri("http://localhost:0/signin/");
        var bound = new Uri("http://localhost:5123/signin/");
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftBrowser,
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            BrowserRedirectUri = requested,
            LoopbackReceiverFactory = new FakeLoopbackReceiverFactory("AUTHCODE-42", bound),
        });

        var interaction = new FakeAuthInteraction();
        JavaSession session = await flow.LoginAsync(interaction, CancellationToken.None);

        Assert.Equal(AuthKind.Microsoft, session.Kind);
        Assert.Equal(MicrosoftChainScript.ProfileName, session.Profile.Name);

        Uri signIn = Assert.IsType<Uri>(interaction.BrowserSignInUrl);
        Assert.Equal(bound.AbsoluteUri, QueryValue(signIn, "redirect_uri"));
        Assert.NotEqual(requested.AbsoluteUri, QueryValue(signIn, "redirect_uri"));

        // The loopback listener reads the code from the redirect request, which requires the default query response mode; asking for fragment here would make the code unreachable.
        Assert.Null(QueryValue(signIn, "response_mode"));

        RecordedRequest tokenReq = handler.Requests.First(r => r.Uri.AbsoluteUri.Contains("oauth2/v2.0/token", StringComparison.Ordinal));
        Assert.Contains("grant_type=authorization_code", tokenReq.Body, StringComparison.Ordinal);
        Assert.Contains("code=AUTHCODE-42", tokenReq.Body, StringComparison.Ordinal);

        // The grant must repeat the SAME redirect URI the code was issued against, which is the bound one, not the requested one.
        Assert.Contains(
            "redirect_uri=" + Uri.EscapeDataString(bound.AbsoluteUri), tokenReq.Body, StringComparison.Ordinal);
    }

    /// <summary>The same path with the REAL receiver, driven by a stand-in browser: the code has to travel from an actual HTTP redirect, through the listener, into the token exchange, whichever loopback family the browser resolved the advertised name to.</summary>
    /// <remarks>
    /// <para><see cref="Browser_ExchangesLoopbackCode_ForSession"/> proves the flow uses the BOUND uri, but it does so against a fake receiver, so it says nothing about whether a browser can reach the real one. This case verifies the HTTP response rather than socket reachability alone.</para>
    /// <para>The stand-in browser pins its connection to one address family while still addressing the advertised name, which is the only way to test the family this machine's resolver did not pick.</para>
    /// </remarks>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Browser_RealLoopbackReceiver_DeliversTheCode_WhicheverFamilyTheBrowserResolved(string family)
    {
        IPAddress address = IPAddress.Parse(family);
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
            return;

        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            """{"access_token":"MSA_ACCESS","refresh_token":"MSA_REFRESH","expires_in":3600}""");
        handler.AddChain();

        string code = "AUTHCODE-" + address.AddressFamily;
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftBrowser,
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            BrowserRedirectUri = new Uri("http://localhost:0/signin/"),
            LoopbackReceiverFactory = HttpListenerLoopbackReceiverFactory.Instance,
        });

        var interaction = new LoopbackBrowserInteraction(address, code);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        JavaSession session = await flow.LoginAsync(interaction, timeout.Token);

        Assert.Equal(MicrosoftChainScript.ProfileName, session.Profile.Name);

        // The advertised redirect URI must still be the localhost NAME: Microsoft waives redirect-port matching only for that literal host, so rewriting it to 127.0.0.1 breaks registration matching.
        Uri signIn = Assert.IsType<Uri>(interaction.BrowserSignInUrl);
        var advertised = new Uri(QueryValue(signIn, "redirect_uri")!);
        Assert.Equal("localhost", advertised.Host);

        RecordedRequest tokenReq = handler.Requests.First(r => r.Uri.AbsoluteUri.Contains("oauth2/v2.0/token", StringComparison.Ordinal));
        Assert.Contains("code=" + code, tokenReq.Body, StringComparison.Ordinal);
        Assert.Contains(
            "redirect_uri=" + Uri.EscapeDataString(advertised.AbsoluteUri), tokenReq.Body, StringComparison.Ordinal);
    }

    /// <summary>A hostile local process must not be able to abort a legitimate sign-in by connecting first.</summary>
    /// <remarks>
    /// <para>Any local process can reach the loopback port and send a request with a wrong or absent <c>state</c>. The receiver must reject that request before it consumes the one-shot slot, leaving the slot available for the browser's valid redirect.</para>
    /// <para>This pins the two-request race at the level where the damage shows: the login either completes with the browser's code or it does not.</para>
    /// </remarks>
    [Fact]
    public async Task Browser_HostileLocalRequestBeforeTheBrowser_DoesNotAbortTheLogin()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            """{"access_token":"MSA_ACCESS","refresh_token":"MSA_REFRESH","expires_in":3600}""");
        handler.AddChain();

        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftBrowser,
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            BrowserRedirectUri = new Uri("http://localhost:0/signin/"),
            LoopbackReceiverFactory = HttpListenerLoopbackReceiverFactory.Instance,
        });

        var interaction = new LoopbackBrowserInteraction(IPAddress.Loopback, "AUTHCODE-VALID")
        {
            HostileStateBeforeBrowser = "STATE-THE-ATTACKER-GUESSED-WRONG",
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        JavaSession session = await flow.LoginAsync(interaction, timeout.Token);

        Assert.Equal(MicrosoftChainScript.ProfileName, session.Profile.Name);

        // The attacker's request must have been turned away rather than served.
        Assert.Equal(HttpStatusCode.BadRequest, interaction.HostileStatus);

        RecordedRequest tokenReq = handler.Requests.First(r => r.Uri.AbsoluteUri.Contains("oauth2/v2.0/token", StringComparison.Ordinal));
        Assert.Contains("code=AUTHCODE-VALID", tokenReq.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("ATTACKER", tokenReq.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browser_HostReturnsCodeDirectly_IsUsed()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            """{"access_token":"MSA_ACCESS","refresh_token":"R","expires_in":3600}""");
        handler.AddChain();

        var redirect = new Uri("http://localhost:5123/signin/");
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftBrowser,
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            BrowserRedirectUri = redirect,
            // Loopback would never complete here; the host-provided code drives the flow.
            LoopbackReceiverFactory = new NeverCompletingReceiverFactory(redirect),
        });

        var interaction = new FakeAuthInteraction { BrowserAuthCode = "HOSTCODE-9" };
        JavaSession session = await flow.LoginAsync(interaction, CancellationToken.None);

        RecordedRequest tokenReq = handler.Requests.First(r => r.Uri.AbsoluteUri.Contains("oauth2/v2.0/token", StringComparison.Ordinal));
        Assert.Contains("code=HOSTCODE-9", tokenReq.Body, StringComparison.Ordinal);
        Assert.Equal(MicrosoftChainScript.ProfileName, session.Profile.Name);
    }

    /// <summary>The paste-page flow has no listener to fall back on, so an empty paste must fail with a message that says what to do rather than posting an empty code to Microsoft.</summary>
    [Fact]
    public async Task Browser_Default_EmptyPastedCode_FailsWithoutCallingMicrosoft()
    {
        var handler = new ScriptedHttpHandler();
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftBrowser,
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
        });

        var interaction = new FakeAuthInteraction { BrowserAuthCode = "   " };

        AuthException ex = await Assert.ThrowsAsync<AuthException>(
            () => flow.LoginAsync(interaction, CancellationToken.None));

        Assert.Contains("paste", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    /// <summary>Returns the decoded value of a single query parameter, or null when absent.</summary>
    private static string? QueryValue(Uri uri, string name)
    {
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            string key = eq < 0 ? pair : pair[..eq];
            if (string.Equals(key, name, StringComparison.Ordinal))
                return eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..]);

        }

        return null;
    }

    private sealed class ThrowingReceiverFactory : ILoopbackCodeReceiverFactory
    {
        public bool WasCreated { get; private set; }

        public ILoopbackCodeReceiver Create()
        {
            WasCreated = true;
            throw new InvalidOperationException("No loopback listener should be created for this flow.");
        }
    }

    /// <summary>Stands in for the browser: reads the sign-in URL the flow produced, then issues the redirect the authorization server would issue, over a connection pinned to one address family. It returns an empty code when the user never pastes anything, so the loopback receiver must deliver the code.</summary>
    private sealed class LoopbackBrowserInteraction(IPAddress address, string code) : IAuthInteraction
    {
        public Uri? BrowserSignInUrl { get; private set; }

        /// <summary>When set, a hostile request carrying THIS state (and an attacker-chosen code) is sent to the callback path before the browser's own redirect, standing in for any local process that connects first without knowing the real state.</summary>
        public string? HostileStateBeforeBrowser { get; init; }

        /// <summary>The status the hostile request got, once it has been sent.</summary>
        public HttpStatusCode? HostileStatus { get; private set; }

        public async Task<string> GetBrowserAuthCodeAsync(Uri signInUrl, CancellationToken ct)
        {
            BrowserSignInUrl = signInUrl;
            var redirect = new Uri(QueryValue(signInUrl, "redirect_uri")!);
            string state = QueryValue(signInUrl, "state")!;

            // ConnectCallback dials the address WE choose while the request keeps addressing the advertised name, so the Host header is "localhost:P" exactly as a browser would send it.
            using var pinned = new SocketsHttpHandler
            {
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(address, context.DnsEndPoint.Port, token).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };

            using var client = new HttpClient(pinned, disposeHandler: false);

            if (HostileStateBeforeBrowser is { } hostileState)
            {
                using HttpResponseMessage hostile = await client
                    .GetAsync(CallbackUri(redirect, "ATTACKER-CODE", hostileState), ct).ConfigureAwait(false);
                HostileStatus = hostile.StatusCode;
            }

            using HttpResponseMessage response = await client
                .GetAsync(CallbackUri(redirect, code, state), ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return string.Empty;
        }

        private static Uri CallbackUri(Uri redirect, string code, string state) =>
            new(redirect,
                redirect.AbsolutePath + "?code=" + Uri.EscapeDataString(code) + "&state=" + Uri.EscapeDataString(state));

        public Task ShowDeviceCodeAsync(DeviceCodePrompt prompt, CancellationToken ct) => Task.CompletedTask;

        public Task<YggdrasilCredentials> GetYggdrasilCredentialsAsync(CancellationToken ct) =>
            Task.FromResult(new YggdrasilCredentials("player", "pw"));
    }

    private sealed class NeverCompletingReceiverFactory(Uri redirectUri) : ILoopbackCodeReceiverFactory
    {
        public ILoopbackCodeReceiver Create() => new NeverCompletingReceiver(redirectUri);
    }

    private sealed class NeverCompletingReceiver(Uri redirectUri) : ILoopbackCodeReceiver
    {
        public Uri Start(Uri requestedRedirectUri, string expectedState) => redirectUri;

        public Task<string> WaitForCodeAsync(CancellationToken ct) =>
            new TaskCompletionSource<string>().Task.WaitAsync(ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
