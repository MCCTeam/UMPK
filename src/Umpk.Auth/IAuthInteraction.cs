namespace Umpk.Auth;

/// <summary>The host-rendered interaction seam for interactive login flows. The library never touches the console; a CLI, GUI, or web host implements this to show a device code, open a browser, or supply credentials. All members are cancellable.</summary>
public interface IAuthInteraction
{
    /// <summary>Called once at the start of a device-code flow so the host can display the code and URL to the user. Returning completes the display step; polling then proceeds internally.</summary>
    Task ShowDeviceCodeAsync(DeviceCodePrompt prompt, CancellationToken ct);

    /// <summary>Called during the browser auth-code flow. The host opens or shows <paramref name="signInUrl"/> and returns the authorization code once the user has signed in. A loopback-listener host may ignore the return value and complete the flow out of band, in which case it should still return the captured code.</summary>
    Task<string> GetBrowserAuthCodeAsync(Uri signInUrl, CancellationToken ct);

    /// <summary>Called by the Yggdrasil flow to obtain the account username and password. Implemented by the host so credentials never pass through library-owned configuration.</summary>
    Task<YggdrasilCredentials> GetYggdrasilCredentialsAsync(CancellationToken ct);
}
