using Microsoft.Extensions.Logging;
using Umpk.Auth.Internal;
using Umpk.Auth.Persistence;
using Umpk.Protocol.Java.Signing;

namespace Umpk.Auth;

/// <summary>The single login orchestrator. Runs the flow selected by <see cref="MinecraftAuthOptions.FlowKind"/>, caches sessions and certificates through the configured <see cref="ITokenStore"/>, and refreshes expired Microsoft tokens transparently. Host interaction (device code, browser, credentials) is delegated to <see cref="IAuthInteraction"/>; the library performs no console I/O and logs no tokens. Stays sealed; <see cref="IMinecraftAuthFlow"/> is the seam a consumer fakes against.</summary>
public sealed class MinecraftAuthFlow : IMinecraftAuthFlow
{
    private const string SessionKeyPrefix = "session:";
    private const string CertificatesKeyPrefix = "certificates:";

    private readonly MinecraftAuthOptions _options;
    private readonly AuthHttpClient _http;
    private readonly MicrosoftAuthenticator _microsoft;
    private readonly CertificatesClient _certificates;

    public MinecraftAuthFlow(MinecraftAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _http = new AuthHttpClient(options.HttpHandlerFactory);
        _microsoft = new MicrosoftAuthenticator(_http, options.ClientId, options.TimeProvider, options.Logger);
        _certificates = new CertificatesClient(_http, options.YggdrasilBaseUrl);
    }

    /// <summary>Runs the configured login flow to completion and caches the resulting session.</summary>
    /// <param name="interaction">The host interaction used to surface prompts.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="loginHint">The identifier the CALLER will later pass to <see cref="TryResumeAsync"/>, typically the configured account login (an email for a Microsoft account). The session is always cached under the resolved Minecraft profile name, but for a Microsoft account that name (the gamertag) is NOT the login the caller knows, so a resume by login would miss and force a fresh interactive sign-in on every start. Supplying the hint caches the session under BOTH keys. Found in live testing.</param>
    public async Task<JavaSession> LoginAsync(
        IAuthInteraction interaction, CancellationToken ct, string? loginHint = null)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        JavaSession session = _options.FlowKind switch
        {
            AuthFlowKind.MicrosoftDeviceCode => await LoginDeviceCodeAsync(interaction, ct).ConfigureAwait(false),
            AuthFlowKind.MicrosoftBrowser => await LoginBrowserAsync(interaction, ct).ConfigureAwait(false),
            AuthFlowKind.Yggdrasil => await LoginYggdrasilAsync(interaction, ct).ConfigureAwait(false),
            AuthFlowKind.Offline => LoginOffline(),
            _ => throw new AuthException("Unsupported auth flow kind: " + _options.FlowKind),
        };

        await CacheSessionAsync(LoginHintFor(session), session, ct).ConfigureAwait(false);
        await CacheAliasAsync(loginHint, session, ct).ConfigureAwait(false);
        return session;
    }

    /// <summary>Attempts to reuse a cached session for <paramref name="loginHint"/>. A still-valid session is returned as-is; a Microsoft session with a refresh token is refreshed and re-cached; anything else returns null so the caller falls back to <see cref="LoginAsync"/>.</summary>
    public async Task<JavaSession?> TryResumeAsync(string loginHint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(loginHint);

        JavaSession? cached = await _options.TokenStore.GetAsync<JavaSession>(SessionKey(loginHint), ct).ConfigureAwait(false);
        if (cached is null)
            return null;

        if (!cached.IsExpired(_options.TimeProvider.GetUtcNow()))
            return cached;

        if (cached.Kind == AuthKind.Microsoft && cached.RefreshToken is { } refresh)
        {
            _options.Logger.LogDebug("Cached Microsoft session expired; refreshing.");
            MsaToken msa = await _microsoft.RefreshAsync(refresh, ct).ConfigureAwait(false);
            JavaSession refreshed = await _microsoft.CompleteChainAsync(msa, ct).ConfigureAwait(false);
            await CacheSessionAsync(loginHint, refreshed, ct).ConfigureAwait(false);
            await CacheAliasAsync(LoginHintFor(refreshed), refreshed, ct).ConfigureAwait(false);
            return refreshed;
        }

        return null;
    }

    /// <summary>Returns the player profile-key certificates for a session, using the cache when present and unexpired, otherwise fetching and caching them.</summary>
    public async Task<PlayerCertificates> GetCertificatesAsync(JavaSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        string key = CertificatesKey(LoginHintFor(session));
        PlayerCertificates? cached = await _options.TokenStore.GetAsync<PlayerCertificates>(key, ct).ConfigureAwait(false);
        if (cached is not null && !cached.IsExpired(_options.TimeProvider.GetUtcNow()))
            return cached;

        PlayerCertificates fetched = await _certificates.FetchAsync(session.AccessToken, ct).ConfigureAwait(false);
        await _options.TokenStore.SetAsync(key, fetched, ct).ConfigureAwait(false);
        return fetched;
    }

    /// <summary>Removes any cached session and certificates for <paramref name="loginHint"/>.</summary>
    public async Task InvalidateAsync(string loginHint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(loginHint);
        await _options.TokenStore.RemoveAsync(SessionKey(loginHint), ct).ConfigureAwait(false);
        await _options.TokenStore.RemoveAsync(CertificatesKey(loginHint), ct).ConfigureAwait(false);
    }

    private async Task<JavaSession> LoginDeviceCodeAsync(IAuthInteraction interaction, CancellationToken ct)
    {
        DeviceCodeResult device = await _microsoft.RequestDeviceCodeAsync(ct).ConfigureAwait(false);
        DateTimeOffset expiresAt = _options.TimeProvider.GetUtcNow() + TimeSpan.FromSeconds(device.ExpiresInSeconds);
        var prompt = new DeviceCodePrompt(device.UserCode, device.VerificationUri, device.Message, expiresAt);
        await interaction.ShowDeviceCodeAsync(prompt, ct).ConfigureAwait(false);

        MsaToken msa = await _microsoft.PollDeviceCodeAsync(device, ct).ConfigureAwait(false);
        return await _microsoft.CompleteChainAsync(msa, ct).ConfigureAwait(false);
    }

    private async Task<JavaSession> LoginBrowserAsync(IAuthInteraction interaction, CancellationToken ct)
    {
        // Refuses before any browser is opened when the configured redirect URI is one Microsoft can never match, so the user sees a message naming the reason instead of a Microsoft error page.
        BrowserRedirectMode mode = BrowserRedirectStrategy.Resolve(_options.BrowserRedirectUri);
        return mode == BrowserRedirectMode.LoopbackListener
            ? await LoginBrowserLoopbackAsync(interaction, ct).ConfigureAwait(false)
            : await LoginBrowserPastePageAsync(interaction, ct).ConfigureAwait(false);
    }

    /// <summary>The default browser flow. The code comes back in the URL fragment, so it is never sent to a server and no local listener is involved; the hosted page shows it and the host reads it back.</summary>
    private async Task<JavaSession> LoginBrowserPastePageAsync(IAuthInteraction interaction, CancellationToken ct)
    {
        // Sent for symmetry with the loopback path, and unvalidated on this path: the user carries the code over by hand, so there is no redirect for us to check it against.
        string state = Guid.NewGuid().ToString("N");
        Uri redirectUri = _options.BrowserRedirectUri;
        Uri signInUrl = _microsoft.BuildAuthorizeUrl(redirectUri, state, BrowserRedirectMode.HostedPastePage);

        string code = await interaction.GetBrowserAuthCodeAsync(signInUrl, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(code))
            throw new AuthException(
                "No authorization code was supplied for the browser sign-in. Complete the sign-in in the "
                + "browser, then paste the code the page displays.");

        MsaToken msa = await _microsoft.ExchangeAuthCodeAsync(code.Trim(), redirectUri, ct).ConfigureAwait(false);
        return await _microsoft.CompleteChainAsync(msa, ct).ConfigureAwait(false);
    }

    /// <summary>The loopback variant, used when the caller configured a loopback redirect URI registered against their own Azure application. A local one-shot listener captures the redirect so nothing is pasted.</summary>
    private async Task<JavaSession> LoginBrowserLoopbackAsync(IAuthInteraction interaction, CancellationToken ct)
    {
        string state = Guid.NewGuid().ToString("N");
        await using ILoopbackCodeReceiver receiver = _options.LoopbackReceiverFactory.Create();
        // The state is handed to the receiver up front so it can refuse a redirect that does not carry it WITHOUT spending its one-shot slot; otherwise any local process could abort this sign-in by connecting first with a wrong state.
        Uri redirectUri = receiver.Start(_options.BrowserRedirectUri, state);
        Uri signInUrl = _microsoft.BuildAuthorizeUrl(redirectUri, state, BrowserRedirectMode.LoopbackListener);

        // The host opens the browser. A loopback host may return the code directly; otherwise it returns empty and the loopback receiver captures the redirect.
        Task<string> hostTask = interaction.GetBrowserAuthCodeAsync(signInUrl, ct);
        Task<string> receiverTask = receiver.WaitForCodeAsync(ct);

        Task<string> completed = await Task.WhenAny(hostTask, receiverTask).ConfigureAwait(false);
        string code = await completed.ConfigureAwait(false);
        if (string.IsNullOrEmpty(code) && completed == hostTask)
            code = await receiverTask.ConfigureAwait(false);

        MsaToken msa = await _microsoft.ExchangeAuthCodeAsync(code, redirectUri, ct).ConfigureAwait(false);
        return await _microsoft.CompleteChainAsync(msa, ct).ConfigureAwait(false);
    }

    private async Task<JavaSession> LoginYggdrasilAsync(IAuthInteraction interaction, CancellationToken ct)
    {
        if (_options.YggdrasilBaseUrl is null)
            throw new AuthException("Yggdrasil flow requires MinecraftAuthOptions.YggdrasilBaseUrl.");

        YggdrasilCredentials credentials = await interaction.GetYggdrasilCredentialsAsync(ct).ConfigureAwait(false);
        string clientToken = Guid.NewGuid().ToString("N");
        var authenticator = new YggdrasilAuthenticator(_http, _options.YggdrasilBaseUrl, _options.TimeProvider);
        return await authenticator.AuthenticateAsync(credentials, clientToken, ct).ConfigureAwait(false);
    }

    private JavaSession LoginOffline()
    {
        string name = _options.OfflineUsername
            ?? throw new AuthException("Offline flow requires MinecraftAuthOptions.OfflineUsername.");
        GameProfile profile = OfflineIdentity.ComputeProfile(name);
        return new JavaSession(profile, string.Empty, DateTimeOffset.MaxValue, null, AuthKind.Offline);
    }

    private async Task CacheSessionAsync(string loginHint, JavaSession session, CancellationToken ct)
    {
        if (session.Kind == AuthKind.Offline)
            return;

        await _options.TokenStore.SetAsync(SessionKey(loginHint), session, ct).ConfigureAwait(false);
    }

    /// <summary>Caches the session under a SECOND key when the caller's login hint differs from the profile name, so a later <see cref="TryResumeAsync"/> by either identifier hits.</summary>
    private async Task CacheAliasAsync(string? loginHint, JavaSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(loginHint)
            || string.Equals(loginHint, LoginHintFor(session), StringComparison.OrdinalIgnoreCase))
            return;

        await CacheSessionAsync(loginHint, session, ct).ConfigureAwait(false);
    }

    private static string LoginHintFor(JavaSession session) => session.Profile.Name;

    private static string SessionKey(string loginHint) =>
        SessionKeyPrefix + loginHint.ToUpperInvariant();

    private static string CertificatesKey(string loginHint) =>
        CertificatesKeyPrefix + loginHint.ToUpperInvariant();

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
