using Umpk.Auth;
using Xunit;

namespace Umpk.Auth.Tests;

/// <summary>Guards the browser flow's redirect URI, which is the one field Microsoft rejects the whole sign-in request over.</summary>
/// <remarks>
/// <para>The client id has no registered loopback redirect URI, and an OS-assigned <c>http://127.0.0.1:0/signin/</c> address is rejected. Two constraints apply:</para>
/// <list type="number">
/// <item>no loopback URI is registered for this client id at all; and</item>
/// <item>
/// even if one were, Microsoft ignores the port when matching a redirect URI ONLY for the host <c>localhost</c> ("This is only true for localhost redirect URIs. In all other cases, the port component is not ignored when matching redirect URIs" - https://learn.microsoft.com/en-us/entra/identity-platform/reply-url). So a random port on <c>127.0.0.1</c> cannot match ANY registration.
/// </item>
/// </list>
/// <para>These tests assert the concrete registered URI rather than merely that "a redirect URI was set", and fail if the random-port loopback shape is reintroduced.</para>
/// </remarks>
public sealed class BrowserRedirectStrategyTests
{
    /// <summary>The redirect URI registered for the default Azure application. Nothing in this project can register another URI for that application, so this exact value is the contract.</summary>
    private const string RegisteredRedirectUri = "https://mccteam.github.io/redirect.html";

    [Fact]
    public void DefaultRedirectUri_IsTheRegisteredHostedPage()
    {
        Assert.Equal(RegisteredRedirectUri, MinecraftAuthOptions.DefaultBrowserRedirectUri.AbsoluteUri);
        Assert.Equal(RegisteredRedirectUri, new MinecraftAuthOptions().BrowserRedirectUri.AbsoluteUri);
    }

    /// <summary>The default must not be a loopback URI because no loopback URI is registered for this client id.</summary>
    [Fact]
    public void DefaultRedirectUri_IsNotLoopback()
    {
        Uri fallback = new MinecraftAuthOptions().BrowserRedirectUri;

        Assert.False(fallback.IsLoopback, "no loopback redirect URI is registered for the bundled client id");
        Assert.Equal(Uri.UriSchemeHttps, fallback.Scheme);
        Assert.Equal(BrowserRedirectMode.HostedPastePage, BrowserRedirectStrategy.Resolve(fallback));
    }

    /// <summary>The exact shape the maintainer hit. An OS-assigned port on a loopback host that is not <c>localhost</c> can never match a registration, so it must be refused up front rather than turned into a sign-in URL.</summary>
    [Theory]
    [InlineData("http://127.0.0.1:0/signin/")]
    [InlineData("http://127.0.0.1:0/")]
    [InlineData("https://127.0.0.1:0/callback/")]
    [InlineData("http://[::1]:0/signin/")]
    public void RandomPortLoopback_OnNonLocalhostHost_IsRefused(string redirectUri)
    {
        var uri = new Uri(redirectUri);

        AuthException ex = Assert.Throws<AuthException>(() => BrowserRedirectStrategy.Resolve(uri));

        // The refusal has to tell the user what to do, not merely that something failed.
        Assert.Contains("localhost", ex.Message, StringComparison.Ordinal);
        Assert.Contains("device-code", ex.Message, StringComparison.Ordinal);
    }

    /// <summary><c>localhost</c> is the documented exception: the port is ignored when matching, so an OS-assigned port is legitimate for a caller who registered a localhost redirect URI.</summary>
    [Theory]
    [InlineData("http://localhost:0/signin/")]
    [InlineData("http://LOCALHOST:0/signin/")]
    public void RandomPortLoopback_OnLocalhost_IsAllowed(string redirectUri)
    {
        Assert.Equal(BrowserRedirectMode.LoopbackListener, BrowserRedirectStrategy.Resolve(new Uri(redirectUri)));
    }

    /// <summary>A FIXED port on 127.0.0.1 is registrable (via the application manifest's <c>replyUrlsWithType</c>), so the refusal must stay narrow and not reject it.</summary>
    [Theory]
    [InlineData("http://127.0.0.1:5123/signin/")]
    [InlineData("http://localhost:5123/signin/")]
    public void FixedPortLoopback_IsAllowed(string redirectUri)
    {
        Assert.Equal(BrowserRedirectMode.LoopbackListener, BrowserRedirectStrategy.Resolve(new Uri(redirectUri)));
    }

    [Theory]
    [InlineData("https://mccteam.github.io/redirect.html")]
    [InlineData("https://example.invalid/callback")]
    public void NonLoopbackRedirect_UsesHostedPastePage(string redirectUri)
    {
        Assert.Equal(BrowserRedirectMode.HostedPastePage, BrowserRedirectStrategy.Resolve(new Uri(redirectUri)));
    }

    [Fact]
    public void IsPortAgnosticHost_IsTrueOnlyForLocalhost()
    {
        Assert.True(BrowserRedirectStrategy.IsPortAgnosticHost(new Uri("http://localhost:1/x")));
        Assert.False(BrowserRedirectStrategy.IsPortAgnosticHost(new Uri("http://127.0.0.1:1/x")));
        Assert.False(BrowserRedirectStrategy.IsPortAgnosticHost(new Uri("http://[::1]:1/x")));
    }

    /// <summary>An IPv6 loopback literal with a usable port is accepted (the receiver maps it down to the IPv4 literal, since Microsoft does not support an IPv6 literal as a redirect host). Only the unusable-port cases are refused, and for the port reason.</summary>
    [Theory]
    [InlineData("http://[::1]:5123/signin/")]
    [InlineData("http://[::ffff:127.0.0.1]:5124/signin/")]
    public void IPv6LoopbackLiteral_WithUsablePort_IsAllowed(string redirectUri)
    {
        Assert.Equal(BrowserRedirectMode.LoopbackListener, BrowserRedirectStrategy.Resolve(new Uri(redirectUri)));
    }

    /// <summary>A loopback redirect URI written with no port carries the scheme's DEFAULT port (80 or 443). On any host other than <c>localhost</c> that is unusable twice over: the port must match the registration exactly there, and no unprivileged process can bind 80 or 443 anyway. It must be refused here, before a browser is opened, rather than dying inside the receiver's bind.</summary>
    /// <remarks>Taking a free port instead (the way <c>localhost</c> does) would be wrong for these hosts: it would manufacture exactly the random-port-on-an-IP-literal shape that <see cref="RandomPortLoopback_OnNonLocalhostHost_IsRefused"/> exists to refuse, and Microsoft's port waiver does not extend to an IPv6 literal, which it does not accept as a redirect host at all.</remarks>
    [Theory]
    [InlineData("http://127.0.0.1/signin/")]
    [InlineData("http://[::1]/signin/")]
    [InlineData("https://127.0.0.1/callback")]
    [InlineData("http://[::ffff:127.0.0.1]/signin/")]
    public void DefaultPortLoopback_OnNonLocalhostHost_IsRefused(string redirectUri)
    {
        AuthException ex = Assert.Throws<AuthException>(() => BrowserRedirectStrategy.Resolve(new Uri(redirectUri)));

        Assert.Contains("localhost", ex.Message, StringComparison.Ordinal);
        Assert.Contains("device-code", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The same shape on <c>localhost</c> stays allowed: the port is ignored when matching there, so the receiver is free to bind a real one.</summary>
    [Theory]
    [InlineData("http://localhost/signin/")]
    [InlineData("http://LOCALHOST/signin/")]
    public void DefaultPortLoopback_OnLocalhost_IsAllowed(string redirectUri)
    {
        Assert.Equal(BrowserRedirectMode.LoopbackListener, BrowserRedirectStrategy.Resolve(new Uri(redirectUri)));
    }

    /// <summary>Schemes the local listener cannot serve must be refused by name here, not left to die inside HttpListener with "Only Uri prefixes with a valid hostname are supported". Note <c>file:///x</c> reaches this check looking like loopback: its host is empty, so <c>Uri.IsLoopback</c> is true.</summary>
    [Theory]
    [InlineData("file:///tmp/x")]
    [InlineData("myapp://localhost/callback")]
    [InlineData("ftp://127.0.0.1:5123/cb")]
    public void NonHttpScheme_IsRefused(string redirectUri)
    {
        AuthException ex = Assert.Throws<AuthException>(() => BrowserRedirectStrategy.Resolve(new Uri(redirectUri)));
        Assert.Contains("scheme", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A relative URI must be refused as an <see cref="AuthException"/>; <c>Uri.IsLoopback</c> throws <see cref="InvalidOperationException"/> on one, which is not a documented failure of this API.</summary>
    [Fact]
    public void RelativeUri_IsRefused()
    {
        AuthException ex = Assert.Throws<AuthException>(
            () => BrowserRedirectStrategy.Resolve(new Uri("/signin/", UriKind.Relative)));
        Assert.Contains("relative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => BrowserRedirectStrategy.Resolve(null!));
}
