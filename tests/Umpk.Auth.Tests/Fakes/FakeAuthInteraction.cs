using Umpk.Auth;

namespace Umpk.Auth.Tests.Fakes;

/// <summary>A test <see cref="IAuthInteraction"/>. Records the device-code prompt and the browser sign-in URL, and returns pre-seeded values. The browser callback returns an empty string by default so the loopback receiver drives the code capture (matching a loopback host); set <see cref="BrowserAuthCode"/> to return a code directly instead.</summary>
public sealed class FakeAuthInteraction : IAuthInteraction
{
    public DeviceCodePrompt? ShownPrompt { get; private set; }

    public Uri? BrowserSignInUrl { get; private set; }

    public string BrowserAuthCode { get; init; } = "";

    public YggdrasilCredentials Credentials { get; init; } = new("player", "pw");

    public Task ShowDeviceCodeAsync(DeviceCodePrompt prompt, CancellationToken ct)
    {
        ShownPrompt = prompt;
        return Task.CompletedTask;
    }

    public Task<string> GetBrowserAuthCodeAsync(Uri signInUrl, CancellationToken ct)
    {
        BrowserSignInUrl = signInUrl;
        return Task.FromResult(BrowserAuthCode);
    }

    public Task<YggdrasilCredentials> GetYggdrasilCredentialsAsync(CancellationToken ct) =>
        Task.FromResult(Credentials);
}
