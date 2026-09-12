using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Umpk.Auth;
using Xunit;

namespace Umpk.Auth.Tests;

/// <summary>Binding AND delivery guarantees for the default browser-flow loopback receiver, which must bind loopback only. The receiver must never expose a public interface, and it must actually serve the redirect the browser sends to the URI that was advertised to Microsoft.</summary>
/// <remarks>
/// <para>Every check in this file drives a REAL HTTP request over a real socket and asserts the response status, the response body, and that the code reached <see cref="ILoopbackCodeReceiver.WaitForCodeAsync"/>. TCP reachability alone is insufficient: the request may still receive <c>404 Not Found</c> when the browser's <c>Host</c> header does not match the prefix bound to the accepting socket. These tests therefore verify the complete HTTP exchange on both loopback families.</para>
/// <para>So: the request bytes are written by hand here, including the <c>Host</c> header, rather than delegating to a client that might normalise them. What a browser puts on the wire is the contract.</para>
/// </remarks>
public sealed class LoopbackReceiverTests
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Start_BindsLoopbackOnly_EvenForNonLoopbackRequestedHost()
    {
        await using var receiver = new HttpListenerLoopbackReceiver();

        // Request a wildcard/public host and an auto-assigned port; the receiver must rewrite the host to loopback and choose a concrete free loopback port.
        Uri effective = receiver.Start(new Uri("http://0.0.0.0:0/signin/"), "STATE-bind-only");

        Assert.True(effective.IsLoopback, "the effective redirect uri must be loopback-only");
        Assert.Equal(IPAddress.Loopback, IPAddress.Parse(effective.Host));
        Assert.NotEqual(0, effective.Port);
    }

    /// <summary>A requested "localhost" must survive verbatim, because it is the only loopback host whose port Microsoft ignores when matching a registration; forcing it to 127.0.0.1 would make an OS-assigned port unmatchable. See <see cref="BrowserRedirectStrategyTests"/>.</summary>
    [Fact]
    public async Task Start_PreservesLocalhostHost_AndDoesNotRewriteItToIpLiteral()
    {
        await using var receiver = new HttpListenerLoopbackReceiver();

        Uri effective = receiver.Start(new Uri("http://localhost:0/signin/"), "STATE-preserve");

        Assert.Equal("localhost", effective.Host);
        Assert.True(effective.IsLoopback);
        Assert.NotEqual(0, effective.Port);
        Assert.Equal("/signin/", effective.AbsolutePath);
    }

    /// <summary>A redirect URI advertised as <c>http://localhost:P/</c> must complete a full HTTP round trip whichever loopback family the browser's resolver picked, because that choice is made on the user's machine and is not ours to influence.</summary>
    /// <remarks>The <c>Host</c> header is <c>localhost:P</c> in both cases because a browser preserves the advertised name after resolving it, regardless of the address it dialled.</remarks>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Start_OnLocalhost_CompletesTheRedirect_WhicheverFamilyTheBrowserResolved(string family)
    {
        IPAddress address = IPAddress.Parse(family);
        if (!IsUsable(address))
            return;

        await using var receiver = new HttpListenerLoopbackReceiver();
        Uri effective = receiver.Start(new Uri("http://localhost:0/signin/"), StateFor("CODE-8f21"));

        // Requirement 1: the advertised host stays "localhost". Rewriting it is what started all this.
        Assert.Equal("localhost", effective.Host);

        await AssertRedirectCompletesAsync(receiver, effective, address, "CODE-8f21");
    }

    /// <summary>"http://localhost/signin/" carries the default port 80, which an unprivileged process cannot bind ("Permission denied"). Microsoft ignores the port for localhost, so a free one is chosen and the registration still matches - and the redirect must still complete on both families.</summary>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Start_OnLocalhostWithDefaultPort_BindsAFreePort_AndCompletesTheRedirect(string family)
    {
        IPAddress address = IPAddress.Parse(family);
        if (!IsUsable(address))
            return;

        await using var receiver = new HttpListenerLoopbackReceiver();
        Uri effective = receiver.Start(new Uri("http://localhost/signin/"), StateFor("CODE-defaultport"));

        Assert.NotEqual(80, effective.Port);
        Assert.Equal("localhost", effective.Host);

        await AssertRedirectCompletesAsync(receiver, effective, address, "CODE-defaultport");
    }

    /// <summary>An IPv6 loopback literal is mapped down to the IPv4 literal, because Microsoft does not support an IPv6 literal as a redirect host at all. The advertised host is then an IP literal, so the browser dials exactly that address and sends exactly that <c>Host</c>.</summary>
    [Theory]
    [InlineData("http://[::1]:0/signin/")]
    [InlineData("http://[::ffff:127.0.0.1]:0/signin/")]
    public async Task Start_MapsIPv6LoopbackLiteral_ToIPv4_AndServesTheRedirect(string requested)
    {
        await using var receiver = new HttpListenerLoopbackReceiver();

        Uri effective = receiver.Start(new Uri(requested), StateFor("CODE-mapped"));

        Assert.Equal("127.0.0.1", effective.Host);
        await AssertRedirectCompletesAsync(receiver, effective, IPAddress.Loopback, "CODE-mapped");
    }

    /// <summary>The receiver serves exactly one sign-in, so anything else on the port must not consume it. A browser opens speculative connections and asks for /favicon.ico; neither may end the wait.</summary>
    [Fact]
    public async Task Start_UnrelatedRequests_DoNotConsumeTheOneShotReceiver()
    {
        await using var receiver = new HttpListenerLoopbackReceiver();
        const string state = "STATE-unrelated";
        Uri effective = receiver.Start(new Uri("http://localhost:0/signin/"), state);

        Task<string> waiting = receiver.WaitForCodeAsync(CancellationToken.None);
        string hostHeader = "localhost:" + effective.Port.ToString(CultureInfo.InvariantCulture);

        // A bare connect-and-drop: a port probe, or a browser pre-connecting.
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            await probe.ConnectAsync(IPAddress.Loopback, effective.Port);

        (string faviconStatus, _) = await SendRequestAsync(
            IPAddress.Loopback, effective.Port, hostHeader, "/favicon.ico");
        Assert.Equal("HTTP/1.1 404 Not Found", faviconStatus);
        Assert.False(waiting.IsCompleted, "an unrelated path must not end the wait for the redirect");

        (string status, _) = await SendRequestAsync(
            IPAddress.Loopback, effective.Port, hostHeader, "/signin/?code=CODE-late&state=" + state);
        Assert.Equal("HTTP/1.1 200 OK", status);
        Assert.Equal("CODE-late", await waiting.WaitAsync(IoTimeout));
    }

    /// <summary>A request line may name the whole URI instead of just the path, which is what a client behind a proxy writes. The redirect must still be recognised.</summary>
    [Fact]
    public async Task Start_AcceptsAnAbsoluteFormRequestTarget()
    {
        await using var receiver = new HttpListenerLoopbackReceiver();
        const string state = "STATE-absolute";
        Uri effective = receiver.Start(new Uri("http://localhost:0/signin/"), state);

        Task<string> waiting = receiver.WaitForCodeAsync(CancellationToken.None);

        (string status, _) = await SendRequestAsync(
            IPAddress.Loopback,
            effective.Port,
            "localhost:" + effective.Port.ToString(CultureInfo.InvariantCulture),
            effective.AbsoluteUri + "?code=CODE-absolute&state=" + state);

        Assert.Equal("HTTP/1.1 200 OK", status);
        Assert.Equal("CODE-absolute", await waiting.WaitAsync(IoTimeout));
    }

    /// <summary>The redirect's <c>state</c> is the CSRF check for this path, and it has to gate the HANDOVER, not merely the result. A request that fails it is turned away and does NOT consume the one-shot slot, so the real browser's later redirect still wins.</summary>
    [Fact]
    public async Task WrongOrMissingState_IsRefusedAtTheDoor_AndDoesNotConsumeTheReceiver()
    {
        await using var receiver = new HttpListenerLoopbackReceiver();
        const string expected = "STATE-expected";
        Uri effective = receiver.Start(new Uri("http://localhost:0/signin/"), expected);

        Task<string> waiting = receiver.WaitForCodeAsync(CancellationToken.None);
        string host = "localhost:" + effective.Port.ToString(CultureInfo.InvariantCulture);

        (string wrongState, _) = await SendRequestAsync(
            IPAddress.Loopback, effective.Port, host, "/signin/?code=CODE-elsewhere&state=STATE-somebody-else");
        Assert.Equal("HTTP/1.1 400 Bad Request", wrongState);
        Assert.False(waiting.IsCompleted, "a wrong state must not spend the one-shot slot");

        // Guessing the state is not even required for the attack, so absence must be refused too.
        (string noState, _) = await SendRequestAsync(
            IPAddress.Loopback, effective.Port, host, "/signin/?code=CODE-nostate");
        Assert.Equal("HTTP/1.1 400 Bad Request", noState);
        Assert.False(waiting.IsCompleted, "a missing state must not spend the one-shot slot");

        // The real browser, arriving after both, still completes the sign-in.
        (string good, _) = await SendRequestAsync(
            IPAddress.Loopback, effective.Port, host, "/signin/?code=CODE-real&state=" + expected);
        Assert.Equal("HTTP/1.1 200 OK", good);
        Assert.Equal("CODE-real", await waiting.WaitAsync(IoTimeout));
    }

    /// <summary>The <c>error=</c> branch is terminal, so it has to be state-gated too or it is the same abort primitive wearing a different query parameter.</summary>
    [Fact]
    public async Task ErrorRedirectWithWrongState_IsRefused_AndDoesNotAbortTheWait()
    {
        await using var receiver = new HttpListenerLoopbackReceiver();
        const string expected = "STATE-error-gate";
        Uri effective = receiver.Start(new Uri("http://localhost:0/signin/"), expected);

        Task<string> waiting = receiver.WaitForCodeAsync(CancellationToken.None);
        string host = "localhost:" + effective.Port.ToString(CultureInfo.InvariantCulture);

        (string refused, _) = await SendRequestAsync(
            IPAddress.Loopback, effective.Port, host, "/signin/?error=access_denied&state=STATE-forged");
        Assert.Equal("HTTP/1.1 400 Bad Request", refused);
        Assert.False(waiting.IsCompleted);

        (string good, _) = await SendRequestAsync(
            IPAddress.Loopback, effective.Port, host, "/signin/?code=CODE-after-forged-error&state=" + expected);
        Assert.Equal("HTTP/1.1 200 OK", good);
        Assert.Equal("CODE-after-forged-error", await waiting.WaitAsync(IoTimeout));
    }

    /// <summary>Query values are percent-decoded, NOT form-decoded: <c>%2B</c> becomes <c>+</c> and a literal <c>+</c> stays a <c>+</c>. Form semantics (<c>WebUtility.UrlDecode</c>) turned a literal plus into a space, which would silently corrupt any authorization code that contained one.</summary>
    [Theory]
    [InlineData("ABC%2BDEF", "ABC+DEF")]
    [InlineData("ABC+DEF", "ABC+DEF")]
    [InlineData("A%2FB%3DC", "A/B=C")]
    public async Task QueryValues_ArePercentDecoded_NotFormDecoded(string onTheWire, string expected)
    {
        await using var receiver = new HttpListenerLoopbackReceiver();
        const string state = "STATE-decoding";
        Uri effective = receiver.Start(new Uri("http://localhost:0/signin/"), state);

        Task<string> waiting = receiver.WaitForCodeAsync(CancellationToken.None);

        (string status, _) = await SendRequestAsync(
            IPAddress.Loopback,
            effective.Port,
            "localhost:" + effective.Port.ToString(CultureInfo.InvariantCulture),
            "/signin/?code=" + onTheWire + "&state=" + state);

        Assert.Equal("HTTP/1.1 200 OK", status);
        Assert.Equal(expected, await waiting.WaitAsync(IoTimeout));
    }

    /// <summary>A malformed percent escape must not take the receiver down; it is untrusted input like any other.</summary>
    [Fact]
    public async Task MalformedPercentEscape_IsSurvived()
    {
        await using var receiver = new HttpListenerLoopbackReceiver();
        const string state = "STATE-malformed";
        Uri effective = receiver.Start(new Uri("http://localhost:0/signin/"), state);

        Task<string> waiting = receiver.WaitForCodeAsync(CancellationToken.None);
        string host = "localhost:" + effective.Port.ToString(CultureInfo.InvariantCulture);

        (string status, _) = await SendRequestAsync(
            IPAddress.Loopback, effective.Port, host, "/signin/?code=ABC%ZZ%&state=" + state);
        Assert.Equal("HTTP/1.1 200 OK", status);

        // Whatever the decoder makes of it, the receiver answered and handed something over rather than dying: the point is that a broken escape is not a way to kill the listener.
        string delivered = await waiting.WaitAsync(IoTimeout);
        Assert.Contains("ABC", delivered, StringComparison.Ordinal);
    }

    /// <summary><c>Start</c> is public API and is reachable without <see cref="BrowserRedirectStrategy.Resolve"/> ever running, so it must enforce the same shape rules itself. Relative URIs and non-HTTP schemes must fail as <see cref="AuthException"/> rather than leaking framework exceptions or binding an invalid callback endpoint.</summary>
    [Fact]
    public async Task Start_OnRelativeUri_RefusesWithAuthException()
    {
        await using var receiver = new HttpListenerLoopbackReceiver();

        AuthException ex = Assert.Throws<AuthException>(
            () => { receiver.Start(new Uri("/signin/", UriKind.Relative), "STATE-relative"); });

        Assert.Contains("relative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ftp://127.0.0.1:5123/cb")]
    [InlineData("myapp://localhost/callback")]
    [InlineData("file:///tmp/x")]
    public async Task Start_OnUnservableScheme_RefusesWithAuthException(string requested)
    {
        await using var receiver = new HttpListenerLoopbackReceiver();

        AuthException ex = Assert.Throws<AuthException>(
            () => { receiver.Start(new Uri(requested), "STATE-scheme"); });

        Assert.Contains("scheme", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A sign-in the user declined comes back as <c>?error=...</c>. The page must say so rather than claiming success, and the wait must fail with the error named.</summary>
    [Fact]
    public async Task WaitForCodeAsync_OnErrorRedirect_ShowsFailure_AndReportsTheError()
    {
        await using var receiver = new HttpListenerLoopbackReceiver();
        Uri effective = receiver.Start(new Uri("http://localhost:0/signin/"), "STATE-error");

        Task<string> waiting = receiver.WaitForCodeAsync(CancellationToken.None);
        (string status, string body) = await SendRequestAsync(
            IPAddress.Loopback,
            effective.Port,
            "localhost:" + effective.Port.ToString(CultureInfo.InvariantCulture),
            "/signin/?error=access_denied&state=STATE-error");

        Assert.Equal("HTTP/1.1 200 OK", status);
        Assert.Contains("failed", body, StringComparison.OrdinalIgnoreCase);
        AuthServiceException ex = await Assert.ThrowsAsync<AuthServiceException>(() => waiting.WaitAsync(IoTimeout));
        Assert.Contains("access_denied", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A loopback redirect URI with no explicit port carries the default port (80 for http, 443 for https). On any host other than <c>localhost</c> that shape is unusable twice over: the port must match the registration exactly there, and an unprivileged process cannot bind it. It must be refused as a named <see cref="AuthException"/>, not as whatever the socket layer happens to throw at whoever is calling.</summary>
    [Theory]
    [InlineData("http://127.0.0.1/signin/")]
    [InlineData("http://[::1]/signin/")]
    public async Task Start_OnDefaultPortNonLocalhostHost_RefusesWithAuthException(string requested)
    {
        await using var receiver = new HttpListenerLoopbackReceiver();

        AuthException ex = Assert.Throws<AuthException>(() => { receiver.Start(new Uri(requested), "STATE-port"); });

        Assert.Contains("port", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("localhost", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Disposing while someone waits must CANCEL that wait, not fault it. The browser flow races this receiver against the host's own paste prompt and abandons whichever loses, so a faulted task there becomes an unobserved task exception; a cancelled one never is.</summary>
    [Fact]
    public async Task DisposeAsync_EndsAPendingWait_AsCancelled_NotFaulted()
    {
        var receiver = new HttpListenerLoopbackReceiver();
        receiver.Start(new Uri("http://localhost:0/signin/"), "STATE-dispose");
        Task<string> waiting = receiver.WaitForCodeAsync(CancellationToken.None);

        await receiver.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(IoTimeout));
        Assert.Equal(TaskStatus.Canceled, waiting.Status);
    }

    [Fact]
    public async Task Start_KeepsExplicitLoopbackPort()
    {
        // Probing a free port and then rebinding it is a TOCTOU race: another process can claim the port between the probe closing and the receiver binding. Retry with a fresh port on the (rare) lost race so the explicit-port assertion cannot flake, and let the exception surface on the last try.
        const int maxAttempts = 10;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            int port = FreeLoopbackPort();
            var receiver = new HttpListenerLoopbackReceiver();
            try
            {
                Uri effective = receiver.Start(new Uri($"http://127.0.0.1:{port}/cb/"), "STATE-fixed");
                Assert.True(effective.IsLoopback);
                Assert.Equal(port, effective.Port);
                return;
            }
            catch (AuthException) when (attempt < maxAttempts)
            {
                // Lost the race for this port; try another.
            }
            finally
            {
                await receiver.DisposeAsync();
            }
        }
    }

    /// <summary>Drives one redirect end to end: the request goes in over <paramref name="address"/> carrying the advertised host in its <c>Host</c> header, the success page comes back, and the code reaches the caller waiting on <see cref="ILoopbackCodeReceiver.WaitForCodeAsync"/>.</summary>
    private static async Task AssertRedirectCompletesAsync(
        HttpListenerLoopbackReceiver receiver, Uri effective, IPAddress address, string code)
    {
        string state = StateFor(code);
        Task<string> waiting = receiver.WaitForCodeAsync(CancellationToken.None);

        string hostHeader = effective.Host + ":" + effective.Port.ToString(CultureInfo.InvariantCulture);
        (string status, string body) = await SendRequestAsync(
            address,
            effective.Port,
            hostHeader,
            effective.AbsolutePath + "?code=" + code + "&state=" + state);

        Assert.Equal("HTTP/1.1 200 OK", status);
        Assert.Contains("Sign-in complete", body, StringComparison.Ordinal);
        Assert.Equal(code, await waiting.WaitAsync(IoTimeout));
    }

    /// <summary>The state a receiver is armed with for <paramref name="code"/>. Start and the request it will be asked to serve have to agree on it, so both derive it from the same place.</summary>
    private static string StateFor(string code) => "STATE-" + code;

    /// <summary>Writes one HTTP/1.1 GET by hand and reads the whole response. Deliberately not <c>HttpClient</c>: the <c>Host</c> header is the subject of the test, so the test spells it out.</summary>
    private static async Task<(string StatusLine, string Body)> SendRequestAsync(
        IPAddress address, int port, string hostHeader, string target)
    {
        using var timeout = new CancellationTokenSource(IoTimeout);
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(address, port, timeout.Token);

        byte[] request = Encoding.ASCII.GetBytes(
            "GET " + target + " HTTP/1.1\r\nHost: " + hostHeader + "\r\nConnection: close\r\n\r\n");
        await socket.SendAsync(request, SocketFlags.None, timeout.Token);

        var received = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int read = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, timeout.Token);
            if (read == 0)
                break;

            received.Write(buffer.AsSpan(0, read));
        }

        string text = Encoding.UTF8.GetString(received.ToArray());
        int lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
        int bodyStart = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return (lineEnd < 0 ? text : text[..lineEnd], bodyStart < 0 ? string.Empty : text[(bodyStart + 4)..]);
    }

    /// <summary>True when this machine can actually use <paramref name="address"/>. A machine with IPv6 disabled cannot exercise the <c>:</c> half, and skipping there is honest; asserting would be a lie.</summary>
    private static bool IsUsable(IPAddress address) =>
        address.AddressFamily != AddressFamily.InterNetworkV6 || Socket.OSSupportsIPv6;

    private static int FreeLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
