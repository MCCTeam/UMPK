using System.Globalization;

namespace Umpk.Auth;

/// <summary>How the browser auth-code flow gets the authorization code back from Microsoft for a given redirect URI.</summary>
public enum BrowserRedirectMode
{
    /// <summary>The redirect URI points at a hosted page (not loopback). Microsoft is asked for <c>response_mode=fragment</c>, so the code lands in the URL fragment and is never sent to a server. The page shows the code and the user pastes it back into the host. This is what the default <see cref="MinecraftAuthOptions.BrowserRedirectUri"/> uses.</summary>
    HostedPastePage,

    /// <summary>The redirect URI is a loopback address, so a local one-shot HTTP listener captures the redirect directly and the user pastes nothing. Requires the redirect URI to be registered against the Azure application.</summary>
    LoopbackListener,
}

/// <summary>Decides how a configured browser redirect URI must be used, and refuses the shapes that can never work.</summary>
/// <remarks>
/// <para>Microsoft's identity platform waives redirect-URI port matching ONLY for the host <c>localhost</c>. Its redirect-URI reference states that the port component "is ignored for the purposes of matching a localhost redirect URI", and immediately adds that this is "only true for localhost redirect URIs. In all other cases, the port component is not ignored when matching redirect URIs."</para>
/// <para>So an ephemeral (OS-assigned) port is only survivable on <c>localhost</c>. On an IP literal such as <c>127.0.0.1</c> the port must match the registration exactly, which a randomly chosen port cannot do against any registration whatsoever. That combination is rejected up front by <see cref="Resolve"/> rather than being turned into a sign-in URL that Microsoft answers with <c>invalid_request</c>.</para>
/// <para>The scheme's DEFAULT port (a redirect URI written with no port at all) is rejected on those same hosts for the same reason plus one more: 80 and 443 cannot be bound by an unprivileged process, and the substitution that rescues <c>localhost</c> is not available where the port has to match.</para>
/// <para>See https://learn.microsoft.com/en-us/entra/identity-platform/reply-url, "Localhost exceptions".</para>
/// </remarks>
public static class BrowserRedirectStrategy
{
    /// <summary>The host for which Microsoft ignores the port when matching a redirect URI.</summary>
    /// <remarks>Deliberately <c>static readonly</c> rather than <c>const</c>: a <c>const</c> is baked into every downstream assembly at compile time, so a change here would silently not reach consumers that were not rebuilt.</remarks>
    public static readonly string PortAgnosticLoopbackHost = "localhost";

    /// <summary>Classifies <paramref name="redirectUri"/>.</summary>
    /// <param name="redirectUri">An absolute <c>http</c> or <c>https</c> redirect URI.</param>
    /// <exception cref="ArgumentNullException"><paramref name="redirectUri"/> is null.</exception>
    /// <exception cref="AuthException"><paramref name="redirectUri"/> is relative rather than absolute; or its scheme is not <c>http</c> or <c>https</c>; or it is a loopback address on a host other than <see cref="PortAgnosticLoopbackHost"/> whose port is either OS-assigned or the scheme default, neither of which any Azure application can have registered AND be served from.</exception>
    public static BrowserRedirectMode Resolve(Uri redirectUri)
    {
        ValidateServable(redirectUri);

        if (!redirectUri.IsLoopback)
            return BrowserRedirectMode.HostedPastePage;

        // Every port is serviceable on the port-agnostic host, the scheme default included: the port is ignored when matching there, so the receiver is free to bind a real one instead of 80 or 443.
        if (IsPortAgnosticHost(redirectUri))
            return BrowserRedirectMode.LoopbackListener;

        if (redirectUri.Port == 0)
            throw new AuthException(
                "The browser sign-in redirect URI '" + redirectUri.OriginalString + "' asks for an "
                + "OS-assigned port on host '" + redirectUri.Host + "'. Microsoft ignores the port when "
                + "matching a redirect URI only for the host '" + PortAgnosticLoopbackHost + "'; for any "
                + "other host the port must match the registration exactly, so a randomly chosen port can "
                + "never match. " + Remedies);

        // A URI written without a port carries the scheme's default. On this host that is unusable in both directions: substituting a free port would recreate the unmatchable shape refused just above, and 80/443 cannot be bound by an unprivileged process. Refusing here means the caller never opens a browser for a sign-in that could not have completed.
        if (redirectUri.IsDefaultPort)
            throw new AuthException(
                "The browser sign-in redirect URI '" + redirectUri.OriginalString + "' has no port, so "
                + "it carries the default port " + redirectUri.Port.ToString(CultureInfo.InvariantCulture)
                + " on host '" + redirectUri.Host + "'. That port cannot be bound without privileges, "
                + "and it cannot be substituted either: Microsoft ignores the port when matching a "
                + "redirect URI only for the host '" + PortAgnosticLoopbackHost + "', so on any other "
                + "host the port must match the registration exactly. " + Remedies);

        return BrowserRedirectMode.LoopbackListener;
    }

    /// <summary>The shape rules any servable redirect URI must satisfy, regardless of how it will be served.</summary>
    /// <remarks>Shared by <see cref="Resolve"/> and by <see cref="ILoopbackCodeReceiver.Start"/> implementations so the two cannot drift apart. <c>Start</c> is public API and a caller can reach it without ever going through <see cref="Resolve"/>; when it did not check for itself, a relative URI escaped as a raw <see cref="InvalidOperationException"/> from <see cref="Uri.IsLoopback"/> and an unservable scheme such as <c>ftp</c> was bound as plaintext HTTP and advertised back verbatim.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="redirectUri"/> is null.</exception>
    /// <exception cref="AuthException"><paramref name="redirectUri"/> is relative, or its scheme is neither <c>http</c> nor <c>https</c>.</exception>
    internal static void ValidateServable(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);

        // Both checks come before Uri.IsLoopback, which throws InvalidOperationException on a relative URI and answers true for schemes the listener cannot serve (file:///x has an empty host).
        if (!redirectUri.IsAbsoluteUri)
            throw new AuthException(
                "The browser sign-in redirect URI '" + redirectUri.OriginalString + "' is relative. It "
                + "must be an absolute http or https URL registered for the application.");

        if (!string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(redirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            throw new AuthException(
                "The browser sign-in redirect URI '" + redirectUri.OriginalString + "' uses the scheme '"
                + redirectUri.Scheme + "'. Only http and https redirect URIs are supported; a custom or "
                + "file scheme cannot be served by the local sign-in listener.");

    }

    /// <summary>The ways out, repeated by every refusal so the message always says what to do next.</summary>
    private static string Remedies =>
        "Use a redirect URI on '" + PortAgnosticLoopbackHost + "', or give this one an explicit fixed "
        + "port that is registered for the application, or use the default hosted redirect URI, or sign "
        + "in with the device-code flow instead.";

    /// <summary>True when Microsoft ignores the port when matching <paramref name="redirectUri"/>, which is the case only for the host <see cref="PortAgnosticLoopbackHost"/>.</summary>
    internal static bool IsPortAgnosticHost(Uri redirectUri) =>
        string.Equals(redirectUri.Host, PortAgnosticLoopbackHost, StringComparison.OrdinalIgnoreCase);
}
