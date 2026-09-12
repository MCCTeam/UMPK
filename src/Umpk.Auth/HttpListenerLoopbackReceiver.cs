using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Umpk.Auth;

/// <summary>The default <see cref="ILoopbackCodeReceiver"/>: a one-shot HTTP responder bound to loopback. When the browser redirects with <c>?code=...&amp;state=...</c> it answers with a small confirmation page and hands the code to <see cref="WaitForCodeAsync"/>. A requested port of 0 becomes a free loopback port chosen at bind time, and the URI actually bound is returned from <see cref="Start"/>.</summary>
/// <remarks>
/// <para><see cref="HttpListener"/> cannot implement this safely. The cross-platform managed listener binds ONE SOCKET PER PREFIX and matches a request's <c>Host</c> header only against the prefixes registered on the socket that accepted THAT connection. A browser sent to <c>http://localhost:P/</c> always sends <c>Host: localhost:P</c>, whichever address its resolver picked for the name. So registering <c>http://localhost:P/</c> plus <c>http://127.0.0.1:P/</c> - the obvious way to cover both loopback families - binds both families but answers <c>404 Not Found</c> to <c>Host: localhost</c> on the <c>127.0.0.1</c> socket, and sign-in hangs with no diagnostic on every machine whose resolver prefers IPv4.</para>
/// <para>That is not fixable with prefixes: a prefix's host text decides both which address is bound and which <c>Host</c> is answered, so no prefix set binds both families and answers the same name on both. The only prefix hosts that answer an arbitrary <c>Host</c> are <c>*</c> and <c>+</c>, which bind <c>IPAddress.Any</c> - a public interface, which an auth receiver must never do. A bracketed IPv6 literal cannot be a prefix at all. Hence a minimal socket-level responder: it serves exactly one sign-in, so the parts of HTTP it needs are the request line, the query string, and one response.</para>
/// <para>The <c>Host</c> header is deliberately NOT matched. The sockets are bound to loopback addresses, so nothing off this machine can reach them; matching the header only ever rejected the browser. What IS matched, at the door, is the CSRF <c>state</c>: everything on this machine can reach a loopback port, so a request that does not carry the state given to <see cref="Start"/> is answered <c>400 Bad Request</c> and does NOT consume the one-shot slot. The receiver stays armed and the real browser's later redirect still wins.</para>
/// <para>Host handling is deliberate on all three branches:</para>
/// <list type="bullet">
/// <item>
/// <c>localhost</c> is preserved, since it is the only host whose port Microsoft ignores when matching a registration (see <see cref="BrowserRedirectStrategy"/>). Because it is a NAME, the browser resolves it independently of us, so both <c>127.0.0.1</c> and <c>[::1]</c> are bound on the same port and either is served. Only those two canonical loopback addresses are bound: binding whatever the local resolver claims <c>localhost</c> means would let a hostile hosts file put this listener on a public interface.
/// </item>
/// <item>
/// An IPv6 loopback literal is mapped down to <c>127.0.0.1</c>, because Microsoft does not accept an IPv6 literal as a redirect host at all, and the port waiver that makes preserving <c>localhost</c> necessary does not apply to a literal anyway. An IP literal host is bound in its own family only: the browser dials exactly the address it was given.
/// </item>
/// <item>Anything not loopback is forced to <c>127.0.0.1</c> so this receiver never exposes a public interface.</item>
/// </list>
/// </remarks>
[UnsupportedOSPlatform("browser")]
public sealed class HttpListenerLoopbackReceiver : ILoopbackCodeReceiver
{
    /// <summary>How many times an OS-assigned port is re-picked when the second address family loses a race for it. Both families must share one port, and the window between the two binds is not closable.</summary>
    private const int PortPairingAttempts = 10;

    /// <summary>Cap on the request head buffered per connection. A sign-in redirect is far smaller.</summary>
    private const int MaxRequestHeadBytes = 16 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly List<Socket> _listeners = [];
    private readonly TaskCompletionSource<RedirectParameters> _redirect =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _shutdown = new();

    private Uri? _redirectUri;
    private string _callbackPath = "/";
    private string _expectedState = string.Empty;
    private bool _disposed;

    /// <inheritdoc />
    /// <exception cref="AuthException">The requested URI is relative, or its scheme is not <c>http</c>/<c>https</c>; or it carries the scheme's default port on a host other than <see cref="BrowserRedirectStrategy.PortAgnosticLoopbackHost"/>, which is unbindable without privileges and unusable as a registration match; or the loopback endpoint could not be bound.</exception>
    public Uri Start(Uri requestedRedirectUri, string expectedState)
    {
        ArgumentNullException.ThrowIfNull(requestedRedirectUri);
        ArgumentException.ThrowIfNullOrEmpty(expectedState);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_redirectUri is not null)
            throw new InvalidOperationException("Start has already been called on this receiver.");

        // Enforced here rather than trusted from the caller: this is public API, reachable without BrowserRedirectStrategy.Resolve ever running. Shared with Resolve so the two cannot drift.
        BrowserRedirectStrategy.ValidateServable(requestedRedirectUri);

        _expectedState = expectedState;
        string host = BindHostFor(requestedRedirectUri);
        int requestedPort = PortToBind(requestedRedirectUri);
        int boundPort = BindListeners(BindAddressesFor(host), requestedPort, host);

        _redirectUri = new UriBuilder(requestedRedirectUri) { Host = host, Port = boundPort }.Uri;
        _callbackPath = EnsureTrailingSlash(_redirectUri.AbsolutePath);

        foreach (Socket listener in _listeners)
            _ = AcceptLoopAsync(listener, _shutdown.Token);

        return _redirectUri;
    }

    /// <inheritdoc />
    public async Task<string> WaitForCodeAsync(CancellationToken ct)
    {
        if (_redirectUri is null)
            throw new InvalidOperationException("Start must be called before WaitForCodeAsync.");

        RedirectParameters redirect = await _redirect.Task.WaitAsync(ct).ConfigureAwait(false);

        // Unreachable while the door check above is intact, and kept as the second lock rather than the first: if a future edit ever loosens ServeOneRequestAsync, this turns "an attacker's code gets exchanged for a token" back into a loud failure. It is deliberately NOT the only check, since failing here is exactly the abort a hostile local client wants.
        if (!StateMatches(redirect.State))
            throw new AuthServiceException("browser_redirect", 0, "The browser redirect state did not match the request.");

        if (redirect.Error is not null)
            throw new AuthServiceException("browser_redirect", 0, "The browser flow returned error code '" + redirect.Error + "'.");

        if (string.IsNullOrEmpty(redirect.Code))
            throw new AuthServiceException("browser_redirect", 0, "The browser redirect contained no authorization code.");

        return redirect.Code;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;

        // Cancelled rather than faulted on purpose: a caller that races this receiver against another source of the code (MinecraftAuthFlow does) abandons the losing task, and a CANCELLED task is never reported as an unobserved task exception, where a faulted one would be.
        _redirect.TrySetCanceled();

        _shutdown.Cancel();
        foreach (Socket listener in _listeners)
            listener.Dispose();

        _listeners.Clear();
        _shutdown.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>The host to advertise and bind: <c>localhost</c> and IPv4 loopback literals survive verbatim, IPv6 loopback literals become <c>127.0.0.1</c>, and any non-loopback host is forced to <c>127.0.0.1</c>.</summary>
    private static string BindHostFor(Uri requested)
    {
        if (!requested.IsLoopback || requested.HostNameType == UriHostNameType.IPv6)
            return "127.0.0.1";

        return requested.Host;
    }

    /// <summary>The port to ask the OS for: 0 means "any free port".</summary>
    /// <remarks>A URI written without a port carries the scheme's default (80 or 443). On the port-agnostic host that is harmless - Microsoft ignores the port when matching a <c>localhost</c> registration, so a free port is taken instead of a privileged one nobody can bind. On any other host it is a dead end in both directions: the port must match the registration exactly there, so substituting a free port would manufacture the very shape <see cref="BrowserRedirectStrategy"/> refuses, and keeping port 80 needs privileges this process will not ask for. That is refused by name, here, before anything is bound and before the caller opens a browser.</remarks>
    private static int PortToBind(Uri requested)
    {
        if (requested.Port == 0)
            return 0;

        if (!requested.IsDefaultPort)
            return requested.Port;

        if (BrowserRedirectStrategy.IsPortAgnosticHost(requested))
            return 0;

        throw new AuthException(
            "The browser sign-in redirect URI '" + requested.OriginalString + "' has no port, so it "
            + "carries the default port " + requested.Port.ToString(CultureInfo.InvariantCulture)
            + " for its scheme. Only the host '" + BrowserRedirectStrategy.PortAgnosticLoopbackHost
            + "' may be served on a different port than it advertises, because Microsoft ignores the "
            + "port only when matching a '" + BrowserRedirectStrategy.PortAgnosticLoopbackHost
            + "' redirect URI; on host '" + requested.Host + "' the port must match the registration "
            + "exactly, and binding a default port needs privileges this process does not take. Give "
            + "the redirect URI an explicit port that is registered for the application, use a redirect "
            + "URI on '" + BrowserRedirectStrategy.PortAgnosticLoopbackHost + "', or sign in with the "
            + "device-code flow instead.");
    }

    /// <summary>The loopback addresses to bind for <paramref name="host"/>. An IP literal is bound in its own family only, since the browser dials exactly the address it was handed. A NAME is resolved on the browser's side, out of our sight, so both canonical loopback addresses are bound.</summary>
    private static IReadOnlyList<IPAddress> BindAddressesFor(string host) =>
        IPAddress.TryParse(host, out IPAddress? literal)
            ? [literal]
            : [IPAddress.Loopback, IPAddress.IPv6Loopback];

    /// <summary>Binds every address in <paramref name="addresses"/> to one shared port and returns it. The first address settles the port (an OS-assigned one included); the rest must join it.</summary>
    private int BindListeners(IReadOnlyList<IPAddress> addresses, int port, string host)
    {
        for (int attempt = PortPairingAttempts; ; attempt--)
        {
            List<Socket> bound = [];
            try
            {
                Socket primary = Listen(addresses[0], port);
                bound.Add(primary);
                int assigned = ((IPEndPoint)primary.LocalEndPoint!).Port;

                for (int i = 1; i < addresses.Count; i++)
                    if (TryListen(addresses[i], assigned) is { } secondary)
                        bound.Add(secondary);

                _listeners.AddRange(bound);
                return assigned;
            }
            catch (SocketException ex)
            {
                foreach (Socket socket in bound)
                    socket.Dispose();

                // Only an OS-assigned port can be retried, and only against the one failure retrying can fix: another process claiming the port between the first bind and the second.
                if (port != 0 || attempt <= 1 || ex.SocketErrorCode != SocketError.AddressAlreadyInUse)
                    throw new AuthException(BindFailureMessage(host, port, ex), ex);

            }
        }
    }

    /// <summary>Binds <paramref name="address"/>, or returns null when this machine has no usable socket in that family. A machine with IPv6 switched off must still be able to sign in over IPv4.</summary>
    private static Socket? TryListen(IPAddress address, int port)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
            return null;

        try
        {
            return Listen(address, port);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressFamilyNotSupported
            or SocketError.AddressNotAvailable
            or SocketError.OperationNotSupported
            or SocketError.ProtocolNotSupported)
        {
            return null;
        }
    }

    private static Socket Listen(IPAddress address, int port)
    {
        // Bound to one specific loopback address of its own family, so the two sockets never collide and the IPv6 one never picks up IPv4 traffic. SO_REUSEADDR is deliberately not set: a port another process holds must be reported, not quietly shared.
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(address, port));
            socket.Listen(16);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string BindFailureMessage(string host, int port, SocketException ex) =>
        "The browser sign-in listener could not bind '" + host
        + (port == 0 ? "' on an OS-assigned port" : ":" + port.ToString(CultureInfo.InvariantCulture) + "'")
        + " (" + ex.SocketErrorCode + "). Another process may hold that port, or binding it may need "
        + "privileges this process does not take. Free the port, configure a different registered "
        + "redirect port, or sign in with the device-code flow instead.";

    private async Task AcceptLoopAsync(Socket listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket connection;
            try
            {
                connection = await listener.AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            // Not awaited: a browser opens speculative connections and may hold one open without sending anything, and the redirect can arrive on the next one.
            _ = ServeAsync(connection, ct);
        }
    }

    private async Task ServeAsync(Socket connection, CancellationToken ct)
    {
        try
        {
            using (connection)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(RequestTimeout);
                await ServeOneRequestAsync(connection, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shut down, or the peer sat on an open connection without ever sending a request.
        }
        catch (SocketException)
        {
            // The peer went away mid-request. Nothing to report and nothing to clean up.
        }
        catch (ObjectDisposedException)
        {
            // The receiver was disposed while this connection was in flight.
        }
    }

    private async Task ServeOneRequestAsync(Socket connection, CancellationToken ct)
    {
        string? head = await ReadRequestHeadAsync(connection, ct).ConfigureAwait(false);
        if (head is null)
        {
            // Closed before a request arrived (a port probe), or a head too large to be a redirect.
            return;
        }

        int lineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
        string requestLine = lineEnd < 0 ? head : head[..lineEnd];
        string[] parts = requestLine.Split(' ');
        if (parts.Length < 2)
        {
            await RespondAsync(connection, "400 Bad Request", "Bad request.", ct).ConfigureAwait(false);
            return;
        }

        string target = parts[1];

        // A request line may carry an absolute target ("GET http://localhost:P/signin/?... HTTP/1.1") rather than an origin-form path; a client routed through a proxy writes it that way. The scheme has to be checked first: on Unix a rooted path is itself a valid absolute URI (a "file:" one), and running an origin-form target through that parser loses its query string.
        if ((target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            && Uri.TryCreate(target, UriKind.Absolute, out Uri? absoluteTarget))
            target = absoluteTarget.PathAndQuery;

        int queryStart = target.IndexOf('?');
        string path = PercentDecode(queryStart < 0 ? target : target[..queryStart]);

        // Only the path is matched. The sockets are loopback-only, and matching the Host header would reject a valid browser redirect when localhost resolves to the other loopback family.
        if (!IsCallbackPath(path))
        {
            await RespondAsync(connection, "404 Not Found", "Not found.", ct).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(parts[0], "GET", StringComparison.Ordinal))
        {
            await RespondAsync(connection, "405 Method Not Allowed", "The sign-in redirect must be a GET.", ct)
                .ConfigureAwait(false);
            return;
        }

        RedirectParameters redirect = RedirectParameters.Parse(queryStart < 0 ? string.Empty : target[(queryStart + 1)..]);

        // The state gates the HANDOVER itself, not just the result. Anything on this machine can reach a loopback port, so a request that fails this check is turned away WITHOUT consuming the one-shot slot and the receiver stays armed for the real browser. Validating after handing the slot over instead made "connect first with any wrong state" a reliable way for any local process to abort a legitimate sign-in: the slot was spent, the browser's own correct redirect was then discarded, and the wait faulted. The error= branch is gated too, or it is the same abort primitive spelled differently.
        if (!StateMatches(redirect.State))
        {
            await RespondAsync(
                connection, "400 Bad Request", "This request does not match the pending sign-in.", ct)
                .ConfigureAwait(false);
            return;
        }

        await RespondAsync(
            connection,
            "200 OK",
            redirect.Error is null ? "Sign-in complete. You can close this window." : "Sign-in failed.",
            ct).ConfigureAwait(false);

        // Handed over only after the page is on the wire: WaitForCodeAsync may still reject what was parsed (a declined sign-in), and the person looking at the browser should be told what happened either way.
        _redirect.TrySetResult(redirect);
    }

    /// <summary>True when <paramref name="state"/> is the state this receiver was armed with.</summary>
    /// <remarks>Compared in time independent of how many leading characters matched. The state is this path's CSRF token and it is being compared against attacker-chosen input that can be retried freely, so the comparison should not leak how close a guess was. (Lengths are not hidden, which is fine: the length of a GUID is not the secret.)</remarks>
    private bool StateMatches(string? state)
    {
        if (state is null)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(_expectedState));
    }

    /// <summary>True when <paramref name="path"/> is at or under the advertised callback path, matching the prefix semantics a redirect URI has.</summary>
    private bool IsCallbackPath(string path) =>
        path.StartsWith(_callbackPath, StringComparison.Ordinal)
        || (path + "/").StartsWith(_callbackPath, StringComparison.Ordinal);

    /// <summary>Reads up to the end of the request head, or returns null if the peer closed first or sent more than a redirect could possibly be.</summary>
    private static async Task<string?> ReadRequestHeadAsync(Socket connection, CancellationToken ct)
    {
        byte[] head = new byte[MaxRequestHeadBytes];
        int filled = 0;
        while (filled < head.Length)
        {
            int read = await connection.ReceiveAsync(head.AsMemory(filled), SocketFlags.None, ct).ConfigureAwait(false);
            if (read == 0)
                return null;

            filled += read;
            int end = head.AsSpan(0, filled).IndexOf("\r\n\r\n"u8);
            if (end >= 0)
                return Encoding.ASCII.GetString(head, 0, end);

        }

        return null;
    }

    private static async Task RespondAsync(Socket connection, string status, string message, CancellationToken ct)
    {
        byte[] body = Encoding.UTF8.GetBytes(
            "<!doctype html><html><body><p>" + WebUtility.HtmlEncode(message) + "</p></body></html>");
        byte[] head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 " + status + "\r\n"
            + "Content-Type: text/html; charset=utf-8\r\n"
            + "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n"
            + "Connection: close\r\n\r\n");

        await SendAllAsync(connection, head, ct).ConfigureAwait(false);
        await SendAllAsync(connection, body, ct).ConfigureAwait(false);

        // The response has no other end marker, so the client is told the body is complete by FIN.
        connection.Shutdown(SocketShutdown.Send);
    }

    private static async Task SendAllAsync(Socket connection, byte[] data, CancellationToken ct)
    {
        int sent = 0;
        while (sent < data.Length)
            sent += await connection.SendAsync(data.AsMemory(sent), SocketFlags.None, ct).ConfigureAwait(false);

    }

    /// <summary>Strict percent-decoding of a URI component: <c>%2B</c> becomes <c>+</c> and a literal <c>+</c> stays a <c>+</c>.</summary>
    /// <remarks>Deliberately NOT <see cref="WebUtility.UrlDecode"/>, which applies <c>application/x-www-form-urlencoded</c> rules and turns a literal <c>+</c> into a space. That silently corrupted any authorization code containing one (measured: <c>ABC+DEF</c> arrived as <c>ABC DEF</c>), with no diagnostic anywhere. A query string in a redirect URI is not a form body, and a path never had plus-as-space semantics at all.</remarks>
    private static string PercentDecode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            // A malformed escape from an untrusted client is not worth failing the connection over: the raw text simply will not match the expected state, or will not be a usable code.
            return value;
        }
    }

    private static string EnsureTrailingSlash(string path) =>
        string.IsNullOrEmpty(path) ? "/" : (path.EndsWith('/') ? path : path + "/");

    /// <summary>The parameters a sign-in redirect can carry, decoded from the query string.</summary>
    private sealed record RedirectParameters(string? Code, string? State, string? Error)
    {
        public static RedirectParameters Parse(string query)
        {
            string? code = null;
            string? state = null;
            string? error = null;

            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                string name = equals < 0 ? pair : pair[..equals];
                string value = equals < 0 ? string.Empty : PercentDecode(pair[(equals + 1)..]);

                // First occurrence wins; a second "code" is not something a redirect legitimately has.
                switch (name)
                {
                    case "code":
                        code ??= value;
                        break;
                    case "state":
                        state ??= value;
                        break;
                    case "error":
                        error ??= value;
                        break;
                    default:
                        break;
                }
            }

            return new RedirectParameters(code, state, error);
        }
    }
}

/// <summary>Creates <see cref="HttpListenerLoopbackReceiver"/> instances.</summary>
[UnsupportedOSPlatform("browser")]
public sealed class HttpListenerLoopbackReceiverFactory : ILoopbackCodeReceiverFactory
{
    /// <summary>The shared factory instance.</summary>
    public static HttpListenerLoopbackReceiverFactory Instance { get; } = new();

    /// <inheritdoc />
    public ILoopbackCodeReceiver Create() => new HttpListenerLoopbackReceiver();
}
